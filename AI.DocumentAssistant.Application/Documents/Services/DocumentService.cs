using AI.DocumentAssistant.Application.Abstractions.AI;
using AI.DocumentAssistant.Application.Abstractions.Common;
using AI.DocumentAssistant.Application.Abstractions.Documents;
using AI.DocumentAssistant.Application.Abstractions.Usage;
using AI.DocumentAssistant.Application.Common.Exceptions;
using AI.DocumentAssistant.Application.Documents.Dtos;
using AI.DocumentAssistant.Application.Services.Authentication;
using AI.DocumentAssistant.Domain.Entities;
using AI.DocumentAssistant.Domain.Enums;
using AI.DocumentAssistant.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using System.Text;

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

    public DocumentService(
        AppDbContext dbContext,
        ICurrentUserService currentUserService,
        IFileStorageService fileStorageService,
        IDocumentProcessingQueue documentProcessingQueue,
        IOpenAiService openAiService,
        IUsageQuotaService usageQuotaService,
        IUsageTrackingService usageTrackingService,
        IDocumentPreviewConverter documentPreviewConverter)
    {
        _dbContext = dbContext;
        _currentUserService = currentUserService;
        _fileStorageService = fileStorageService;
        _documentProcessingQueue = documentProcessingQueue;
        _openAiService = openAiService;
        _usageQuotaService = usageQuotaService;
        _usageTrackingService = usageTrackingService;
        _documentPreviewConverter = documentPreviewConverter;
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

        if (request.FolderId is not null)
        {
            var folderExists = await _dbContext.DocumentFolders.AnyAsync(
                x => x.Id == request.FolderId && x.UserId == userId,
                cancellationToken);

            if (!folderExists)
            {
                throw new BadRequestException("Selected folder does not exist.");
            }
        }

        var fileName = $"{Guid.NewGuid():N}{extension.ToLowerInvariant()}";

        await using var stream = file.OpenReadStream();
        var storagePath = await _fileStorageService.SaveAsync(stream, fileName, cancellationToken);

        var organizationMode = ResolveOrganizationMode(request);

        var processingProfile = DocumentProcessingProfileResolver.Resolve(file.FileName, file.ContentType);

        var document = new Document
        {
            Id = Guid.NewGuid(),
            UserId = userId,
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
                FolderId = x.FolderId,
                FolderName = x.Folder != null ? x.Folder.Name : null,
                FolderNamePl = x.Folder != null ? x.Folder.NamePl : null,
                FolderNameEn = x.Folder != null ? x.Folder.NameEn : null,
                FolderNameUa = x.Folder != null ? x.Folder.NameUa : null,
                FolderClassificationStatus = x.FolderClassificationStatus,
                FolderClassificationConfidence = x.FolderClassificationConfidence,
                WasFolderAutoAssigned = x.WasFolderAutoAssigned,
                IsNew = x.IsNew,
                ProcessingProfile = x.ProcessingProfile,
            })
            .FirstAsync(cancellationToken);
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
                    SmartOrganize = request.SmartOrganize,
                    AllowSystemFolderCreation = request.AllowSystemFolderCreation
                },
                cancellationToken);

            result.Documents.Add(singleResult);
        }

        return result;
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
            return "Folder selected by user.";
        }

        return request.SmartOrganize
            ? "Document queued for smart organization."
            : "Smart organization disabled by user.";
    }

    public async Task<List<DocumentDto>> GetAllAsync(Guid? folderId, CancellationToken cancellationToken)
    {
        var userId = _currentUserService.GetUserId();

        var query = _dbContext.Documents.Where(x => x.UserId == userId);

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
                FolderId = x.FolderId,
                FolderName = x.Folder != null ? x.Folder.Name : null,
                FolderNamePl = x.Folder != null ? x.Folder.NamePl : null,
                FolderNameEn = x.Folder != null ? x.Folder.NameEn : null,
                FolderNameUa = x.Folder != null ? x.Folder.NameUa : null,
                FolderClassificationStatus = x.FolderClassificationStatus,
                FolderClassificationConfidence = x.FolderClassificationConfidence,
                WasFolderAutoAssigned = x.WasFolderAutoAssigned
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<DocumentDetailsDto> GetByIdAsync(Guid documentId, CancellationToken cancellationToken)
    {
        var userId = _currentUserService.GetUserId();

        var document = await _dbContext.Documents
            .Where(x => x.Id == documentId && x.UserId == userId)
            .Select(x => new DocumentDetailsDto
            {
                Id = x.Id,
                OriginalFileName = x.OriginalFileName,
                ContentType = x.ContentType,
                SizeInBytes = x.SizeInBytes,
                Status = x.Status,
                Summary = x.Summary,
                UploadedAtUtc = x.UploadedAtUtc,
                ProcessedAtUtc = x.ProcessedAtUtc,
                ErrorMessage = x.ErrorMessage,
                FolderId = x.FolderId,
                FolderName = x.Folder != null ? x.Folder.Name : null,
                FolderNamePl = x.Folder != null ? x.Folder.NamePl : null,
                FolderNameEn = x.Folder != null ? x.Folder.NameEn : null,
                FolderNameUa = x.Folder != null ? x.Folder.NameUa : null,
                FolderClassificationStatus = x.FolderClassificationStatus,
                FolderClassificationConfidence = x.FolderClassificationConfidence,
                WasFolderAutoAssigned = x.WasFolderAutoAssigned
            })
            .FirstOrDefaultAsync(cancellationToken);

        return document ?? throw new NotFoundException("Document not found.");
    }

    public async Task<DocumentStatusDto> GetStatusAsync(Guid documentId, CancellationToken cancellationToken)
    {
        var userId = _currentUserService.GetUserId();

        var document = await _dbContext.Documents
            .Where(x => x.Id == documentId && x.UserId == userId)
            .Select(x => new DocumentStatusDto
            {
                Id = x.Id,
                OriginalFileName = x.OriginalFileName,
                Status = x.Status,
                UploadedAtUtc = x.UploadedAtUtc,
                ProcessedAtUtc = x.ProcessedAtUtc,
                ErrorMessage = x.ErrorMessage
            })
            .FirstOrDefaultAsync(cancellationToken);

        return document ?? throw new NotFoundException("Document not found.");
    }

    public async Task<DocumentDto> MoveToFolderAsync(Guid documentId, MoveDocumentToFolderRequestDto request, CancellationToken cancellationToken)
    {
        var userId = _currentUserService.GetUserId();

        var document = await _dbContext.Documents
            .FirstOrDefaultAsync(x => x.Id == documentId && x.UserId == userId, cancellationToken);

        if (document is null)
        {
            throw new NotFoundException("Document not found.");
        }

        if (request.FolderId is not null)
        {
            var folderExists = await _dbContext.DocumentFolders.AnyAsync(
                x => x.Id == request.FolderId && x.UserId == userId,
                cancellationToken);

            if (!folderExists)
            {
                throw new BadRequestException("Folder not found.");
            }
        }

        document.FolderId = request.FolderId;
        document.FolderClassificationStatus = "manual";
        document.WasFolderAutoAssigned = false;

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

        var document = await _dbContext.Documents
            .Where(x => x.Id == documentId && x.UserId == userId)
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

        var document = await _dbContext.Documents
            .Where(x => x.Id == documentId && x.UserId == userId)
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

        var document = await _dbContext.Documents
            .Where(x => x.Id == documentId && x.UserId == userId)
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
}