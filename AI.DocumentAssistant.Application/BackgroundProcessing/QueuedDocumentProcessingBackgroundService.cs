using AI.DocumentAssistant.Application.Abstractions.Documents;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AI.DocumentAssistant.Infrastructure.BackgroundProcessing;

public sealed class QueuedDocumentProcessingBackgroundService : BackgroundService
{
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
            try
            {
                var documentId = await _queue.DequeueAsync(stoppingToken);

                using var scope = _scopeFactory.CreateScope();
                var processor = scope.ServiceProvider.GetRequiredService<IDocumentProcessingService>();

                _logger.LogInformation("Processing document {DocumentId}", documentId);

                await processor.ProcessAsync(documentId, stoppingToken);

                _logger.LogInformation("Finished processing document {DocumentId}", documentId);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while processing queued document.");
            }
        }

        _logger.LogInformation("Document processing background worker stopped.");
    }
}