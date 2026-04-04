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

        if (document.Status != DocumentStatus.Ready)
        {
            throw new BadRequestException("Document is not ready yet.");
        }

        if (string.IsNullOrWhiteSpace(document.ExtractedText))
        {
            throw new BadRequestException("Document text has not been processed yet.");
        }

        var summary = await _openAiService.GenerateSummaryAsync(document.ExtractedText, cancellationToken);
        document.Summary = summary;

        await _dbContext.SaveChangesAsync(cancellationToken);

        return new SummarizeResultDto
        {
            DocumentId = document.Id,
            Summary = summary
        };
    }

    public async Task<ExtractDocumentResultDto> ExtractAsync(
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

        if (document.Status != DocumentStatus.Ready)
        {
            throw new BadRequestException("Document is not ready yet.");
        }

        if (string.IsNullOrWhiteSpace(document.ExtractedText))
        {
            throw new BadRequestException("Document text has not been processed yet.");
        }

        var extractionType = string.IsNullOrWhiteSpace(request.ExtractionType)
            ? "generic"
            : request.ExtractionType.Trim();

        var promptSuffix = request.Fields.Count > 0
            ? $"\nREQUESTED FIELDS:\n- {string.Join("\n- ", request.Fields)}"
            : "\nREQUESTED FIELDS:\n- infer the most relevant fields dynamically";

        var rawJson = await _openAiService.ExtractStructuredDataAsync(
            document.ExtractedText + promptSuffix,
            extractionType,
            cancellationToken);

        return new ExtractDocumentResultDto
        {
            DocumentId = document.Id,
            ExtractionType = extractionType,
            RawJson = rawJson
        };
    }
}