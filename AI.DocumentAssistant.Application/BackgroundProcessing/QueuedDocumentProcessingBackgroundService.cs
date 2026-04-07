using AI.DocumentAssistant.Application.Abstractions.Documents;
using AI.DocumentAssistant.Domain.Enums;
using AI.DocumentAssistant.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AI.DocumentAssistant.Infrastructure.BackgroundProcessing;

public sealed class QueuedDocumentProcessingBackgroundService : BackgroundService
{
    private const int MaxAttempts = 3;

    private readonly IDocumentProcessingQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<QueuedDocumentProcessingBackgroundService> _logger;

    public QueuedDocumentProcessingBackgroundService(
        IDocumentProcessingQueue queue,
        IServiceScopeFactory scopeFactory,
        ILogger<QueuedDocumentProcessingBackgroundService> logger)
    {
        _queue = queue;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Document processing background worker started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            Guid documentId = Guid.Empty;

            try
            {
                documentId = await _queue.DequeueAsync(stoppingToken);

                for (var attempt = 1; attempt <= MaxAttempts; attempt++)
                {
                    try
                    {
                        using var scope = _scopeFactory.CreateScope();
                        var processor = scope.ServiceProvider.GetRequiredService<IDocumentProcessingService>();

                        _logger.LogInformation(
                            "Processing document {DocumentId}, attempt {Attempt}/{MaxAttempts}",
                            documentId,
                            attempt,
                            MaxAttempts);

                        await processor.ProcessAsync(documentId, stoppingToken);

                        _logger.LogInformation("Finished processing document {DocumentId}", documentId);
                        break;
                    }
                    catch (Exception ex) when (attempt < MaxAttempts)
                    {
                        _logger.LogWarning(
                            ex,
                            "Processing failed for document {DocumentId} on attempt {Attempt}/{MaxAttempts}. Retrying.",
                            documentId,
                            attempt,
                            MaxAttempts);

                        await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)), stoppingToken);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(
                            ex,
                            "Processing failed for document {DocumentId} after {MaxAttempts} attempts.",
                            documentId,
                            MaxAttempts);

                        await MarkDocumentAsFailedAsync(documentId, ex, stoppingToken);
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while processing queued document loop.");
                if (documentId != Guid.Empty)
                {
                    await MarkDocumentAsFailedAsync(documentId, ex, stoppingToken);
                }
            }
        }

        _logger.LogInformation("Document processing background worker stopped.");
    }

    private async Task MarkDocumentAsFailedAsync(
        Guid documentId,
        Exception exception,
        CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var document = await dbContext.Documents.FirstOrDefaultAsync(
                x => x.Id == documentId,
                cancellationToken);

            if (document is null)
            {
                return;
            }

            document.Status = DocumentStatus.Failed;
            document.ErrorMessage = exception.Message[..Math.Min(exception.Message.Length, 2000)];
            document.ProcessedAtUtc = DateTime.UtcNow;

            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not update failed state for document {DocumentId}", documentId);
        }
    }
}