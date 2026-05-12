using AI.DocumentAssistant.Application.Abstractions.Documents;
using AI.DocumentAssistant.Application.Documents.Dtos;
using AI.DocumentAssistant.Domain.Entities;
using AI.DocumentAssistant.Domain.Enums;
using AI.DocumentAssistant.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Text.RegularExpressions;

namespace AI.DocumentAssistant.Application.Documents.Services;

public sealed class DocumentFolderDecisionEngine : IDocumentFolderDecisionEngine
{
    private const int MaxSystemFoldersCreatedPerDay = 30;
    private const int MaxPathDepth = 4;
    private const decimal AutoAssignConfidence = 0.68m;
    private const decimal AutoCreateConfidence = 0.70m;
    private const decimal ReviewConfidence = 0.45m;

    private readonly AppDbContext _dbContext;
    private readonly IDocumentIntelligenceService _documentIntelligenceService;

    public DocumentFolderDecisionEngine(
        AppDbContext dbContext,
        IDocumentIntelligenceService documentIntelligenceService)
    {
        _dbContext = dbContext;
        _documentIntelligenceService = documentIntelligenceService;
    }

    public async Task<DocumentFolderDecisionResultDto> DecideAsync(
        Document document,
        DocumentFolderAnalysisResultDto analysis,
        IReadOnlyCollection<DocumentFolder> existingFolders,
        CancellationToken cancellationToken)
    {
        if (document.FolderId is not null)
        {
            await ClearPendingSuggestionsAsync(document.Id, cancellationToken);
            return new DocumentFolderDecisionResultDto
            {
                FolderId = document.FolderId,
                CreatedNewFolder = false,
                AutoAssigned = false,
                Status = "manual",
                Confidence = 1m,
                Reason = "smart_folder.manual_upload_folder_selected"
            };
        }

        if (document.OrganizationMode == DocumentOrganizationMode.Disabled)
        {
            await ClearPendingSuggestionsAsync(document.Id, cancellationToken);
            return new DocumentFolderDecisionResultDto
            {
                FolderId = null,
                CreatedNewFolder = false,
                AutoAssigned = false,
                Status = "disabled",
                Confidence = null,
                Reason = "smart_folder.disabled"
            };
        }

        var structureDecision = await TryResolveFolderFromUploadedStructureAsync(
            document,
            existingFolders,
            cancellationToken);

        if (structureDecision is not null)
        {
            await ClearPendingSuggestionsAsync(document.Id, cancellationToken);
            return structureDecision;
        }

        var proposedPath = ReconcileProposedPathWithExistingTree(
            analysis,
            existingFolders,
            SanitizeProposedPath(analysis.ProposedPath.Count > 0
                ? analysis.ProposedPath
                : analysis.ProposedFolder is null
                    ? Array.Empty<DocumentFolderProposalDto>()
                    : new[] { analysis.ProposedFolder }));

        await _documentIntelligenceService.EnsureSnapshotAsync(document, cancellationToken);
        await ApplyLearningBoostsForReviewOnlyAsync(document, analysis, existingFolders, cancellationToken);
        NormalizeCandidates(analysis);

        var decision = NormalizeDecision(analysis.Decision);
        var confidence = Math.Clamp(analysis.Confidence, 0m, 1m);

        if (decision == "use_existing" &&
            analysis.SuggestedExistingFolderId is Guid existingId &&
            confidence >= AutoAssignConfidence &&
            existingFolders.Any(x => x.Id == existingId))
        {
            await ClearPendingSuggestionsAsync(document.Id, cancellationToken);
            var folder = existingFolders.First(x => x.Id == existingId);
            return new DocumentFolderDecisionResultDto
            {
                FolderId = existingId,
                CreatedNewFolder = false,
                AutoAssigned = true,
                Status = "auto-assigned",
                Confidence = confidence,
                Reason = BuildFinalReason(analysis, "smart_folder.auto_assigned_existing")
            };
        }

        if (decision == "create_path" && proposedPath.Count > 0)
        {
            var existingLeaf = ResolveExistingPath(existingFolders, proposedPath);
            if (existingLeaf is not null && confidence >= AutoAssignConfidence)
            {
                await ClearPendingSuggestionsAsync(document.Id, cancellationToken);
                return new DocumentFolderDecisionResultDto
                {
                    FolderId = existingLeaf.Id,
                    CreatedNewFolder = false,
                    AutoAssigned = true,
                    Status = "auto-assigned",
                    Confidence = confidence,
                    Reason = BuildFinalReason(analysis, "smart_folder.auto_assigned_existing_path")
                };
            }

            if (document.OrganizationMode == DocumentOrganizationMode.AutoAssignOrCreate &&
                confidence >= AutoCreateConfidence &&
                await CanAutoCreateFolderPathAsync(document.UserId, proposedPath, existingFolders, cancellationToken))
            {
                var leaf = await GetOrCreateFolderPathAsync(
                    document.UserId,
                    proposedPath,
                    cancellationToken);

                await ClearPendingSuggestionsAsync(document.Id, cancellationToken);

                return new DocumentFolderDecisionResultDto
                {
                    FolderId = leaf.Id,
                    CreatedNewFolder = leaf.IsSystemGenerated,
                    AutoAssigned = true,
                    Status = proposedPath.Count > 1 ? "auto-created-path-and-assigned" : "auto-created-and-assigned",
                    Confidence = confidence,
                    Reason = BuildFinalReason(analysis, "smart_folder.auto_created_path")
                };
            }
        }

        if (decision == "uncategorized" || confidence < ReviewConfidence)
        {
            await SaveNeedsReviewSuggestionAsync(document, analysis, existingFolders, proposedPath, cancellationToken);
            return new DocumentFolderDecisionResultDto
            {
                FolderId = null,
                CreatedNewFolder = false,
                AutoAssigned = false,
                Status = "uncategorized",
                Confidence = confidence == 0m ? null : confidence,
                Reason = BuildFinalReason(analysis, "smart_folder.no_confident_match")
            };
        }

        // Existing-only mode, or medium confidence: do not force a wrong folder. Ask for review and let the UI show folder picker.
        await SaveNeedsReviewSuggestionAsync(document, analysis, existingFolders, proposedPath, cancellationToken);
        return new DocumentFolderDecisionResultDto
        {
            FolderId = null,
            CreatedNewFolder = false,
            AutoAssigned = false,
            Status = "needs-review",
            Confidence = confidence,
            Reason = BuildFinalReason(analysis, "smart_folder.needs_review")
        };
    }

    private async Task<DocumentFolderDecisionResultDto?> TryResolveFolderFromUploadedStructureAsync(
        Document document,
        IReadOnlyCollection<DocumentFolder> existingFolders,
        CancellationToken cancellationToken)
    {
        var segments = ExtractUsefulPathSegments(document.OriginalFileName);
        if (segments.Count == 0)
        {
            return null;
        }

        var path = SanitizeProposedPath(segments.Select(segment => new DocumentFolderProposalDto
        {
            Key = NormalizeKey(segment),
            Name = segment,
            NamePl = segment,
            NameEn = segment,
            NameUa = segment
        }));

        if (path.Count == 0)
        {
            return null;
        }

        var existingLeaf = ResolveExistingPath(existingFolders, path);
        if (existingLeaf is not null)
        {
            return new DocumentFolderDecisionResultDto
            {
                FolderId = existingLeaf.Id,
                CreatedNewFolder = false,
                AutoAssigned = true,
                Status = "auto-assigned-from-structure",
                Confidence = 0.96m,
                Reason = "smart_folder.upload_structure_matched_existing"
            };
        }

        if (document.OrganizationMode != DocumentOrganizationMode.AutoAssignOrCreate)
        {
            return null;
        }

        var leaf = await GetOrCreateFolderPathAsync(document.UserId, path, cancellationToken);
        return new DocumentFolderDecisionResultDto
        {
            FolderId = leaf.Id,
            CreatedNewFolder = leaf.IsSystemGenerated,
            AutoAssigned = true,
            Status = "auto-assigned-from-structure",
            Confidence = 0.96m,
            Reason = "smart_folder.upload_structure_created_path"
        };
    }

    private static List<string> ExtractUsefulPathSegments(string? originalFileName)
    {
        if (string.IsNullOrWhiteSpace(originalFileName))
        {
            return new List<string>();
        }

        var rawSegments = originalFileName
            .Replace('\\', '/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

        if (rawSegments.Count <= 1)
        {
            return new List<string>();
        }

        rawSegments.RemoveAt(rawSegments.Count - 1);

        return rawSegments
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Where(x => !x.EndsWith(":", StringComparison.OrdinalIgnoreCase))
            .Where(x => !string.Equals(x, "fakepath", StringComparison.OrdinalIgnoreCase))
            .Where(x => x is not "." and not "..")
            .Select(x => x.Length > 80 ? x[..80] : x)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MaxPathDepth)
            .ToList();
    }

    private static List<DocumentFolderProposalDto> ReconcileProposedPathWithExistingTree(
        DocumentFolderAnalysisResultDto analysis,
        IReadOnlyCollection<DocumentFolder> existingFolders,
        IReadOnlyList<DocumentFolderProposalDto> proposedPath)
    {
        var clean = SanitizeProposedPath(proposedPath);
        if (clean.Count == 0)
        {
            return clean;
        }

        var category = NormalizeKey(analysis.Category);

        if (category is "cv" or "resume")
        {
            clean = ReconcileCvPath(existingFolders, clean);
        }

        clean = AnchorFirstSegmentToExistingRoot(existingFolders, clean);
        return SanitizeProposedPath(clean);
    }

    private static List<DocumentFolderProposalDto> ReconcileCvPath(
        IReadOnlyCollection<DocumentFolder> existingFolders,
        IReadOnlyList<DocumentFolderProposalDto> path)
    {
        var clean = path.ToList();
        var cvIndex = clean.FindIndex(IsCvSegment);
        var cvRoot = FindTopLevelSegment(existingFolders, BuildSegment("cv", "CV", "CV", "CV", "Резюме"));
        var cvSegment = cvRoot is null ? BuildSegment("cv", "CV", "CV", "CV", "Резюме") : ToProposal(cvRoot);

        if (cvIndex > 0)
        {
            var reordered = new List<DocumentFolderProposalDto> { cvSegment };
            reordered.AddRange(clean.Take(cvIndex).Where(x => !IsCvSegment(x)));
            reordered.AddRange(clean.Skip(cvIndex + 1).Where(x => !IsCvSegment(x)));
            return SanitizeProposedPath(reordered);
        }

        if (cvIndex < 0)
        {
            var withCvRoot = new List<DocumentFolderProposalDto> { cvSegment };
            withCvRoot.AddRange(clean.Where(x => !IsCvSegment(x)));
            return SanitizeProposedPath(withCvRoot);
        }

        if (cvRoot is not null)
        {
            clean[0] = cvSegment;
        }

        return SanitizeProposedPath(clean);
    }

    private static List<DocumentFolderProposalDto> AnchorFirstSegmentToExistingRoot(
        IReadOnlyCollection<DocumentFolder> existingFolders,
        IReadOnlyList<DocumentFolderProposalDto> path)
    {
        if (path.Count == 0)
        {
            return path.ToList();
        }

        var existingRoot = FindTopLevelSegment(existingFolders, path[0]);
        if (existingRoot is null)
        {
            return path.ToList();
        }

        var result = path.ToList();
        result[0] = ToProposal(existingRoot);
        return result;
    }

    private static DocumentFolder? FindTopLevelSegment(
        IReadOnlyCollection<DocumentFolder> existingFolders,
        DocumentFolderProposalDto segment)
    {
        var key = NormalizeKey(segment.Key);
        return existingFolders
            .Where(x => x.ParentFolderId is null)
            .Select(x => new { Folder = x, Score = ScoreFolderSegment(x, segment, key) })
            .Where(x => x.Score >= 0.86m)
            .OrderByDescending(x => x.Score)
            .Select(x => x.Folder)
            .FirstOrDefault();
    }

    private static bool IsCvSegment(DocumentFolderProposalDto segment)
    {
        var key = NormalizeKey(segment.Key);
        var name = NormalizeKey(segment.Name);
        var namePl = NormalizeKey(segment.NamePl);
        var nameEn = NormalizeKey(segment.NameEn);
        return key is "cv" or "resume" || name is "cv" or "resume" || namePl is "cv" or "resume" || nameEn is "cv" or "resume";
    }

    private static DocumentFolderProposalDto BuildSegment(string key, string name, string namePl, string nameEn, string nameUa)
    {
        return new DocumentFolderProposalDto
        {
            Key = NormalizeKey(key),
            Name = name,
            NamePl = namePl,
            NameEn = nameEn,
            NameUa = nameUa,
            ParentFolderId = null
        };
    }

    private static DocumentFolderProposalDto ToProposal(DocumentFolder folder)
    {
        return new DocumentFolderProposalDto
        {
            Key = NormalizeKey(folder.Key),
            Name = NormalizeDisplayName(folder.Name, folder.Key),
            NamePl = NormalizeDisplayName(folder.NamePl, folder.Name),
            NameEn = NormalizeDisplayName(folder.NameEn, folder.Name),
            NameUa = NormalizeDisplayName(folder.NameUa, folder.Name),
            ParentFolderId = null
        };
    }

    private async Task ApplyLearningBoostsForReviewOnlyAsync(
        Document document,
        DocumentFolderAnalysisResultDto analysis,
        IReadOnlyCollection<DocumentFolder> existingFolders,
        CancellationToken cancellationToken)
    {
        if (existingFolders.Count == 0)
        {
            return;
        }

        var similarities = await _documentIntelligenceService.CalculateFolderSimilaritiesAsync(
            document,
            existingFolders,
            cancellationToken);

        foreach (var (folderId, similarity) in similarities)
        {
            if (similarity < 0.32m)
            {
                continue;
            }

            var folder = existingFolders.FirstOrDefault(x => x.Id == folderId);
            if (folder is null || analysis.ExistingFolderCandidates.Any(x => x.FolderId == folderId))
            {
                continue;
            }

            analysis.ExistingFolderCandidates.Add(new DocumentFolderCandidateDto
            {
                FolderId = folder.Id,
                FolderKey = folder.Key,
                FolderName = folder.Name,
                Score = Math.Clamp(similarity, 0m, 0.70m),
                SemanticScore = similarity,
                FinalScore = Math.Clamp(similarity, 0m, 0.70m),
                Reason = "smart_folder.semantic_profile_match"
            });
        }

        var source = $"{document.OriginalFileName} {document.ExtractedText}".ToLowerInvariant();
        var rules = await _dbContext.UserFolderRules
            .Where(x => x.UserId == document.UserId && x.Weight > 0m)
            .ToListAsync(cancellationToken);

        foreach (var rule in rules)
        {
            var tokens = rule.Pattern
                .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(x => x.Length >= 4)
                .Take(10)
                .ToList();

            if (tokens.Count == 0)
            {
                continue;
            }

            var matches = tokens.Count(x => source.Contains(x, StringComparison.OrdinalIgnoreCase));
            if (matches == 0)
            {
                continue;
            }

            var folder = existingFolders.FirstOrDefault(x => x.Id == rule.FolderId);
            if (folder is null)
            {
                continue;
            }

            var score = Math.Clamp(0.45m + (matches / (decimal)tokens.Count) * Math.Min(rule.Weight, 2m) * 0.20m, 0m, 0.78m);
            var candidate = analysis.ExistingFolderCandidates.FirstOrDefault(x => x.FolderId == folder.Id);
            if (candidate is null)
            {
                analysis.ExistingFolderCandidates.Add(new DocumentFolderCandidateDto
                {
                    FolderId = folder.Id,
                    FolderKey = folder.Key,
                    FolderName = folder.Name,
                    Score = score,
                    RuleScore = score,
                    UserHistoryScore = score,
                    FinalScore = score,
                    Reason = "smart_folder.user_history_match"
                });
            }
            else
            {
                candidate.UserHistoryScore = Math.Max(candidate.UserHistoryScore, score);
                candidate.FinalScore = Math.Max(candidate.FinalScore, score);
                candidate.Score = Math.Max(candidate.Score, score);
                candidate.Reason = AppendReason(candidate.Reason, "smart_folder.user_history_match");
            }

            rule.LastMatchedAtUtc = DateTime.UtcNow;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task SaveNeedsReviewSuggestionAsync(
        Document document,
        DocumentFolderAnalysisResultDto analysis,
        IReadOnlyCollection<DocumentFolder> existingFolders,
        IReadOnlyList<DocumentFolderProposalDto> proposedPath,
        CancellationToken cancellationToken)
    {
        var oldPending = await _dbContext.DocumentFolderSuggestions
            .Where(x => x.DocumentId == document.Id && x.Status == "pending")
            .ToListAsync(cancellationToken);

        foreach (var old in oldPending)
        {
            old.Status = "superseded";
        }

        DocumentFolderSuggestion? suggestion = null;

        if (analysis.SuggestedExistingFolderId is Guid existingId && existingFolders.FirstOrDefault(x => x.Id == existingId) is { } folder)
        {
            suggestion = BuildExistingSuggestion(document, folder, analysis, rank: 1);
        }
        else if (proposedPath.Count > 0)
        {
            var parent = proposedPath.Count > 1
                ? ResolveExistingPath(existingFolders, proposedPath.Take(proposedPath.Count - 1).ToList())
                : null;
            var leaf = proposedPath[^1];

            suggestion = new DocumentFolderSuggestion
            {
                Id = Guid.NewGuid(),
                DocumentId = document.Id,
                UserId = document.UserId,
                ExistingFolderId = null,
                ProposedKey = leaf.Key,
                ProposedName = leaf.Name,
                ProposedNamePl = leaf.NamePl,
                ProposedNameEn = leaf.NameEn,
                ProposedNameUa = leaf.NameUa,
                ProposedParentFolderId = parent?.Id ?? leaf.ParentFolderId,
                Score = Math.Clamp(analysis.Confidence, 0m, 1m),
                RuleScore = Math.Clamp(analysis.Confidence, 0m, 1m),
                SemanticScore = 0m,
                UserHistoryScore = 0m,
                FinalScore = Math.Clamp(analysis.Confidence, 0m, 1m),
                Rank = 1,
                Reason = BuildFinalReason(analysis, "smart_folder.proposed_path_needs_review"),
                Status = "pending",
                CreatedAtUtc = DateTime.UtcNow
            };
        }
        else if (analysis.ExistingFolderCandidates.OrderByDescending(x => x.Score).FirstOrDefault() is { } best &&
                 existingFolders.FirstOrDefault(x => x.Id == best.FolderId) is { } bestFolder)
        {
            suggestion = BuildExistingSuggestion(document, bestFolder, analysis, rank: 1);
        }

        if (suggestion is not null)
        {
            _dbContext.DocumentFolderSuggestions.Add(suggestion);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private static DocumentFolderSuggestion BuildExistingSuggestion(
        Document document,
        DocumentFolder folder,
        DocumentFolderAnalysisResultDto analysis,
        int rank)
    {
        return new DocumentFolderSuggestion
        {
            Id = Guid.NewGuid(),
            DocumentId = document.Id,
            UserId = document.UserId,
            ExistingFolderId = folder.Id,
            ProposedKey = folder.Key,
            ProposedName = folder.Name,
            ProposedNamePl = folder.NamePl,
            ProposedNameEn = folder.NameEn,
            ProposedNameUa = folder.NameUa,
            ProposedParentFolderId = folder.ParentFolderId,
            Score = Math.Clamp(analysis.Confidence, 0m, 1m),
            RuleScore = Math.Clamp(analysis.Confidence, 0m, 1m),
            SemanticScore = 0m,
            UserHistoryScore = 0m,
            FinalScore = Math.Clamp(analysis.Confidence, 0m, 1m),
            Rank = rank,
            Reason = BuildFinalReason(analysis, "smart_folder.proposed_existing_needs_review"),
            Status = "pending",
            CreatedAtUtc = DateTime.UtcNow
        };
    }

    private async Task ClearPendingSuggestionsAsync(Guid documentId, CancellationToken cancellationToken)
    {
        var oldPending = await _dbContext.DocumentFolderSuggestions
            .Where(x => x.DocumentId == documentId && x.Status == "pending")
            .ToListAsync(cancellationToken);

        foreach (var old in oldPending)
        {
            old.Status = "superseded";
        }

        if (oldPending.Count > 0)
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    private async Task<bool> CanAutoCreateFolderPathAsync(
        Guid userId,
        IReadOnlyList<DocumentFolderProposalDto> path,
        IReadOnlyCollection<DocumentFolder> existingFolders,
        CancellationToken cancellationToken)
    {
        var cleanPath = SanitizeProposedPath(path);
        if (cleanPath.Count == 0)
        {
            return false;
        }

        var today = DateTime.UtcNow.Date;
        var createdToday = await _dbContext.DocumentFolders.CountAsync(
            x => x.UserId == userId && x.IsSystemGenerated && x.CreatedAtUtc >= today,
            cancellationToken);

        if (createdToday >= MaxSystemFoldersCreatedPerDay)
        {
            return false;
        }

        return true;
    }

    private async Task<DocumentFolder> GetOrCreateFolderPathAsync(
        Guid userId,
        IReadOnlyList<DocumentFolderProposalDto> path,
        CancellationToken cancellationToken)
    {
        var cleanPath = SanitizeProposedPath(path);
        if (cleanPath.Count == 0)
        {
            throw new InvalidOperationException("Folder path is empty.");
        }

        Guid? parentId = null;
        DocumentFolder? current = null;

        foreach (var segment in cleanPath)
        {
            current = await FindSimilarFolderUnderParentAsync(userId, parentId, segment, cancellationToken);

            if (current is null)
            {
                current = new DocumentFolder
                {
                    Id = Guid.NewGuid(),
                    UserId = userId,
                    ParentFolderId = parentId,
                    Key = NormalizeKey(segment.Key),
                    Name = NormalizeDisplayName(segment.Name, segment.NamePl),
                    NamePl = NormalizeDisplayName(segment.NamePl, segment.Name),
                    NameEn = NormalizeDisplayName(segment.NameEn, segment.Name),
                    NameUa = NormalizeDisplayName(segment.NameUa, segment.Name),
                    IsSystemGenerated = true,
                    CreatedAtUtc = DateTime.UtcNow
                };

                _dbContext.DocumentFolders.Add(current);
                await _dbContext.SaveChangesAsync(cancellationToken);
            }

            parentId = current.Id;
        }

        return current ?? throw new InvalidOperationException("Folder path is empty.");
    }

    private async Task<DocumentFolder?> FindSimilarFolderUnderParentAsync(
        Guid userId,
        Guid? parentId,
        DocumentFolderProposalDto segment,
        CancellationToken cancellationToken)
    {
        var key = NormalizeKey(segment.Key);
        var siblings = await _dbContext.DocumentFolders
            .Where(x => x.UserId == userId && x.ParentFolderId == parentId)
            .ToListAsync(cancellationToken);

        return siblings
            .Select(x => new { Folder = x, Score = ScoreFolderSegment(x, segment, key) })
            .Where(x => x.Score >= 0.86m)
            .OrderByDescending(x => x.Score)
            .Select(x => x.Folder)
            .FirstOrDefault();
    }

    private static DocumentFolder? ResolveExistingPath(
        IReadOnlyCollection<DocumentFolder> existingFolders,
        IReadOnlyList<DocumentFolderProposalDto> proposedPath)
    {
        var cleanPath = SanitizeProposedPath(proposedPath);
        if (cleanPath.Count == 0 || existingFolders.Count == 0)
        {
            return null;
        }

        Guid? parentId = null;
        DocumentFolder? current = null;

        foreach (var segment in cleanPath)
        {
            var key = NormalizeKey(segment.Key);
            current = existingFolders
                .Where(x => x.ParentFolderId == parentId)
                .Select(x => new { Folder = x, Score = ScoreFolderSegment(x, segment, key) })
                .Where(x => x.Score >= 0.86m)
                .OrderByDescending(x => x.Score)
                .Select(x => x.Folder)
                .FirstOrDefault();

            if (current is null)
            {
                return null;
            }

            parentId = current.Id;
        }

        return current;
    }

    private static decimal ScoreFolderSegment(DocumentFolder folder, DocumentFolderProposalDto segment, string normalizedSegmentKey)
    {
        var scores = new[]
        {
            string.Equals(NormalizeKey(folder.Key), normalizedSegmentKey, StringComparison.OrdinalIgnoreCase) ? 1m : 0m,
            CalculateNameSimilarity(folder.Name, segment.Name),
            CalculateNameSimilarity(folder.NamePl, segment.NamePl),
            CalculateNameSimilarity(folder.NameEn, segment.NameEn),
            CalculateNameSimilarity(folder.NameUa, segment.NameUa)
        };

        return scores.Max();
    }

    private static List<DocumentFolderProposalDto> SanitizeProposedPath(IEnumerable<DocumentFolderProposalDto>? path)
    {
        if (path is null)
        {
            return new List<DocumentFolderProposalDto>();
        }

        return path
            .Where(x => x is not null)
            .Select(x =>
            {
                var name = NormalizeDisplayName(x.NamePl, NormalizeDisplayName(x.Name, x.Key));
                return new DocumentFolderProposalDto
                {
                    Key = NormalizeKey(string.IsNullOrWhiteSpace(x.Key) ? name : x.Key),
                    Name = NormalizeDisplayName(x.Name, name),
                    NamePl = NormalizeDisplayName(x.NamePl, name),
                    NameEn = NormalizeDisplayName(x.NameEn, name),
                    NameUa = NormalizeDisplayName(x.NameUa, name),
                    ParentFolderId = null
                };
            })
            .Where(x => !IsBadFolderName(x.Name) && !IsBadFolderName(x.NamePl))
            .GroupBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.First())
            .Take(MaxPathDepth)
            .ToList();
    }

    private static void NormalizeCandidates(DocumentFolderAnalysisResultDto analysis)
    {
        analysis.ExistingFolderCandidates = analysis.ExistingFolderCandidates
            .GroupBy(x => x.FolderId)
            .Select(group => group.OrderByDescending(x => x.Score).First())
            .OrderByDescending(x => x.Score)
            .Take(5)
            .ToList();

        foreach (var candidate in analysis.ExistingFolderCandidates)
        {
            candidate.Score = Math.Clamp(candidate.FinalScore > 0m ? candidate.FinalScore : candidate.Score, 0m, 1m);
            candidate.FinalScore = candidate.Score;
        }
    }

    private static string NormalizeDecision(string? decision)
    {
        var normalized = NormalizeKey(decision).Replace('-', '_');
        return normalized is "use_existing" or "create_path" or "needs_review" or "uncategorized"
            ? normalized
            : "needs_review";
    }

    private static string BuildFinalReason(DocumentFolderAnalysisResultDto analysis, string fallbackCode)
    {
        // Store a stable final-action code. The UI owns PL/EN/UA translations.
        return NormalizeReasonCode(fallbackCode, fallbackCode);
    }

    private static string NormalizeReasonCode(string? code, string fallbackCode)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return fallbackCode;
        }

        var normalized = code.Trim().Replace('-', '_');
        return normalized.StartsWith("smart_folder.", StringComparison.OrdinalIgnoreCase)
            ? normalized
            : $"smart_folder.{NormalizeKey(normalized).Replace('-', '_')}";
    }

    private static string AppendReason(string? current, string addition)
    {
        return string.IsNullOrWhiteSpace(current) ? addition : $"{current} {addition}";
    }

    private static string BuildFolderPath(IReadOnlyCollection<DocumentFolder> folders, DocumentFolder leaf)
    {
        var names = new Stack<string>();
        var current = leaf;
        var guard = 0;

        while (guard++ < 8)
        {
            names.Push(current.Name);
            if (current.ParentFolderId is not Guid parentId)
            {
                break;
            }

            var parent = folders.FirstOrDefault(x => x.Id == parentId);
            if (parent is null)
            {
                break;
            }

            current = parent;
        }

        return string.Join(" / ", names);
    }

    private static bool IsBadFolderName(string? name)
    {
        var normalized = NormalizeKey(name);
        return string.IsNullOrWhiteSpace(normalized) || normalized is "document" or "documents" or "dokument" or "dokumenty" or "file" or "files" or "plik" or "pliki" or "other" or "inne" or "misc" or "rozne";
    }

    private static string NormalizeKey(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "folder";
        }

        var normalized = RemoveDiacritics(value.Trim().ToLowerInvariant());
        normalized = Regex.Replace(normalized, @"[^a-z0-9]+", "-");
        normalized = Regex.Replace(normalized, @"-{2,}", "-").Trim('-');
        return string.IsNullOrWhiteSpace(normalized) ? "folder" : normalized;
    }

    private static string NormalizeDisplayName(string? value, string? fallback)
    {
        var result = string.IsNullOrWhiteSpace(value) ? fallback : value;
        result = string.IsNullOrWhiteSpace(result) ? "Folder" : result.Trim();
        return result.Length > 80 ? result[..80] : result;
    }

    private static string RemoveDiacritics(string value)
    {
        var normalized = value.Normalize(System.Text.NormalizationForm.FormD);
        var chars = normalized
            .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            .ToArray();

        return new string(chars).Normalize(System.Text.NormalizationForm.FormC);
    }

    private static decimal CalculateNameSimilarity(string? first, string? second)
    {
        var a = new string((first ?? string.Empty).Trim().ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
        var b = new string((second ?? string.Empty).Trim().ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());

        if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b))
        {
            return 0m;
        }

        if (a == b)
        {
            return 1m;
        }

        if (a.Contains(b) || b.Contains(a))
        {
            return 0.90m;
        }

        var costs = new int[b.Length + 1];
        for (var j = 0; j < costs.Length; j++) costs[j] = j;
        for (var i = 1; i <= a.Length; i++)
        {
            costs[0] = i;
            var previous = i - 1;
            for (var j = 1; j <= b.Length; j++)
            {
                var current = costs[j];
                costs[j] = a[i - 1] == b[j - 1]
                    ? previous
                    : Math.Min(Math.Min(costs[j - 1], costs[j]), previous) + 1;
                previous = current;
            }
        }

        return Math.Round(1m - (decimal)costs[b.Length] / Math.Max(a.Length, b.Length), 4);
    }
}
