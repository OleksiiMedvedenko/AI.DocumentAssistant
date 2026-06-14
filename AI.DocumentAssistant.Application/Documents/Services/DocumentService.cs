using AI.DocumentAssistant.Application.Abstractions.AI;
using AI.DocumentAssistant.Application.Abstractions.Common;
using AI.DocumentAssistant.Application.Abstractions.Authorization;
using AI.DocumentAssistant.Application.Abstractions.Documents;
using AI.DocumentAssistant.Application.Abstractions.Usage;
using AI.DocumentAssistant.Application.Common.Exceptions;
using AI.DocumentAssistant.Application.Documents.Dtos;
using AI.DocumentAssistant.Domain.Entities;
using AI.DocumentAssistant.Application.Authorization;
using AI.DocumentAssistant.Domain.Enums;
using AI.DocumentAssistant.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using System.Text;
using System.Text.RegularExpressions;

namespace AI.DocumentAssistant.Application.Documents.Services;

public sealed class DocumentService : IDocumentService
{
    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf", ".docx", ".txt", ".md", ".markdown", ".csv", ".json", ".xml", ".log"
    };

    private readonly AppDbContext _dbContext;
    private readonly ICurrentUserService _currentUserService;
    private readonly IFileStorageService _fileStorageService;
    private readonly IDocumentProcessingQueue _documentProcessingQueue;
    private readonly IOpenAiService _openAiService;
    private readonly IUsageQuotaService _usageQuotaService;
    private readonly IUsageTrackingService _usageTrackingService;
    private readonly IDocumentPreviewConverter _documentPreviewConverter;
    private readonly IDocumentFolderClassifier _documentFolderClassifier;
    private readonly IDocumentFolderDecisionEngine _documentFolderDecisionEngine;
    private readonly IDocumentIntelligenceService _documentIntelligenceService;
    private readonly IPermissionService _permissionService;

    public DocumentService(
        AppDbContext dbContext,
        ICurrentUserService currentUserService,
        IFileStorageService fileStorageService,
        IDocumentProcessingQueue documentProcessingQueue,
        IOpenAiService openAiService,
        IUsageQuotaService usageQuotaService,
        IUsageTrackingService usageTrackingService,
        IDocumentPreviewConverter documentPreviewConverter,
        IDocumentFolderClassifier documentFolderClassifier,
        IDocumentFolderDecisionEngine documentFolderDecisionEngine,
        IDocumentIntelligenceService documentIntelligenceService,
        IPermissionService permissionService)
    {
        _dbContext = dbContext;
        _currentUserService = currentUserService;
        _fileStorageService = fileStorageService;
        _documentProcessingQueue = documentProcessingQueue;
        _openAiService = openAiService;
        _usageQuotaService = usageQuotaService;
        _usageTrackingService = usageTrackingService;
        _documentPreviewConverter = documentPreviewConverter;
        _documentFolderClassifier = documentFolderClassifier;
        _documentFolderDecisionEngine = documentFolderDecisionEngine;
        _documentIntelligenceService = documentIntelligenceService;
        _permissionService = permissionService;
    }

    public async Task<DocumentDto> UploadAsync(UploadDocumentRequestDto request, CancellationToken cancellationToken)
    {
        var file = request.File;
        if (file is null)
        {
            throw new BadRequestException("File is required.");
        }

        if (file.Length == 0)
        {
            throw new BadRequestException("File is empty.");
        }

        var extension = Path.GetExtension(file.FileName);
        if (string.IsNullOrWhiteSpace(extension) || !AllowedExtensions.Contains(extension))
        {
            throw new BadRequestException("Unsupported file type. Allowed: pdf, docx, txt, md, csv, json, xml, log.");
        }

        var userId = _currentUserService.GetUserId();
        var visibility = ResolveVisibility(request.Visibility, request.OrganizationId);

        if (request.OrganizationId.HasValue)
        {
            await _permissionService.EnsurePermissionAsync(
                userId,
                PermissionKeys.DocumentsUpload,
                organizationId: request.OrganizationId.Value,
                cancellationToken: cancellationToken);
        }

        if (request.FolderId is not null)
        {
            var folderExists = await _dbContext.DocumentFolders.AnyAsync(
                x => x.Id == request.FolderId &&
                     (x.UserId == userId || (request.OrganizationId.HasValue && x.OrganizationId == request.OrganizationId.Value)),
                cancellationToken);

            if (!folderExists)
            {
                throw new BadRequestException("Selected folder does not exist.");
            }
        }

        await _usageQuotaService.EnsureWithinQuotaAsync(
            userId,
            UsageType.UploadDocument,
            1,
            cancellationToken);

        var fileName = $"{Guid.NewGuid():N}{extension.ToLowerInvariant()}";
        string? storagePath = null;

        try
        {
            await using var stream = file.OpenReadStream();
            storagePath = await _fileStorageService.SaveAsync(stream, fileName, cancellationToken);

            var organizationMode = ResolveOrganizationMode(request);
            var processingProfile = DocumentProcessingProfileResolver.Resolve(file.FileName, file.ContentType);

            var document = new Document
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                OrganizationId = request.OrganizationId,
                Visibility = visibility,
                FolderId = request.FolderId,
                FileName = fileName,
                OriginalFileName = file.FileName,
                ContentType = string.IsNullOrWhiteSpace(file.ContentType)
                    ? "application/octet-stream"
                    : file.ContentType,
                SizeInBytes = file.Length,
                StoragePath = storagePath,
                Status = DocumentStatus.Uploaded,
                UploadedAtUtc = DateTime.UtcNow,

                OrganizationMode = organizationMode,
                SmartOrganizeRequested = request.SmartOrganize,
                AllowSystemFolderCreation = request.AllowSystemFolderCreation,

                FolderClassificationStatus = ResolveInitialClassificationStatus(request),
                FolderClassificationConfidence = request.FolderId is not null ? 1m : null,
                FolderClassificationReason = ResolveInitialClassificationReason(request),
                WasFolderAutoAssigned = false,
                ProcessingProfile = processingProfile,
                IsNew = true,
                AnalyzedAtUtc = null,
                QuickSummary = null,
            };

            _dbContext.Documents.Add(document);
            await _dbContext.SaveChangesAsync(cancellationToken);

            await _usageTrackingService.TrackAsync(
                userId,
                UsageType.UploadDocument,
                1,
                cancellationToken,
                referenceId: document.Id.ToString());

            await _documentProcessingQueue.EnqueueAsync(document.Id, cancellationToken);

            return await _dbContext.Documents
                .Where(x => x.Id == document.Id)
                .Select(x => new DocumentDto
                {
                    Id = x.Id,
                    OriginalFileName = x.OriginalFileName,
                    ContentType = x.ContentType,
                    SizeInBytes = x.SizeInBytes,
                    Status = x.Status,
                    UploadedAtUtc = x.UploadedAtUtc,
                    OrganizationId = x.OrganizationId,
                    Visibility = x.Visibility,
                    FolderId = x.FolderId,
                    FolderName = x.Folder != null ? x.Folder.Name : null,
                    FolderNamePl = x.Folder != null ? x.Folder.NamePl : null,
                    FolderNameEn = x.Folder != null ? x.Folder.NameEn : null,
                    FolderNameUa = x.Folder != null ? x.Folder.NameUa : null,
                    FolderClassificationStatus = x.FolderClassificationStatus,
                    FolderClassificationReason = x.FolderClassificationReason,
                    FolderClassificationReasonCode = x.FolderClassificationReason,
                    FolderClassificationConfidence = x.FolderClassificationConfidence,
                    WasFolderAutoAssigned = x.WasFolderAutoAssigned,
                    IsNew = x.IsNew,
                    ProcessingProfile = x.ProcessingProfile,
                })
                .FirstAsync(cancellationToken);
        }
        catch
        {
            if (!string.IsNullOrWhiteSpace(storagePath))
            {
                try
                {
                    await _fileStorageService.DeleteAsync(storagePath, cancellationToken);
                }
                catch
                {
                }
            }

            throw;
        }
    }

    public async Task<UploadDocumentsResultDto> UploadManyAsync(
    UploadDocumentsRequestDto request,
    CancellationToken cancellationToken)
    {
        if (request.Files is null || request.Files.Count == 0)
        {
            throw new BadRequestException("At least one file is required.");
        }

        var result = new UploadDocumentsResultDto();

        foreach (var file in request.Files.Where(static x => x is not null))
        {
            var singleResult = await UploadAsync(
                new UploadDocumentRequestDto
                {
                    File = file,
                    FolderId = request.FolderId,
                    OrganizationId = request.OrganizationId,
                    Visibility = request.Visibility,
                    SmartOrganize = request.SmartOrganize,
                    AllowSystemFolderCreation = request.AllowSystemFolderCreation
                },
                cancellationToken);

            result.Documents.Add(singleResult);
        }

        return result;
    }

    private static DocumentVisibility ResolveVisibility(string? visibility, Guid? organizationId)
    {
        if (string.IsNullOrWhiteSpace(visibility))
        {
            return organizationId.HasValue ? DocumentVisibility.Organization : DocumentVisibility.Private;
        }

        if (!Enum.TryParse<DocumentVisibility>(visibility, true, out var parsed))
        {
            throw new BadRequestException("Unsupported document visibility.");
        }

        if (!organizationId.HasValue && parsed != DocumentVisibility.Private)
        {
            throw new BadRequestException("Only private visibility can be used outside an organization.");
        }

        return parsed;
    }

    private static DocumentOrganizationMode ResolveOrganizationMode(UploadDocumentRequestDto request)
    {
        if (request.FolderId is not null)
        {
            return DocumentOrganizationMode.Manual;
        }

        if (!request.SmartOrganize)
        {
            return DocumentOrganizationMode.Disabled;
        }

        return request.AllowSystemFolderCreation
            ? DocumentOrganizationMode.AutoAssignOrCreate
            : DocumentOrganizationMode.AutoAssignExistingOnly;
    }

    private static string ResolveInitialClassificationStatus(UploadDocumentRequestDto request)
    {
        if (request.FolderId is not null)
        {
            return "manual";
        }

        return request.SmartOrganize ? "pending" : "disabled";
    }

    private static string ResolveInitialClassificationReason(UploadDocumentRequestDto request)
    {
        if (request.FolderId is not null)
        {
            return "smart_folder.manual_folder_selected";
        }

        return request.SmartOrganize
            ? "smart_folder.pending"
            : "smart_folder.disabled";
    }

    public async Task<List<DocumentDto>> GetAllAsync(Guid? folderId, Guid? organizationId, CancellationToken cancellationToken)
    {
        var userId = _currentUserService.GetUserId();

        var query = _dbContext.Documents.AsQueryable();

        if (organizationId.HasValue)
        {
            await _permissionService.EnsurePermissionAsync(
                userId,
                PermissionKeys.DocumentsView,
                organizationId: organizationId.Value,
                cancellationToken: cancellationToken);

            query = query.Where(x => x.OrganizationId == organizationId.Value &&
                (x.Visibility == DocumentVisibility.Organization || x.UserId == userId));
        }
        else
        {
            query = query.Where(x => x.UserId == userId && x.OrganizationId == null);
        }

        if (folderId.HasValue)
        {
            query = query.Where(x => x.FolderId == folderId.Value);
        }

        return await query
            .OrderByDescending(x => x.UploadedAtUtc)
            .Select(x => new DocumentDto
            {
                Id = x.Id,
                OriginalFileName = x.OriginalFileName,
                ContentType = x.ContentType,
                SizeInBytes = x.SizeInBytes,
                Status = x.Status,
                UploadedAtUtc = x.UploadedAtUtc,
                OrganizationId = x.OrganizationId,
                Visibility = x.Visibility,
                FolderId = x.FolderId,
                FolderName = x.Folder != null ? x.Folder.Name : null,
                FolderNamePl = x.Folder != null ? x.Folder.NamePl : null,
                FolderNameEn = x.Folder != null ? x.Folder.NameEn : null,
                FolderNameUa = x.Folder != null ? x.Folder.NameUa : null,
                FolderClassificationStatus = x.FolderClassificationStatus,
                    FolderClassificationReason = x.FolderClassificationReason,
                    FolderClassificationReasonCode = x.FolderClassificationReason,
                FolderClassificationConfidence = x.FolderClassificationConfidence,
                WasFolderAutoAssigned = x.WasFolderAutoAssigned
            })
            .ToListAsync(cancellationToken);
    }


    public async Task<List<DocumentDto>> GetInboxAsync(CancellationToken cancellationToken)
    {
        var userId = _currentUserService.GetUserId();

        return await _dbContext.Documents
            .Where(x => x.UserId == userId && x.OrganizationId == null && (x.IsNew || x.FolderId == null || x.FolderClassificationStatus == "suggested"))
            .OrderByDescending(x => x.UploadedAtUtc)
            .Select(x => new DocumentDto
            {
                Id = x.Id,
                OriginalFileName = x.OriginalFileName,
                ContentType = x.ContentType,
                SizeInBytes = x.SizeInBytes,
                Status = x.Status,
                UploadedAtUtc = x.UploadedAtUtc,
                FolderId = x.FolderId,
                FolderName = x.Folder != null ? x.Folder.Name : null,
                FolderNamePl = x.Folder != null ? x.Folder.NamePl : null,
                FolderNameEn = x.Folder != null ? x.Folder.NameEn : null,
                FolderNameUa = x.Folder != null ? x.Folder.NameUa : null,
                FolderClassificationStatus = x.FolderClassificationStatus,
                    FolderClassificationReason = x.FolderClassificationReason,
                    FolderClassificationReasonCode = x.FolderClassificationReason,
                FolderClassificationConfidence = x.FolderClassificationConfidence,
                WasFolderAutoAssigned = x.WasFolderAutoAssigned,
                IsNew = x.IsNew,
                ProcessingProfile = x.ProcessingProfile
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<DocumentDashboardDto> GetDashboardAsync(CancellationToken cancellationToken)
    {
        var userId = _currentUserService.GetUserId();

        var documents = await _dbContext.Documents
            .Include(x => x.Folder)
            .Where(x => x.UserId == userId && x.OrganizationId == null)
            .OrderByDescending(x => x.UploadedAtUtc)
            .ToListAsync(cancellationToken);

        var pendingSuggestionEntities = await _dbContext.DocumentFolderSuggestions
            .Where(x => x.UserId == userId && x.Status == "pending")
            .ToListAsync(cancellationToken);

        pendingSuggestionEntities = pendingSuggestionEntities
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Rank)
            .Take(10)
            .ToList();

        var existingFolderIds = pendingSuggestionEntities
            .Where(x => x.ExistingFolderId.HasValue)
            .Select(x => x.ExistingFolderId!.Value)
            .Distinct()
            .ToList();

        var existingFolders = existingFolderIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await _dbContext.DocumentFolders
                .Where(x => x.UserId == userId && existingFolderIds.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken);

        var pendingSuggestions = pendingSuggestionEntities
            .Select(x => new DocumentFolderSuggestionResponseDto
            {
                Id = x.Id,
                DocumentId = x.DocumentId,
                ExistingFolderId = x.ExistingFolderId,
                ExistingFolderName = x.ExistingFolderId.HasValue &&
                                     existingFolders.TryGetValue(x.ExistingFolderId.Value, out var folderName)
                    ? folderName
                    : null,
                ProposedKey = x.ProposedKey,
                ProposedName = x.ProposedName,
                ProposedNamePl = x.ProposedNamePl,
                ProposedNameEn = x.ProposedNameEn,
                ProposedNameUa = x.ProposedNameUa,
                ProposedParentFolderId = x.ProposedParentFolderId,
                Score = x.Score,
                RuleScore = x.RuleScore,
                SemanticScore = x.SemanticScore,
                UserHistoryScore = x.UserHistoryScore,
                FinalScore = x.FinalScore,
                Rank = x.Rank,
                Reason = x.Reason,
                ReasonCode = x.Reason,
                Status = x.Status,
                CreatedAtUtc = x.CreatedAtUtc,
                AcceptedAtUtc = x.AcceptedAtUtc,
                RejectedAtUtc = x.RejectedAtUtc
            })
            .ToList();

        return new DocumentDashboardDto
        {
            TotalDocuments = documents.Count,
            NewDocuments = documents.Count(x => x.IsNew),
            UnfiledDocuments = documents.Count(x => x.FolderId is null),
            PendingReviewDocuments = documents.Count(x =>
                x.FolderClassificationStatus == "suggested" ||
                x.FolderClassificationStatus == "pending"),
            ReadyDocuments = documents.Count(x => x.Status == DocumentStatus.Ready),
            FailedDocuments = documents.Count(x => x.Status == DocumentStatus.Failed),

            ByStatus = documents
                .GroupBy(x => x.Status.ToString())
                .Select(x => new DocumentDashboardBucketDto
                {
                    Key = x.Key,
                    Name = x.Key,
                    Count = x.Count()
                })
                .OrderByDescending(x => x.Count)
                .ToList(),

            ByFolder = documents
                .GroupBy(x => new
                {
                    FolderId = x.FolderId?.ToString() ?? "unfiled",
                    Name = x.Folder != null ? x.Folder.Name : "Unfiled"
                })
                .Select(x => new DocumentDashboardBucketDto
                {
                    Key = x.Key.FolderId,
                    Name = x.Key.Name,
                    Count = x.Count()
                })
                .OrderByDescending(x => x.Count)
                .Take(10)
                .ToList(),

            ByDocumentType = documents
                .GroupBy(x => GuessDocumentType(x.OriginalFileName, x.ExtractedText))
                .Select(x => new DocumentDashboardBucketDto
                {
                    Key = x.Key,
                    Name = ToDisplayName(x.Key),
                    Count = x.Count()
                })
                .OrderByDescending(x => x.Count)
                .ToList(),

            RecentDocuments = documents
                .Take(10)
                .Select(ToDocumentDto)
                .ToList(),

            PendingSuggestions = pendingSuggestions
        };
    }

    public async Task<List<DocumentFolderSuggestionResponseDto>> GetFolderSuggestionsAsync(
    Guid documentId,
    CancellationToken cancellationToken)
    {
        var userId = _currentUserService.GetUserId();

        var exists = await _dbContext.Documents.AnyAsync(
            x => x.Id == documentId && x.UserId == userId,
            cancellationToken);

        if (!exists)
        {
            throw new NotFoundException("Document not found.");
        }

        var suggestions = await _dbContext.DocumentFolderSuggestions
            .Where(x => x.DocumentId == documentId && x.UserId == userId)
            .ToListAsync(cancellationToken);

        suggestions = suggestions
            .OrderBy(x => x.Rank)
            .ThenByDescending(x => x.Score)
            .ToList();

        var existingFolderIds = suggestions
            .Where(x => x.ExistingFolderId.HasValue)
            .Select(x => x.ExistingFolderId!.Value)
            .Distinct()
            .ToList();

        var existingFolders = existingFolderIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await _dbContext.DocumentFolders
                .Where(x => x.UserId == userId && existingFolderIds.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken);

        return suggestions
            .Select(x => new DocumentFolderSuggestionResponseDto
            {
                Id = x.Id,
                DocumentId = x.DocumentId,
                ExistingFolderId = x.ExistingFolderId,
                ExistingFolderName = x.ExistingFolderId.HasValue &&
                                     existingFolders.TryGetValue(x.ExistingFolderId.Value, out var folderName)
                    ? folderName
                    : null,
                ProposedKey = x.ProposedKey,
                ProposedName = x.ProposedName,
                ProposedNamePl = x.ProposedNamePl,
                ProposedNameEn = x.ProposedNameEn,
                ProposedNameUa = x.ProposedNameUa,
                ProposedParentFolderId = x.ProposedParentFolderId,
                Score = x.Score,
                RuleScore = x.RuleScore,
                SemanticScore = x.SemanticScore,
                UserHistoryScore = x.UserHistoryScore,
                FinalScore = x.FinalScore,
                Rank = x.Rank,
                Reason = x.Reason,
                ReasonCode = x.Reason,
                Status = x.Status,
                CreatedAtUtc = x.CreatedAtUtc,
                AcceptedAtUtc = x.AcceptedAtUtc,
                RejectedAtUtc = x.RejectedAtUtc
            })
            .ToList();
    }

    public async Task<DocumentDto> AcceptFolderSuggestionAsync(Guid documentId, Guid suggestionId, CancellationToken cancellationToken)
    {
        var userId = _currentUserService.GetUserId();
        await _permissionService.EnsurePermissionAsync(userId, PermissionKeys.DocumentsView, AccessResourceType.Document, documentId, cancellationToken: cancellationToken);

        var document = await _dbContext.Documents
            .FirstOrDefaultAsync(x => x.Id == documentId, cancellationToken);

        if (document is null)
        {
            throw new NotFoundException("Document not found.");
        }

        var suggestion = await _dbContext.DocumentFolderSuggestions
            .FirstOrDefaultAsync(x => x.Id == suggestionId && x.DocumentId == documentId && x.UserId == userId, cancellationToken);

        if (suggestion is null)
        {
            throw new NotFoundException("Folder suggestion not found.");
        }

        if (suggestion.Status != "pending")
        {
            throw new BadRequestException("Folder suggestion is no longer pending.");
        }

        var folder = suggestion.ExistingFolderId is Guid existingFolderId
            ? await _dbContext.DocumentFolders.FirstOrDefaultAsync(x => x.Id == existingFolderId && x.UserId == userId, cancellationToken)
            : await GetOrCreateSuggestedFolderAsync(userId, suggestion, cancellationToken);

        if (folder is null)
        {
            throw new NotFoundException("Suggested folder not found.");
        }

        document.FolderId = folder.Id;
        document.FolderClassificationStatus = "accepted-suggestion";
        document.FolderClassificationConfidence = suggestion.Score;
        document.FolderClassificationReason = "smart_folder.suggestion_accepted";
        document.WasFolderAutoAssigned = false;
        document.IsNew = false;

        suggestion.Status = "accepted";
        suggestion.AcceptedAtUtc = DateTime.UtcNow;

        var otherSuggestions = await _dbContext.DocumentFolderSuggestions
            .Where(x => x.DocumentId == documentId && x.Id != suggestion.Id && x.Status == "pending")
            .ToListAsync(cancellationToken);

        foreach (var other in otherSuggestions)
        {
            other.Status = "superseded";
        }

        await UpsertUserFolderRuleAsync(userId, folder.Id, document, suggestion, createdFromCorrection: true, cancellationToken);
        await _documentIntelligenceService.UpdateFolderProfileAsync(userId, folder.Id, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return await _dbContext.Documents
            .Include(x => x.Folder)
            .Where(x => x.Id == document.Id)
            .Select(x => new DocumentDto
            {
                Id = x.Id,
                OriginalFileName = x.OriginalFileName,
                ContentType = x.ContentType,
                SizeInBytes = x.SizeInBytes,
                Status = x.Status,
                UploadedAtUtc = x.UploadedAtUtc,
                FolderId = x.FolderId,
                FolderName = x.Folder != null ? x.Folder.Name : null,
                FolderNamePl = x.Folder != null ? x.Folder.NamePl : null,
                FolderNameEn = x.Folder != null ? x.Folder.NameEn : null,
                FolderNameUa = x.Folder != null ? x.Folder.NameUa : null,
                FolderClassificationStatus = x.FolderClassificationStatus,
                    FolderClassificationReason = x.FolderClassificationReason,
                    FolderClassificationReasonCode = x.FolderClassificationReason,
                FolderClassificationConfidence = x.FolderClassificationConfidence,
                WasFolderAutoAssigned = x.WasFolderAutoAssigned,
                IsNew = x.IsNew,
                ProcessingProfile = x.ProcessingProfile
            })
            .FirstAsync(cancellationToken);
    }

    public async Task<DocumentFolderSuggestionResponseDto> RejectFolderSuggestionAsync(Guid documentId, Guid suggestionId, CancellationToken cancellationToken)
    {
        var userId = _currentUserService.GetUserId();

        var document = await _dbContext.Documents
            .FirstOrDefaultAsync(x => x.Id == documentId && x.UserId == userId, cancellationToken);

        if (document is null)
        {
            throw new NotFoundException("Document not found.");
        }

        var suggestion = await _dbContext.DocumentFolderSuggestions
            .Include(x => x.ExistingFolder)
            .FirstOrDefaultAsync(x => x.Id == suggestionId && x.DocumentId == documentId && x.UserId == userId, cancellationToken);

        if (suggestion is null)
        {
            throw new NotFoundException("Folder suggestion not found.");
        }

        if (suggestion.Status == "accepted")
        {
            throw new BadRequestException("Accepted folder suggestion cannot be rejected.");
        }

        var rejectedFolderId = suggestion.ExistingFolderId;
        if (rejectedFolderId is null)
        {
            rejectedFolderId = await _dbContext.DocumentFolders
                .Where(x => x.UserId == userId &&
                            x.ParentFolderId == suggestion.ProposedParentFolderId &&
                            x.Key == suggestion.ProposedKey)
                .Select(x => (Guid?)x.Id)
                .FirstOrDefaultAsync(cancellationToken);
        }

        suggestion.Status = "rejected";
        suggestion.RejectedAtUtc = DateTime.UtcNow;
        document.IsNew = false;

        if (rejectedFolderId is Guid folderId && document.FolderId == folderId)
        {
            document.FolderId = null;
            document.FolderClassificationStatus = "uncategorized";
            document.FolderClassificationConfidence = null;
            document.FolderClassificationReason = "smart_folder.suggestion_rejected_uncategorized";
            document.WasFolderAutoAssigned = false;
            await _documentIntelligenceService.UpdateFolderProfileAsync(userId, folderId, cancellationToken);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        return new DocumentFolderSuggestionResponseDto
        {
            Id = suggestion.Id,
            DocumentId = suggestion.DocumentId,
            ExistingFolderId = suggestion.ExistingFolderId,
            ExistingFolderName = suggestion.ExistingFolder?.Name,
            ProposedKey = suggestion.ProposedKey,
            ProposedName = suggestion.ProposedName,
            ProposedNamePl = suggestion.ProposedNamePl,
            ProposedNameEn = suggestion.ProposedNameEn,
            ProposedNameUa = suggestion.ProposedNameUa,
            ProposedParentFolderId = suggestion.ProposedParentFolderId,
            Score = suggestion.Score,
            RuleScore = suggestion.RuleScore,
            SemanticScore = suggestion.SemanticScore,
            UserHistoryScore = suggestion.UserHistoryScore,
            FinalScore = suggestion.FinalScore,
            Rank = suggestion.Rank,
            Reason = suggestion.Reason,
            ReasonCode = suggestion.Reason,
            Status = suggestion.Status,
            CreatedAtUtc = suggestion.CreatedAtUtc,
            AcceptedAtUtc = suggestion.AcceptedAtUtc,
            RejectedAtUtc = suggestion.RejectedAtUtc
        };
    }

    public async Task<List<RelatedDocumentDto>> GetRelatedDocumentsAsync(Guid documentId, CancellationToken cancellationToken)
    {
        var userId = _currentUserService.GetUserId();

        var document = await _dbContext.Documents
            .FirstOrDefaultAsync(x => x.Id == documentId && x.UserId == userId, cancellationToken);

        if (document is null)
        {
            throw new NotFoundException("Document not found.");
        }

        var sourceText = BuildSearchText(document);
        var sourceTokens = Tokenize(sourceText);
        var sourceType = GuessDocumentType(document.OriginalFileName, document.ExtractedText);

        var candidates = await _dbContext.Documents
            .Include(x => x.Folder)
            .Where(x => x.UserId == userId && x.Id != documentId)
            .ToListAsync(cancellationToken);

        return candidates
            .Select(x =>
            {
                var score = CalculateRelatedScore(document, sourceTokens, sourceType, x);
                return new RelatedDocumentDto
                {
                    DocumentId = x.Id,
                    OriginalFileName = x.OriginalFileName,
                    FolderId = x.FolderId,
                    FolderName = x.Folder?.Name,
                    Status = x.Status,
                    Score = score,
                    Reason = BuildRelatedReason(document, x, sourceType, score),
                    UploadedAtUtc = x.UploadedAtUtc
                };
            })
            .Where(x => x.Score >= 0.15m)
            .OrderByDescending(x => x.Score)
            .ThenByDescending(x => x.UploadedAtUtc)
            .Take(10)
            .ToList();
    }


    public async Task<RegenerateFolderSuggestionsResultDto> RegenerateFolderSuggestionsAsync(
        Guid documentId,
        CancellationToken cancellationToken)
    {
        var userId = _currentUserService.GetUserId();

        var document = await _dbContext.Documents
            .FirstOrDefaultAsync(x => x.Id == documentId && x.UserId == userId, cancellationToken);

        if (document is null)
        {
            throw new NotFoundException("Document not found.");
        }

        var existingFolders = await _dbContext.DocumentFolders
            .Where(x => x.UserId == userId)
            .ToListAsync(cancellationToken);

        var intelligence = await _documentIntelligenceService.EnsureSnapshotAsync(document, cancellationToken);
        var analysis = await _documentFolderClassifier.AnalyzeAsync(document, existingFolders, cancellationToken);
        var previousFolderId = document.FolderId;
        var originalMode = document.OrganizationMode;

        document.OrganizationMode = DocumentOrganizationMode.AutoAssignExistingOnly;
        await _documentFolderDecisionEngine.DecideAsync(document, analysis, existingFolders, cancellationToken);
        document.OrganizationMode = originalMode;
        document.FolderId = previousFolderId;
        document.FolderClassificationStatus = previousFolderId is null ? "suggested" : document.FolderClassificationStatus;
        document.FolderClassificationReason = "smart_folder.suggestions_regenerated";

        await _dbContext.SaveChangesAsync(cancellationToken);

        var suggestions = await GetFolderSuggestionsAsync(documentId, cancellationToken);
        return new RegenerateFolderSuggestionsResultDto
        {
            DocumentId = documentId,
            Intelligence = intelligence,
            Suggestions = suggestions
        };
    }

    public async Task<DocumentIntelligenceSnapshotDto> GetIntelligenceSnapshotAsync(
        Guid documentId,
        CancellationToken cancellationToken)
    {
        var userId = _currentUserService.GetUserId();

        var document = await _dbContext.Documents
            .FirstOrDefaultAsync(x => x.Id == documentId && x.UserId == userId, cancellationToken);

        if (document is null)
        {
            throw new NotFoundException("Document not found.");
        }

        return await _documentIntelligenceService.EnsureSnapshotAsync(document, cancellationToken);
    }

    public async Task<DocumentDetailsDto> GetByIdAsync(Guid documentId, CancellationToken cancellationToken)
    {
        var userId = _currentUserService.GetUserId();
        await _permissionService.EnsurePermissionAsync(userId, PermissionKeys.DocumentsView, AccessResourceType.Document, documentId, cancellationToken: cancellationToken);

        var document = await _dbContext.Documents
            .Where(x => x.Id == documentId)
            .Select(x => new DocumentDetailsDto
            {
                Id = x.Id,
                OriginalFileName = x.OriginalFileName,
                ContentType = x.ContentType,
                SizeInBytes = x.SizeInBytes,
                Status = x.Status,
                Summary = x.Summary,
                UploadedAtUtc = x.UploadedAtUtc,
                OrganizationId = x.OrganizationId,
                Visibility = x.Visibility,
                ProcessedAtUtc = x.ProcessedAtUtc,
                ErrorMessage = x.ErrorMessage,
                FolderId = x.FolderId,
                FolderName = x.Folder != null ? x.Folder.Name : null,
                FolderNamePl = x.Folder != null ? x.Folder.NamePl : null,
                FolderNameEn = x.Folder != null ? x.Folder.NameEn : null,
                FolderNameUa = x.Folder != null ? x.Folder.NameUa : null,
                FolderClassificationStatus = x.FolderClassificationStatus,
                    FolderClassificationReason = x.FolderClassificationReason,
                    FolderClassificationReasonCode = x.FolderClassificationReason,
                FolderClassificationConfidence = x.FolderClassificationConfidence,
                WasFolderAutoAssigned = x.WasFolderAutoAssigned
            })
            .FirstOrDefaultAsync(cancellationToken);

        return document ?? throw new NotFoundException("Document not found.");
    }

    public async Task<DocumentStatusDto> GetStatusAsync(Guid documentId, CancellationToken cancellationToken)
    {
        var userId = _currentUserService.GetUserId();
        await _permissionService.EnsurePermissionAsync(userId, PermissionKeys.DocumentsView, AccessResourceType.Document, documentId, cancellationToken: cancellationToken);

        var document = await _dbContext.Documents
            .Where(x => x.Id == documentId)
            .Select(x => new DocumentStatusDto
            {
                Id = x.Id,
                OriginalFileName = x.OriginalFileName,
                Status = x.Status,
                UploadedAtUtc = x.UploadedAtUtc,
                OrganizationId = x.OrganizationId,
                Visibility = x.Visibility,
                ProcessedAtUtc = x.ProcessedAtUtc,
                ErrorMessage = x.ErrorMessage
            })
            .FirstOrDefaultAsync(cancellationToken);

        return document ?? throw new NotFoundException("Document not found.");
    }

    public async Task<DocumentDto> ConfirmFolderAssignmentAsync(Guid documentId, CancellationToken cancellationToken)
    {
        var userId = _currentUserService.GetUserId();

        var document = await _dbContext.Documents
            .FirstOrDefaultAsync(x => x.Id == documentId && x.UserId == userId, cancellationToken);

        if (document is null)
        {
            throw new NotFoundException("Document not found.");
        }

        document.IsNew = false;
        document.WasFolderAutoAssigned = false;
        document.FolderClassificationStatus = document.FolderId is null
            ? "confirmed-uncategorized"
            : "confirmed";
        document.FolderClassificationReason = document.FolderId is null
            ? "smart_folder.user_confirmed_uncategorized"
            : "smart_folder.user_confirmed_assignment";

        if (document.FolderId is Guid folderId)
        {
            document.FolderClassificationConfidence = 1m;
            await UpsertUserFolderRuleAsync(
                userId,
                folderId,
                document,
                suggestion: null,
                createdFromCorrection: true,
                cancellationToken);
            await _documentIntelligenceService.UpdateFolderProfileAsync(userId, folderId, cancellationToken);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        return await _dbContext.Documents
            .Where(x => x.Id == documentId)
            .Select(x => new DocumentDto
            {
                Id = x.Id,
                OriginalFileName = x.OriginalFileName,
                ContentType = x.ContentType,
                SizeInBytes = x.SizeInBytes,
                Status = x.Status,
                UploadedAtUtc = x.UploadedAtUtc,
                OrganizationId = x.OrganizationId,
                Visibility = x.Visibility,
                FolderId = x.FolderId,
                FolderName = x.Folder != null ? x.Folder.Name : null,
                FolderNamePl = x.Folder != null ? x.Folder.NamePl : null,
                FolderNameEn = x.Folder != null ? x.Folder.NameEn : null,
                FolderNameUa = x.Folder != null ? x.Folder.NameUa : null,
                FolderClassificationStatus = x.FolderClassificationStatus,
                FolderClassificationReason = x.FolderClassificationReason,
                FolderClassificationReasonCode = x.FolderClassificationReason,
                FolderClassificationConfidence = x.FolderClassificationConfidence,
                WasFolderAutoAssigned = x.WasFolderAutoAssigned,
                IsNew = x.IsNew,
                ProcessingProfile = x.ProcessingProfile
            })
            .FirstAsync(cancellationToken);
    }

    public async Task<DocumentDto> MoveToFolderAsync(Guid documentId, MoveDocumentToFolderRequestDto request, CancellationToken cancellationToken)
    {
        var userId = _currentUserService.GetUserId();
        await _permissionService.EnsurePermissionAsync(userId, PermissionKeys.DocumentsEdit, AccessResourceType.Document, documentId, cancellationToken: cancellationToken);

        var document = await _dbContext.Documents
            .FirstOrDefaultAsync(x => x.Id == documentId, cancellationToken);

        if (document is null)
        {
            throw new NotFoundException("Document not found.");
        }

        if (request.FolderId is not null)
        {
            var folderExists = await _dbContext.DocumentFolders.AnyAsync(
                x => x.Id == request.FolderId &&
                     (x.UserId == userId || (document.OrganizationId.HasValue && x.OrganizationId == document.OrganizationId.Value)),
                cancellationToken);

            if (!folderExists)
            {
                throw new BadRequestException("Folder not found.");
            }
        }

        document.FolderId = request.FolderId;
        document.FolderClassificationStatus = "manual";
        document.FolderClassificationReason = request.FolderId is null
            ? "smart_folder.manual_folder_removed"
            : "smart_folder.manual_folder_selected";
        document.FolderClassificationConfidence = request.FolderId is null ? null : 1m;
        document.WasFolderAutoAssigned = false;
        document.IsNew = false;

        if (request.FolderId is Guid movedFolderId)
        {
            await UpsertUserFolderRuleAsync(
                userId,
                movedFolderId,
                document,
                suggestion: null,
                createdFromCorrection: true,
                cancellationToken);
        }

        if (request.FolderId is Guid profileFolderId)
        {
            await _documentIntelligenceService.UpdateFolderProfileAsync(userId, profileFolderId, cancellationToken);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        return await _dbContext.Documents
            .Where(x => x.Id == documentId)
            .Select(x => new DocumentDto
            {
                Id = x.Id,
                OriginalFileName = x.OriginalFileName,
                ContentType = x.ContentType,
                SizeInBytes = x.SizeInBytes,
                Status = x.Status,
                UploadedAtUtc = x.UploadedAtUtc,
                FolderId = x.FolderId,
                FolderName = x.Folder != null ? x.Folder.Name : null,
                FolderNamePl = x.Folder != null ? x.Folder.NamePl : null,
                FolderNameEn = x.Folder != null ? x.Folder.NameEn : null,
                FolderNameUa = x.Folder != null ? x.Folder.NameUa : null,
                FolderClassificationStatus = x.FolderClassificationStatus,
                    FolderClassificationReason = x.FolderClassificationReason,
                    FolderClassificationReasonCode = x.FolderClassificationReason,
                FolderClassificationConfidence = x.FolderClassificationConfidence,
                WasFolderAutoAssigned = x.WasFolderAutoAssigned
            })
            .FirstAsync(cancellationToken);
    }

    public async Task DeleteAsync(Guid documentId, CancellationToken cancellationToken)
    {
        var userId = _currentUserService.GetUserId();

        var document = await _dbContext.Documents
            .FirstOrDefaultAsync(x => x.Id == documentId && x.UserId == userId, cancellationToken);

        if (document is null)
        {
            throw new NotFoundException("Document not found.");
        }

        var actionRuns = await _dbContext.AiActionRuns
            .Where(x => x.DocumentId == document.Id && x.UserId == userId)
            .ToListAsync(cancellationToken);

        if (actionRuns.Count > 0)
        {
            foreach (var run in actionRuns.Where(x => !string.IsNullOrWhiteSpace(x.ResultFilePath)))
            {
                try
                {
                    await _fileStorageService.DeleteAsync(run.ResultFilePath!, cancellationToken);
                }
                catch
                {
                }
            }

            _dbContext.AiActionRuns.RemoveRange(actionRuns);
        }

        _dbContext.Documents.Remove(document);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await _fileStorageService.DeleteAsync(document.StoragePath, cancellationToken);
    }

    public async Task<SummarizeResultDto> SummarizeAsync(
        Guid documentId,
        SummarizeDocumentRequestDto request,
        CancellationToken cancellationToken)
    {
        var userId = _currentUserService.GetUserId();

        var document = await _dbContext.Documents
            .Include(x => x.Chunks)
            .FirstOrDefaultAsync(x => x.Id == documentId && x.UserId == userId, cancellationToken);

        if (document is null)
        {
            throw new NotFoundException("Document not found.");
        }

        EnsureReadyForAi(document);

        await _usageQuotaService.EnsureWithinQuotaAsync(
            userId,
            UsageType.SummarizeDocument,
            1,
            cancellationToken);

        var summary = await _openAiService.GenerateSummaryAsync(
            document.ExtractedText!,
            request.Language,
            cancellationToken);

        document.Summary = summary;
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _usageTrackingService.TrackAsync(
            userId,
            UsageType.SummarizeDocument,
            1,
            cancellationToken,
            model: "gpt-4o-mini",
            referenceId: document.Id.ToString());

        return new SummarizeResultDto
        {
            DocumentId = document.Id,
            Summary = summary
        };
    }

    public async Task<ExtractedDataDto> ExtractAsync(
        Guid documentId,
        ExtractDocumentRequestDto request,
        CancellationToken cancellationToken)
    {
        var userId = _currentUserService.GetUserId();

        var document = await _dbContext.Documents
            .Include(x => x.Chunks)
            .FirstOrDefaultAsync(x => x.Id == documentId && x.UserId == userId, cancellationToken);

        if (document is null)
        {
            throw new NotFoundException("Document not found.");
        }

        EnsureReadyForAi(document);

        await _usageQuotaService.EnsureWithinQuotaAsync(
            userId,
            UsageType.ExtractDocument,
            1,
            cancellationToken);

        var extractionType = string.IsNullOrWhiteSpace(request.ExtractionType)
            ? "generic"
            : request.ExtractionType.Trim();

        var fields = request.Fields
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var context = fields.Count > 0
            ? document.ExtractedText! + "\n\nREQUESTED FIELDS:\n- " + string.Join("\n- ", fields)
            : document.ExtractedText!;

        var jsonResult = await _openAiService.ExtractStructuredDataAsync(
            context,
            extractionType,
            request.Language,
            cancellationToken);

        var extraction = new ExtractedData
        {
            Id = Guid.NewGuid(),
            DocumentId = document.Id,
            ExtractionType = extractionType,
            JsonResult = jsonResult,
            CreatedAtUtc = DateTime.UtcNow
        };

        _dbContext.ExtractedData.Add(extraction);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _usageTrackingService.TrackAsync(
            userId,
            UsageType.ExtractDocument,
            1,
            cancellationToken,
            model: "gpt-4o-mini",
            referenceId: extraction.Id.ToString());

        return new ExtractedDataDto
        {
            Id = extraction.Id,
            DocumentId = extraction.DocumentId,
            ExtractionType = extraction.ExtractionType,
            Fields = fields,
            JsonResult = extraction.JsonResult,
            CreatedAtUtc = extraction.CreatedAtUtc
        };
    }

    public async Task<List<ExtractedDataDto>> GetExtractionsAsync(
        Guid documentId,
        CancellationToken cancellationToken)
    {
        var userId = _currentUserService.GetUserId();

        var documentExists = await _dbContext.Documents
            .AnyAsync(x => x.Id == documentId && x.UserId == userId, cancellationToken);

        if (!documentExists)
        {
            throw new NotFoundException("Document not found.");
        }

        return await _dbContext.ExtractedData
            .Where(x => x.DocumentId == documentId)
            .OrderByDescending(x => x.CreatedAtUtc)
            .Select(x => new ExtractedDataDto
            {
                Id = x.Id,
                DocumentId = x.DocumentId,
                ExtractionType = x.ExtractionType,
                Fields = Array.Empty<string>(),
                JsonResult = x.JsonResult,
                CreatedAtUtc = x.CreatedAtUtc
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<ExtractedDataDto> GetExtractionByIdAsync(
        Guid documentId,
        Guid extractionId,
        CancellationToken cancellationToken)
    {
        var userId = _currentUserService.GetUserId();

        var extraction = await _dbContext.ExtractedData
            .Where(x => x.Id == extractionId && x.DocumentId == documentId && x.Document.UserId == userId)
            .Select(x => new ExtractedDataDto
            {
                Id = x.Id,
                DocumentId = x.DocumentId,
                ExtractionType = x.ExtractionType,
                Fields = Array.Empty<string>(),
                JsonResult = x.JsonResult,
                CreatedAtUtc = x.CreatedAtUtc
            })
            .FirstOrDefaultAsync(cancellationToken);

        return extraction ?? throw new NotFoundException("Extraction not found.");
    }

    public async Task<CompareDocumentsResultDto> CompareAsync(
        Guid firstDocumentId,
        CompareDocumentsRequestDto request,
        CancellationToken cancellationToken)
    {
        var userId = _currentUserService.GetUserId();

        if (firstDocumentId == request.SecondDocumentId)
        {
            throw new BadRequestException("You must choose two different documents.");
        }

        var documents = await _dbContext.Documents
            .Where(x => x.UserId == userId && (x.Id == firstDocumentId || x.Id == request.SecondDocumentId))
            .Include(x => x.Chunks)
            .ToListAsync(cancellationToken);

        var firstDocument = documents.FirstOrDefault(x => x.Id == firstDocumentId);
        var secondDocument = documents.FirstOrDefault(x => x.Id == request.SecondDocumentId);

        if (firstDocument is null || secondDocument is null)
        {
            throw new NotFoundException("One or both documents were not found.");
        }

        EnsureReadyForAi(firstDocument);
        EnsureReadyForAi(secondDocument);

        await _usageQuotaService.EnsureWithinQuotaAsync(
            userId,
            UsageType.CompareDocument,
            1,
            cancellationToken);

        var result = await _openAiService.CompareDocumentsAsync(
            firstDocument.ExtractedText!,
            secondDocument.ExtractedText!,
            request.Prompt,
            request.Language,
            cancellationToken);

        await _usageTrackingService.TrackAsync(
            userId,
            UsageType.CompareDocument,
            1,
            cancellationToken,
            model: "gpt-4o-mini",
            referenceId: $"{firstDocument.Id}:{secondDocument.Id}");

        return new CompareDocumentsResultDto
        {
            FirstDocumentId = firstDocument.Id,
            SecondDocumentId = secondDocument.Id,
            FirstDocumentName = firstDocument.OriginalFileName,
            SecondDocumentName = secondDocument.OriginalFileName,
            Result = result
        };
    }

    private static void EnsureReadyForAi(Document document)
    {
        if (document.Status is DocumentStatus.Queued or DocumentStatus.Processing or DocumentStatus.Uploaded)
        {
            throw new BadRequestException("Document is still being processed. Please try again in a moment.");
        }

        if (document.Status == DocumentStatus.Failed)
        {
            var message = string.IsNullOrWhiteSpace(document.ErrorMessage)
                ? "Document processing failed."
                : $"Document processing failed: {document.ErrorMessage}";

            throw new BadRequestException(message);
        }

        if (string.IsNullOrWhiteSpace(document.ExtractedText))
        {
            throw new BadRequestException("Document is not ready yet. Extracted text is missing.");
        }

        if (document.Chunks is null || document.Chunks.Count == 0)
        {
            throw new BadRequestException("Document is not ready yet. Search chunks are missing.");
        }

        if (document.Status != DocumentStatus.Ready)
        {
            throw new BadRequestException("Document is not ready yet.");
        }
    }

    public async Task<DocumentPreviewMetaDto> GetPreviewMetaAsync(Guid documentId, CancellationToken cancellationToken)
    {
        var userId = _currentUserService.GetUserId();
        await _permissionService.EnsurePermissionAsync(userId, PermissionKeys.DocumentsView, AccessResourceType.Document, documentId, cancellationToken: cancellationToken);

        var document = await _dbContext.Documents
            .Where(x => x.Id == documentId)
            .Select(x => new
            {
                x.Id,
                x.OriginalFileName,
                x.ContentType
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (document is null)
        {
            throw new NotFoundException("Document not found.");
        }

        var extension = Path.GetExtension(document.OriginalFileName).ToLowerInvariant();

        if (string.Equals(document.ContentType, "application/pdf", StringComparison.OrdinalIgnoreCase) || extension == ".pdf")
        {
            return new DocumentPreviewMetaDto
            {
                DocumentId = document.Id,
                FileName = document.OriginalFileName,
                ContentType = document.ContentType,
                PreviewKind = "pdf",
                CanInlinePreview = true
            };
        }

        if (extension == ".docx")
        {
            return new DocumentPreviewMetaDto
            {
                DocumentId = document.Id,
                FileName = document.OriginalFileName,
                ContentType = "text/html",
                PreviewKind = "html",
                CanInlinePreview = true,
                Message = "Preview is generated as HTML from the DOCX structure."
            };
        }

        if (extension == ".doc")
        {
            return new DocumentPreviewMetaDto
            {
                DocumentId = document.Id,
                FileName = document.OriginalFileName,
                ContentType = document.ContentType,
                PreviewKind = "download",
                CanInlinePreview = false,
                Message = "Legacy DOC files cannot be previewed inline without an external converter."
            };
        }

        if (extension is ".txt" or ".md" or ".json" or ".xml" or ".csv" or ".log")
        {
            return new DocumentPreviewMetaDto
            {
                DocumentId = document.Id,
                FileName = document.OriginalFileName,
                ContentType = "text/plain",
                PreviewKind = "text",
                CanInlinePreview = true
            };
        }

        return new DocumentPreviewMetaDto
        {
            DocumentId = document.Id,
            FileName = document.OriginalFileName,
            ContentType = document.ContentType,
            PreviewKind = "download",
            CanInlinePreview = false,
            Message = "Inline preview is not available for this file type."
        };
    }

    public async Task<(Stream Stream, string ContentType, string FileName)> OpenOriginalFileAsync(
    Guid documentId,
    CancellationToken cancellationToken)
    {
        var userId = _currentUserService.GetUserId();
        await _permissionService.EnsurePermissionAsync(userId, PermissionKeys.DocumentsExport, AccessResourceType.Document, documentId, cancellationToken: cancellationToken);

        var document = await _dbContext.Documents
            .Where(x => x.Id == documentId)
            .Select(x => new
            {
                x.StoragePath,
                x.ContentType,
                x.OriginalFileName
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (document is null)
        {
            throw new NotFoundException("Document not found.");
        }

        var stream = File.OpenRead(document.StoragePath);

        return (
            stream,
            string.IsNullOrWhiteSpace(document.ContentType) ? "application/octet-stream" : document.ContentType,
            document.OriginalFileName
        );
    }

    public async Task<(Stream Stream, string ContentType, string FileName)> OpenPreviewFileAsync(
        Guid documentId,
        CancellationToken cancellationToken)
    {
        var userId = _currentUserService.GetUserId();
        await _permissionService.EnsurePermissionAsync(userId, PermissionKeys.DocumentsExport, AccessResourceType.Document, documentId, cancellationToken: cancellationToken);

        var document = await _dbContext.Documents
            .Where(x => x.Id == documentId)
            .Select(x => new
            {
                x.StoragePath,
                x.ContentType,
                x.OriginalFileName
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (document is null)
        {
            throw new NotFoundException("Document not found.");
        }

        return await _documentPreviewConverter.ConvertToPreviewAsync(
            document.StoragePath,
            document.OriginalFileName,
            document.ContentType,
            cancellationToken);
    }

    private async Task<DocumentFolder> GetOrCreateSuggestedFolderAsync(
        Guid userId,
        DocumentFolderSuggestion suggestion,
        CancellationToken cancellationToken)
    {
        if (suggestion.ProposedParentFolderId is Guid parentId)
        {
            var parentExists = await _dbContext.DocumentFolders.AnyAsync(
                x => x.Id == parentId && x.UserId == userId,
                cancellationToken);

            if (!parentExists)
            {
                suggestion.ProposedParentFolderId = null;
            }
        }

        var existing = await _dbContext.DocumentFolders.FirstOrDefaultAsync(
            x => x.UserId == userId &&
                 x.ParentFolderId == suggestion.ProposedParentFolderId &&
                 x.Key == suggestion.ProposedKey,
            cancellationToken);

        if (existing is not null)
        {
            return existing;
        }

        var folder = new DocumentFolder
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            ParentFolderId = suggestion.ProposedParentFolderId,
            Key = suggestion.ProposedKey,
            Name = suggestion.ProposedName,
            NamePl = suggestion.ProposedNamePl,
            NameEn = suggestion.ProposedNameEn,
            NameUa = suggestion.ProposedNameUa,
            IsSystemGenerated = true,
            CreatedAtUtc = DateTime.UtcNow
        };

        _dbContext.DocumentFolders.Add(folder);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return folder;
    }

    private async Task UpsertUserFolderRuleAsync(
        Guid userId,
        Guid folderId,
        Document document,
        DocumentFolderSuggestion? suggestion,
        bool createdFromCorrection,
        CancellationToken cancellationToken)
    {
        var pattern = BuildLearningPattern(document);
        if (string.IsNullOrWhiteSpace(pattern))
        {
            return;
        }

        var documentType = GuessDocumentType(document.OriginalFileName, document.ExtractedText);

        var rule = await _dbContext.UserFolderRules.FirstOrDefaultAsync(
            x => x.UserId == userId && x.FolderId == folderId && x.Pattern == pattern,
            cancellationToken);

        if (rule is null)
        {
            _dbContext.UserFolderRules.Add(new UserFolderRule
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                FolderId = folderId,
                Pattern = pattern,
                DocumentType = documentType,
                Topic = suggestion?.ProposedKey,
                Weight = createdFromCorrection ? 1.25m : 1m,
                CreatedFromCorrection = createdFromCorrection,
                CreatedAtUtc = DateTime.UtcNow,
                LastMatchedAtUtc = DateTime.UtcNow
            });

            return;
        }

        rule.Weight = Math.Min(rule.Weight + 0.25m, 5m);
        rule.LastMatchedAtUtc = DateTime.UtcNow;
    }

    private static string BuildLearningPattern(Document document)
    {
        var source = BuildSearchText(document);
        var tokens = Tokenize(source)
            .Where(x => x.Length >= 4)
            .Take(8)
            .ToList();

        return string.Join(' ', tokens);
    }

    private static decimal CalculateRelatedScore(
        Document source,
        HashSet<string> sourceTokens,
        string sourceType,
        Document candidate)
    {
        var candidateText = BuildSearchText(candidate);
        var candidateTokens = Tokenize(candidateText);

        var shared = sourceTokens.Intersect(candidateTokens).Count();
        var union = sourceTokens.Union(candidateTokens).Count();
        var tokenScore = union == 0 ? 0m : (decimal)shared / union;

        var folderScore = source.FolderId is not null && source.FolderId == candidate.FolderId ? 0.25m : 0m;
        var typeScore = sourceType == GuessDocumentType(candidate.OriginalFileName, candidate.ExtractedText) ? 0.25m : 0m;
        var filenameScore = Path.GetFileNameWithoutExtension(source.OriginalFileName)
            .Split(new[] { '-', '_', ' ', '.', ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(part => candidate.OriginalFileName.Contains(part, StringComparison.OrdinalIgnoreCase) && part.Length >= 4)
            ? 0.15m
            : 0m;

        return Math.Round(Math.Min(1m, tokenScore + folderScore + typeScore + filenameScore), 4);
    }

    private static string BuildRelatedReason(Document source, Document candidate, string sourceType, decimal score)
    {
        if (source.FolderId is not null && source.FolderId == candidate.FolderId)
        {
            return "Same folder and similar document signals.";
        }

        if (sourceType == GuessDocumentType(candidate.OriginalFileName, candidate.ExtractedText))
        {
            return $"Same detected document type: {ToDisplayName(sourceType)}.";
        }

        return score >= 0.35m
            ? "Similar keywords and document metadata."
            : "Weak semantic similarity based on keywords and metadata.";
    }

    private static DocumentDto ToDocumentDto(Document document)
    {
        return new DocumentDto
        {
            Id = document.Id,
            OriginalFileName = document.OriginalFileName,
            ContentType = document.ContentType,
            SizeInBytes = document.SizeInBytes,
            Status = document.Status,
            UploadedAtUtc = document.UploadedAtUtc,
            OrganizationId = document.OrganizationId,
            Visibility = document.Visibility,
            FolderId = document.FolderId,
            FolderName = document.Folder?.Name,
            FolderNamePl = document.Folder?.NamePl,
            FolderNameEn = document.Folder?.NameEn,
            FolderNameUa = document.Folder?.NameUa,
            FolderClassificationStatus = document.FolderClassificationStatus,
            FolderClassificationReason = document.FolderClassificationReason,
            FolderClassificationReasonCode = document.FolderClassificationReason,
            FolderClassificationConfidence = document.FolderClassificationConfidence,
            WasFolderAutoAssigned = document.WasFolderAutoAssigned,
            IsNew = document.IsNew,
            ProcessingProfile = document.ProcessingProfile
        };
    }

    private static string BuildSearchText(Document document)
    {
        var builder = new StringBuilder();
        builder.AppendLine(document.OriginalFileName);
        builder.AppendLine(document.ContentType);
        if (!string.IsNullOrWhiteSpace(document.QuickSummary)) builder.AppendLine(document.QuickSummary);
        if (!string.IsNullOrWhiteSpace(document.Summary)) builder.AppendLine(document.Summary);
        if (!string.IsNullOrWhiteSpace(document.ExtractedText))
        {
            builder.AppendLine(document.ExtractedText[..Math.Min(document.ExtractedText.Length, 3000)]);
        }
        return builder.ToString();
    }

    private static HashSet<string> Tokenize(string text)
    {
        return Regex.Matches(text.ToLowerInvariant(), @"[\p{L}\p{N}]{3,}")
            .Select(x => x.Value)
            .Where(x => !StopWords.Contains(x))
            .Take(250)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static string GuessDocumentType(string fileName, string? text)
    {
        var source = ($"{fileName} {text ?? string.Empty}").ToLowerInvariant();
        if (source.Contains("invoice") || source.Contains("faktura") || source.Contains("vat") || source.Contains("seller") || source.Contains("buyer")) return "invoice";
        if (source.Contains("contract") || source.Contains("agreement") || source.Contains("umowa") || source.Contains("parties")) return "contract";
        if (source.Contains("cv") || source.Contains("resume") || source.Contains("curriculum") || source.Contains("candidate") || source.Contains("skills")) return "cv";
        if (source.Contains("api") || source.Contains("swagger") || source.Contains("openapi") || source.Contains("documentation")) return "documentation";
        if (source.Contains("report") || source.Contains("raport") || source.Contains("analysis")) return "report";
        return "other";
    }

    private static string ToDisplayName(string key)
    {
        return key switch
        {
            "invoice" => "Invoices",
            "contract" => "Contracts",
            "cv" => "CVs",
            "documentation" => "Documentation",
            "report" => "Reports",
            _ => "Other"
        };
    }

    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "the", "and", "for", "with", "from", "this", "that", "document", "file", "oraz", "jest", "dla", "or", "are", "was", "were"
    };

}