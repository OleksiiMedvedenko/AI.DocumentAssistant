using AI.DocumentAssistant.Application.Abstractions.Documents;
using AI.DocumentAssistant.Domain.Entities;
using AI.DocumentAssistant.Domain.Enums;
using AI.DocumentAssistant.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AI.DocumentAssistant.Application.Documents.Services
{
    public sealed class DocumentProcessingService : IDocumentProcessingService
    {
        private readonly AppDbContext _dbContext;
        private readonly IFileStorageService _fileStorageService;
        private readonly IPdfTextExtractor _pdfTextExtractor;
        private readonly IDocumentChunkingService _documentChunkingService;

        public DocumentProcessingService(
            AppDbContext dbContext,
            IFileStorageService fileStorageService,
            IPdfTextExtractor pdfTextExtractor,
            IDocumentChunkingService documentChunkingService)
        {
            _dbContext = dbContext;
            _fileStorageService = fileStorageService;
            _pdfTextExtractor = pdfTextExtractor;
            _documentChunkingService = documentChunkingService;
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

            try
            {
                document.Status = DocumentStatus.Processing;
                await _dbContext.SaveChangesAsync(cancellationToken);

                await using var stream = await _fileStorageService.OpenReadAsync(document.StoragePath, cancellationToken);
                var text = await _pdfTextExtractor.ExtractTextAsync(stream, cancellationToken);

                document.ExtractedText = text;
                document.Chunks.Clear();

                var chunks = _documentChunkingService.Chunk(text);
                for (var i = 0; i < chunks.Count; i++)
                {
                    document.Chunks.Add(new DocumentChunk
                    {
                        Id = Guid.NewGuid(),
                        DocumentId = document.Id,
                        ChunkIndex = i,
                        Text = chunks[i]
                    });
                }

                document.Status = DocumentStatus.Ready;
                document.ProcessedAtUtc = DateTime.UtcNow;
                document.ErrorMessage = null;

                await _dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                document.Status = DocumentStatus.Failed;
                document.ErrorMessage = ex.Message;
                await _dbContext.SaveChangesAsync(cancellationToken);
            }
        }
    }
}
