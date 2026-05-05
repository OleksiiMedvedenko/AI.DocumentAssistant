using AI.DocumentAssistant.Application.Abstractions.Documents;
using AI.DocumentAssistant.Application.Documents.Dtos;
using AI.DocumentAssistant.Domain.Entities;
using AI.DocumentAssistant.Domain.Enums;
using AI.DocumentAssistant.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AI.DocumentAssistant.Application.Documents.Services
{
    public sealed class DocumentFolderDecisionEngine : IDocumentFolderDecisionEngine
    {
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
                return new DocumentFolderDecisionResultDto
                {
                    FolderId = document.FolderId,
                    CreatedNewFolder = false,
                    AutoAssigned = false,
                    Status = "manual",
                    Confidence = 1m,
                    Reason = "Folder selected manually during upload."
                };
            }

            if (document.OrganizationMode == DocumentOrganizationMode.Disabled)
            {
                return new DocumentFolderDecisionResultDto
                {
                    Status = "disabled",
                    Confidence = null,
                    Reason = "Smart organization is disabled for this document."
                };
            }

            await _documentIntelligenceService.EnsureSnapshotAsync(document, cancellationToken);
            await ApplySemanticFolderBoostAsync(document, analysis, existingFolders, cancellationToken);
            await ApplyUserHistoryBoostAsync(document, analysis, cancellationToken);

            await SaveSuggestionsAsync(document, analysis, existingFolders, cancellationToken);

            var bestExisting = analysis.ExistingFolderCandidates
                .OrderByDescending(x => x.Score)
                .FirstOrDefault();

            var assignExistingThreshold = analysis.Category switch
            {
                "invoices" => 0.55m,
                "cv" => 0.55m,
                "contracts" => 0.58m,
                "documentation" => 0.60m,
                _ => 0.62m
            };

            var createThreshold = analysis.Category switch
            {
                "invoices" => 0.90m,
                "cv" => 0.90m,
                "contracts" => 0.92m,
                "documentation" => 0.92m,
                _ => 0.94m
            };


            if (bestExisting is not null && bestExisting.Score >= assignExistingThreshold)
            {
                return new DocumentFolderDecisionResultDto
                {
                    FolderId = bestExisting.FolderId,
                    CreatedNewFolder = false,
                    AutoAssigned = true,
                    Status = "auto-assigned",
                    Confidence = bestExisting.Score,
                    Reason = bestExisting.Reason
                };
            }

            if (document.OrganizationMode == DocumentOrganizationMode.AutoAssignExistingOnly)
            {
                return new DocumentFolderDecisionResultDto
                {
                    FolderId = null,
                    CreatedNewFolder = false,
                    AutoAssigned = false,
                    Status = "suggested",
                    Confidence = analysis.Confidence,
                    Reason = analysis.Reason
                };
            }

            if (document.OrganizationMode == DocumentOrganizationMode.AutoAssignOrCreate &&
                analysis.ProposedFolder is not null &&
                analysis.Confidence >= createThreshold &&
                await CanAutoCreateFolderAsync(document.UserId, analysis.ProposedFolder, existingFolders, cancellationToken))
            {
                var createdOrExisting = await GetOrCreateFolderAsync(
                    document.UserId,
                    analysis.ProposedFolder,
                    cancellationToken);

                return new DocumentFolderDecisionResultDto
                {
                    FolderId = createdOrExisting.Id,
                    CreatedNewFolder = createdOrExisting.IsSystemGenerated,
                    AutoAssigned = true,
                    Status = "auto-created-and-assigned",
                    Confidence = analysis.Confidence,
                    Reason = analysis.Reason
                };
            }

            return new DocumentFolderDecisionResultDto
            {
                FolderId = null,
                CreatedNewFolder = false,
                AutoAssigned = false,
                Status = "suggested",
                Confidence = analysis.Confidence,
                Reason = analysis.Reason
            };
        }




        private async Task ApplySemanticFolderBoostAsync(
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
                if (similarity < 0.20m)
                {
                    continue;
                }

                var boost = Math.Min(0.22m, similarity * 0.22m);
                var candidate = analysis.ExistingFolderCandidates.FirstOrDefault(x => x.FolderId == folderId);
                var folder = existingFolders.FirstOrDefault(x => x.Id == folderId);
                if (folder is null)
                {
                    continue;
                }

                if (candidate is null)
                {
                    analysis.ExistingFolderCandidates.Add(new DocumentFolderCandidateDto
                    {
                        FolderId = folder.Id,
                        FolderKey = folder.Key,
                        FolderName = folder.Name,
                        Score = Math.Clamp(0.40m + boost, 0m, 1m),
                        RuleScore = 0.40m,
                        SemanticScore = similarity,
                        FinalScore = Math.Clamp(0.40m + boost, 0m, 1m),
                        Reason = $"Semantic similarity to folder profile: {similarity:P0}."
                    });
                }
                else
                {
                    candidate.SemanticScore = Math.Max(candidate.SemanticScore, similarity);
                    candidate.FinalScore = Math.Clamp(candidate.Score + boost, 0m, 1m);
                    candidate.Score = candidate.FinalScore;
                    candidate.Reason = $"{candidate.Reason} Semantic folder profile match: {similarity:P0}.";
                }
            }
        }

        private async Task SaveSuggestionsAsync(
            Document document,
            DocumentFolderAnalysisResultDto analysis,
            IReadOnlyCollection<DocumentFolder> existingFolders,
            CancellationToken cancellationToken)
        {
            var oldPending = await _dbContext.DocumentFolderSuggestions
                .Where(x => x.DocumentId == document.Id && x.Status == "pending")
                .ToListAsync(cancellationToken);

            foreach (var old in oldPending)
            {
                old.Status = "superseded";
            }

            var suggestions = new List<DocumentFolderSuggestion>();
            var rank = 1;

            foreach (var candidate in analysis.ExistingFolderCandidates
                         .OrderByDescending(x => x.Score)
                         .Take(3))
            {
                var folder = existingFolders.FirstOrDefault(x => x.Id == candidate.FolderId);
                if (folder is null)
                {
                    continue;
                }

                suggestions.Add(new DocumentFolderSuggestion
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
                    Score = Math.Clamp(candidate.Score, 0m, 1m),
                    RuleScore = Math.Clamp(candidate.RuleScore == 0m ? candidate.Score : candidate.RuleScore, 0m, 1m),
                    SemanticScore = Math.Clamp(candidate.SemanticScore, 0m, 1m),
                    UserHistoryScore = Math.Clamp(candidate.UserHistoryScore, 0m, 1m),
                    FinalScore = Math.Clamp(candidate.Score, 0m, 1m),
                    Rank = rank++,
                    Reason = candidate.Reason,
                    Status = "pending",
                    CreatedAtUtc = DateTime.UtcNow
                });
            }

            if (analysis.ProposedFolder is not null && suggestions.All(x => x.ProposedKey != analysis.ProposedFolder.Key))
            {
                suggestions.Add(new DocumentFolderSuggestion
                {
                    Id = Guid.NewGuid(),
                    DocumentId = document.Id,
                    UserId = document.UserId,
                    ExistingFolderId = null,
                    ProposedKey = analysis.ProposedFolder.Key,
                    ProposedName = analysis.ProposedFolder.Name,
                    ProposedNamePl = analysis.ProposedFolder.NamePl,
                    ProposedNameEn = analysis.ProposedFolder.NameEn,
                    ProposedNameUa = analysis.ProposedFolder.NameUa,
                    ProposedParentFolderId = analysis.ProposedFolder.ParentFolderId,
                    Score = Math.Clamp(analysis.Confidence, 0m, 1m),
                    RuleScore = Math.Clamp(analysis.Confidence, 0m, 1m),
                    SemanticScore = 0m,
                    UserHistoryScore = 0m,
                    FinalScore = Math.Clamp(analysis.Confidence, 0m, 1m),
                    Rank = rank,
                    Reason = analysis.Reason,
                    Status = "pending",
                    CreatedAtUtc = DateTime.UtcNow
                });
            }

            if (suggestions.Count > 0)
            {
                _dbContext.DocumentFolderSuggestions.AddRange(suggestions.Take(3));
                await _dbContext.SaveChangesAsync(cancellationToken);
            }
        }

        private async Task ApplyUserHistoryBoostAsync(
            Document document,
            DocumentFolderAnalysisResultDto analysis,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(document.ExtractedText) && string.IsNullOrWhiteSpace(document.OriginalFileName))
            {
                return;
            }

            var source = $"{document.OriginalFileName} {document.ExtractedText}".ToLowerInvariant();
            var rules = await _dbContext.UserFolderRules
                .Where(x => x.UserId == document.UserId)
                .ToListAsync(cancellationToken);

            foreach (var rule in rules)
            {
                var patternTokens = rule.Pattern
                    .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Where(x => x.Length >= 4)
                    .ToList();

                if (patternTokens.Count == 0)
                {
                    continue;
                }

                var matches = patternTokens.Count(x => source.Contains(x, StringComparison.OrdinalIgnoreCase));
                if (matches == 0)
                {
                    continue;
                }

                var boost = Math.Min(0.18m, (matches / (decimal)patternTokens.Count) * 0.18m * Math.Min(rule.Weight, 2m));
                var candidate = analysis.ExistingFolderCandidates.FirstOrDefault(x => x.FolderId == rule.FolderId);

                if (candidate is null)
                {
                    var folder = await _dbContext.DocumentFolders.FirstOrDefaultAsync(
                        x => x.Id == rule.FolderId && x.UserId == document.UserId,
                        cancellationToken);

                    if (folder is null)
                    {
                        continue;
                    }

                    analysis.ExistingFolderCandidates.Add(new DocumentFolderCandidateDto
                    {
                        FolderId = folder.Id,
                        FolderKey = folder.Key,
                        FolderName = folder.Name,
                        Score = Math.Clamp(0.55m + boost, 0m, 1m),
                        RuleScore = 0.55m,
                        UserHistoryScore = boost,
                        FinalScore = Math.Clamp(0.55m + boost, 0m, 1m),
                        Reason = "Matched previous user folder choices for similar documents."
                    });
                }
                else
                {
                    candidate.UserHistoryScore = Math.Max(candidate.UserHistoryScore, boost);
                    candidate.FinalScore = Math.Clamp(candidate.Score + boost, 0m, 1m);
                    candidate.Score = candidate.FinalScore;
                    candidate.Reason = $"{candidate.Reason} User history increased confidence.";
                }

                rule.LastMatchedAtUtc = DateTime.UtcNow;
            }

            await _dbContext.SaveChangesAsync(cancellationToken);
        }


        private async Task<bool> CanAutoCreateFolderAsync(
            Guid userId,
            DocumentFolderProposalDto proposal,
            IReadOnlyCollection<DocumentFolder> existingFolders,
            CancellationToken cancellationToken)
        {
            var today = DateTime.UtcNow.Date;
            var autoCreatedToday = await _dbContext.DocumentFolders.CountAsync(
                x => x.UserId == userId && x.IsSystemGenerated && x.CreatedAtUtc >= today,
                cancellationToken);

            if (autoCreatedToday >= 5)
            {
                return false;
            }

            return existingFolders.All(x =>
                x.ParentFolderId != proposal.ParentFolderId ||
                CalculateNameSimilarity(x.NameEn, proposal.NameEn) < 0.82m);
        }

        private static decimal CalculateNameSimilarity(string first, string second)
        {
            var a = new string(first.Trim().ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
            var b = new string(second.Trim().ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());

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
                return 0.86m;
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

        private async Task<DocumentFolder> GetOrCreateFolderAsync(
            Guid userId,
            DocumentFolderProposalDto proposal,
            CancellationToken cancellationToken)
        {
            var existing = await _dbContext.DocumentFolders.FirstOrDefaultAsync(
                x => x.UserId == userId &&
                     x.ParentFolderId == proposal.ParentFolderId &&
                     x.Key == proposal.Key,
                cancellationToken);

            if (existing is not null)
            {
                existing.IsSystemGenerated = existing.IsSystemGenerated || true;
                return existing;
            }

            var folder = new DocumentFolder
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                ParentFolderId = proposal.ParentFolderId,
                Key = proposal.Key,
                Name = proposal.Name,
                NamePl = proposal.NamePl,
                NameEn = proposal.NameEn,
                NameUa = proposal.NameUa,
                IsSystemGenerated = true,
                CreatedAtUtc = DateTime.UtcNow
            };

            _dbContext.DocumentFolders.Add(folder);
            await _dbContext.SaveChangesAsync(cancellationToken);

            return folder;
        }
    }
}