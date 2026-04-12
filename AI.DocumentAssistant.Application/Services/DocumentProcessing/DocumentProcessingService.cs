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
    private readonly IDocumentFolderDecisionEngine _documentFolderDecisionEngine;

    public DocumentProcessingService(
        AppDbContext dbContext,
        IFileStorageService fileStorageService,
        IEnumerable<IDocumentTextExtractor> textExtractors,
        IDocumentChunkingService chunkingService,
        IEmbeddingService embeddingService,
        IDocumentFolderClassifier documentFolderClassifier,
        IDocumentFolderDecisionEngine documentFolderDecisionEngine)
    {
        _dbContext = dbContext;
        _fileStorageService = fileStorageService;
        _textExtractors = textExtractors;
        _chunkingService = chunkingService;
        _embeddingService = embeddingService;
        _documentFolderClassifier = documentFolderClassifier;
        _documentFolderDecisionEngine = documentFolderDecisionEngine;
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
        document.LastProcessingAttemptAtUtc = DateTime.UtcNow;
        document.ProcessingAttemptCount += 1;

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

            await OrganizeDocumentAsync(document, cancellationToken);

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

    private async Task OrganizeDocumentAsync(Document document, CancellationToken cancellationToken)
    {
        if (document.FolderId is not null)
        {
            document.FolderClassificationStatus = "manual";
            document.FolderClassificationConfidence = 1m;
            document.FolderClassificationReason = "Folder selected manually during upload.";
            document.WasFolderAutoAssigned = false;
            return;
        }

        if (document.OrganizationMode == DocumentOrganizationMode.Disabled)
        {
            document.FolderClassificationStatus = "disabled";
            document.FolderClassificationConfidence = null;
            document.FolderClassificationReason = "Smart organization disabled for this document.";
            document.WasFolderAutoAssigned = false;
            return;
        }

        var existingFolders = await _dbContext.DocumentFolders
            .Where(x => x.UserId == document.UserId)
            .ToListAsync(cancellationToken);

        var analysis = await _documentFolderClassifier.AnalyzeAsync(
            document,
            existingFolders,
            cancellationToken);

        var decision = await _documentFolderDecisionEngine.DecideAsync(
            document,
            analysis,
            existingFolders,
            cancellationToken);

        document.FolderId = decision.FolderId;
        document.FolderClassificationStatus = decision.Status;
        document.FolderClassificationConfidence = decision.Confidence;
        document.FolderClassificationReason = decision.Reason;
        document.WasFolderAutoAssigned = decision.AutoAssigned;
    }
}