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

    public DocumentProcessingService(
        AppDbContext dbContext,
        IFileStorageService fileStorageService,
        IEnumerable<IDocumentTextExtractor> textExtractors,
        IDocumentChunkingService chunkingService)
    {
        _dbContext = dbContext;
        _fileStorageService = fileStorageService;
        _textExtractors = textExtractors;
        _chunkingService = chunkingService;
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
            var extractor = _textExtractors.FirstOrDefault(x =>
                x.CanHandle(document.OriginalFileName, document.ContentType));

            if (extractor is null)
            {
                throw new InvalidOperationException(
                    $"Unsupported file type: {document.OriginalFileName} ({document.ContentType}).");
            }

            await using var stream = await _fileStorageService.OpenReadAsync(document.StoragePath, cancellationToken);
            var extractedText = await extractor.ExtractTextAsync(stream, cancellationToken);

            document.ExtractedText = extractedText;

            if (document.Chunks.Count > 0)
            {
                _dbContext.DocumentChunks.RemoveRange(document.Chunks);
            }

            var chunks = _chunkingService.Chunk(extractedText);

            var entities = chunks
                .Select((text, index) => new DocumentChunk
                {
                    Id = Guid.NewGuid(),
                    DocumentId = document.Id,
                    ChunkIndex = index,
                    Text = text
                })
                .ToList();

            _dbContext.DocumentChunks.AddRange(entities);

            document.Status = DocumentStatus.Ready;
            document.ProcessedAtUtc = DateTime.UtcNow;
            document.ErrorMessage = null;

            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            document.Status = DocumentStatus.Failed;
            document.ErrorMessage = ex.Message;
            document.ProcessedAtUtc = DateTime.UtcNow;

            await _dbContext.SaveChangesAsync(cancellationToken);
        }
    }
}