using System.Text.Json;
using AI.DocumentAssistant.Application.Abstractions.AI;
using AI.DocumentAssistant.Application.Abstractions.Common;
using AI.DocumentAssistant.Application.Abstractions.Documents;
using AI.DocumentAssistant.Application.Common.Exceptions;
using AI.DocumentAssistant.Application.Documents.Dtos;
using AI.DocumentAssistant.Domain.Entities;
using AI.DocumentAssistant.Domain.Enums;
using AI.DocumentAssistant.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace AI.DocumentAssistant.Application.Documents.Services;

public sealed class DocumentService : IDocumentService
{
    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf",
        ".docx",
        ".txt",
        ".md",
        ".markdown",
        ".csv",
        ".json",
        ".xml",
        ".log"
    };

    private readonly AppDbContext _dbContext;
    private readonly ICurrentUserService _currentUserService;
    private readonly IFileStorageService _fileStorageService;
    private readonly IDocumentProcessingQueue _documentProcessingQueue;
    private readonly IOpenAiService _openAiService;

    public DocumentService(
        AppDbContext dbContext,
        ICurrentUserService currentUserService,
        IFileStorageService fileStorageService,
        IDocumentProcessingQueue documentProcessingQueue,
        IOpenAiService openAiService)
    {
        _dbContext = dbContext;
        _currentUserService = currentUserService;
        _fileStorageService = fileStorageService;
        _documentProcessingQueue = documentProcessingQueue;
        _openAiService = openAiService;
    }

    public async Task<DocumentDto> UploadAsync(IFormFile file, CancellationToken cancellationToken)
    {
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
            throw new BadRequestException(
                "Unsupported file type. Allowed: pdf, docx, txt, md, csv, json, xml, log.");
        }

        var userId = _currentUserService.GetUserId();
        var fileName = $"{Guid.NewGuid():N}{extension.ToLowerInvariant()}";

        await using var stream = file.OpenReadStream();
        var storagePath = await _fileStorageService.SaveAsync(stream, fileName, cancellationToken);

        var document = new Document
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            FileName = fileName,
            OriginalFileName = file.FileName,
            ContentType = file.ContentType ?? "application/octet-stream",
            SizeInBytes = file.Length,
            StoragePath = storagePath,
            Status = DocumentStatus.Uploaded,
            UploadedAtUtc = DateTime.UtcNow
        };

        _dbContext.Documents.Add(document);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _documentProcessingQueue.EnqueueAsync(document.Id, cancellationToken);

        return new DocumentDto
        {
            Id = document.Id,
            OriginalFileName = document.OriginalFileName,
            ContentType = document.ContentType,
            SizeInBytes = document.SizeInBytes,
            Status = document.Status,
            UploadedAtUtc = document.UploadedAtUtc
        };
    }

    public async Task<IReadOnlyList<DocumentDto>> GetAllAsync(CancellationToken cancellationToken)
    {
        var userId = _currentUserService.GetUserId();

        return await _dbContext.Documents
            .Where(x => x.UserId == userId)
            .OrderByDescending(x => x.UploadedAtUtc)
            .Select(x => new DocumentDto
            {
                Id = x.Id,
                OriginalFileName = x.OriginalFileName,
                ContentType = x.ContentType,
                SizeInBytes = x.SizeInBytes,
                Status = x.Status,
                UploadedAtUtc = x.UploadedAtUtc
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
                ErrorMessage = x.ErrorMessage
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

    public async Task<SummarizeResultDto> SummarizeAsync(Guid documentId, CancellationToken cancellationToken)
    {
        var userId = _currentUserService.GetUserId();

        var document = await _dbContext.Documents
            .FirstOrDefaultAsync(x => x.Id == documentId && x.UserId == userId, cancellationToken);

        if (document is null)
        {
            throw new NotFoundException("Document not found.");
        }

        EnsureReadyForAi(document);

        var summary = await _openAiService.GenerateSummaryAsync(document.ExtractedText!, cancellationToken);
        document.Summary = summary;

        await _dbContext.SaveChangesAsync(cancellationToken);

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
            .FirstOrDefaultAsync(x => x.Id == documentId && x.UserId == userId, cancellationToken);

        if (document is null)
        {
            throw new NotFoundException("Document not found.");
        }

        EnsureReadyForAi(document);

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

    public async Task<IReadOnlyList<ExtractedDataDto>> GetExtractionsAsync(
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

    private static void EnsureReadyForAi(Document document)
    {
        if (document.Status != DocumentStatus.Ready)
        {
            throw new BadRequestException("Document is not ready yet.");
        }

        if (string.IsNullOrWhiteSpace(document.ExtractedText))
        {
            throw new BadRequestException("Document text has not been processed yet.");
        }
    }
}