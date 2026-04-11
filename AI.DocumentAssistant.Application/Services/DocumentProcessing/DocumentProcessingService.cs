using AI.DocumentAssistant.Application.Abstractions.AI;
using AI.DocumentAssistant.Application.Abstractions.Documents;
using AI.DocumentAssistant.Domain.Entities;
using AI.DocumentAssistant.Domain.Enums;
using AI.DocumentAssistant.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AI.DocumentAssistant.Application.Services.DocumentProcessing;

public sealed class DocumentProcessingService : IDocumentProcessingService
{
    private readonly AppDbContext _dbContext;
    private readonly IFileStorageService _fileStorageService;
    private readonly IEnumerable<IDocumentTextExtractor> _textExtractors;
    private readonly IDocumentChunkingService _chunkingService;
    private readonly IEmbeddingService _embeddingService;
    private readonly IDocumentFolderClassifier _documentFolderClassifier;

    public DocumentProcessingService(
        AppDbContext dbContext,
        IFileStorageService fileStorageService,
        IEnumerable<IDocumentTextExtractor> textExtractors,
        IDocumentChunkingService chunkingService,
        IEmbeddingService embeddingService,
        IDocumentFolderClassifier documentFolderClassifier)
    {
        _dbContext = dbContext;
        _fileStorageService = fileStorageService;
        _textExtractors = textExtractors;
        _chunkingService = chunkingService;
        _embeddingService = embeddingService;
        _documentFolderClassifier = documentFolderClassifier;
    }

    public async Task ProcessAsync(Guid documentId, CancellationToken cancellationToken)
    {
        var document = await _dbContext.Documents
            .Include(x => x.Chunks)
            .FirstOrDefaultAsync(x => x.Id == documentId, cancellationToken);

        if (document is null)
        {
            return;
        }

        document.Status = DocumentStatus.Processing;
        document.ErrorMessage = null;
        await _dbContext.SaveChangesAsync(cancellationToken);

        try
        {
            var extractor = _textExtractors.FirstOrDefault(
                x => x.CanHandle(document.OriginalFileName, document.ContentType));

            if (extractor is null)
            {
                throw new InvalidOperationException(
                    $"Unsupported file type: {document.OriginalFileName} ({document.ContentType}).");
            }

            await using var stream = await _fileStorageService.OpenReadAsync(document.StoragePath, cancellationToken);
            var extractedText = await extractor.ExtractTextAsync(stream, cancellationToken);

            if (string.IsNullOrWhiteSpace(extractedText))
            {
                throw new InvalidOperationException("Could not extract text from the document.");
            }

            document.ExtractedText = extractedText.Trim();

            if (document.Chunks.Count > 0)
            {
                _dbContext.DocumentChunks.RemoveRange(document.Chunks);
                await _dbContext.SaveChangesAsync(cancellationToken);
            }

            var chunks = _chunkingService.Chunk(document.ExtractedText);

            if (chunks.Count == 0)
            {
                throw new InvalidOperationException("Document text was extracted, but no chunks could be created.");
            }

            var entities = new List<DocumentChunk>(chunks.Count);

            for (var index = 0; index < chunks.Count; index++)
            {
                var chunkText = chunks[index];
                float[]? embedding = null;

                try
                {
                    embedding = await _embeddingService.GenerateEmbeddingAsync(chunkText, cancellationToken);
                }
                catch
                {
                    embedding = null;
                }

                entities.Add(new DocumentChunk
                {
                    Id = Guid.NewGuid(),
                    DocumentId = document.Id,
                    ChunkIndex = index,
                    Text = chunkText,
                    Embedding = embedding
                });
            }

            _dbContext.DocumentChunks.AddRange(entities);
            await _dbContext.SaveChangesAsync(cancellationToken);

            var createdChunkCount = await _dbContext.DocumentChunks
                .CountAsync(x => x.DocumentId == document.Id, cancellationToken);

            if (createdChunkCount == 0)
            {
                throw new InvalidOperationException("Document processing did not create any chunks.");
            }

            await AutoAssignFolderAsync(document, cancellationToken);

            document.Status = DocumentStatus.Ready;
            document.ProcessedAtUtc = DateTime.UtcNow;
            document.ErrorMessage = null;

            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            document.Status = DocumentStatus.Failed;
            document.ErrorMessage = ex.Message[..Math.Min(ex.Message.Length, 2000)];
            document.ProcessedAtUtc = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    private async Task AutoAssignFolderAsync(Document document, CancellationToken cancellationToken)
    {
        if (document.FolderId is not null)
        {
            document.FolderClassificationStatus = "manual";
            document.FolderClassificationConfidence ??= 1m;
            document.WasFolderAutoAssigned = false;
            return;
        }

        var existingFolders = await _dbContext.DocumentFolders
            .Where(x => x.UserId == document.UserId)
            .ToListAsync(cancellationToken);

        var suggestion = await _documentFolderClassifier.SuggestAsync(document, existingFolders, cancellationToken);

        if (suggestion is null)
        {
            document.FolderClassificationStatus = "uncategorized";
            document.FolderClassificationConfidence = 0m;
            document.WasFolderAutoAssigned = false;
            return;
        }

        document.FolderClassificationConfidence = suggestion.Confidence;

        if (suggestion.ExistingFolderId is Guid existingFolderId && suggestion.Confidence >= 0.70m)
        {
            document.FolderId = existingFolderId;
            document.FolderClassificationStatus = "auto-assigned";
            document.WasFolderAutoAssigned = true;
            return;
        }

        if (suggestion.Confidence >= 0.85m)
        {
            var duplicate = await _dbContext.DocumentFolders.FirstOrDefaultAsync(
                x => x.UserId == document.UserId &&
                     x.ParentFolderId == null &&
                     x.Key == suggestion.ProposedKey,
                cancellationToken);

            if (duplicate is null)
            {
                duplicate = new DocumentFolder
                {
                    Id = Guid.NewGuid(),
                    UserId = document.UserId,
                    ParentFolderId = null,
                    Key = suggestion.ProposedKey,
                    Name = suggestion.ProposedName,
                    NamePl = suggestion.ProposedNamePl,
                    NameEn = suggestion.ProposedNameEn,
                    NameUa = suggestion.ProposedNameUa,
                    IsSystemGenerated = true,
                    CreatedAtUtc = DateTime.UtcNow
                };

                _dbContext.DocumentFolders.Add(duplicate);
                await _dbContext.SaveChangesAsync(cancellationToken);
            }

            document.FolderId = duplicate.Id;
            document.FolderClassificationStatus = "auto-created-and-assigned";
            document.WasFolderAutoAssigned = true;
            return;
        }

        document.FolderClassificationStatus = "suggested";
        document.WasFolderAutoAssigned = false;
    }
}