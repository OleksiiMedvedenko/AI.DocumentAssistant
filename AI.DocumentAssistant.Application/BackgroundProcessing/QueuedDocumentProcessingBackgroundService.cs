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
                        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                        var processor = scope.ServiceProvider.GetRequiredService<IDocumentProcessingService>();

                        await UpdateAttemptMetadataAsync(dbContext, documentId, attempt, stoppingToken);

                        _logger.LogInformation(
                            "Processing document {DocumentId}, attempt {Attempt}/{MaxAttempts}",
                            documentId,
                            attempt,
                            MaxAttempts);

                        await processor.ProcessAsync(documentId, stoppingToken);

                        var finalStatus = await GetDocumentStatusAsync(dbContext, documentId, stoppingToken);

                        if (finalStatus == DocumentStatus.Ready)
                        {
                            _logger.LogInformation("Finished processing document {DocumentId}", documentId);
                            break;
                        }

                        if (finalStatus == DocumentStatus.Failed)
                        {
                            if (attempt < MaxAttempts)
                            {
                                _logger.LogWarning(
                                    "Document {DocumentId} finished with Failed status on attempt {Attempt}/{MaxAttempts}. Retrying.",
                                    documentId,
                                    attempt,
                                    MaxAttempts);

                                await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)), stoppingToken);
                                continue;
                            }

                            _logger.LogError(
                                "Document {DocumentId} finished with Failed status after {MaxAttempts} attempts.",
                                documentId,
                                MaxAttempts);

                            break;
                        }

                        if (attempt < MaxAttempts)
                        {
                            _logger.LogWarning(
                                "Document {DocumentId} ended in unexpected status {Status} on attempt {Attempt}/{MaxAttempts}. Retrying.",
                                documentId,
                                finalStatus,
                                attempt,
                                MaxAttempts);

                            await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)), stoppingToken);
                            continue;
                        }

                        await MarkDocumentAsFailedAsync(
                            documentId,
                            $"Document processing ended in unexpected status '{finalStatus}'.",
                            stoppingToken);

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

                        await MarkDocumentAsFailedAsync(documentId, ex.Message, stoppingToken);
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
                    await MarkDocumentAsFailedAsync(documentId, ex.Message, stoppingToken);
                }
            }
        }

        _logger.LogInformation("Document processing background worker stopped.");
    }

    private static async Task UpdateAttemptMetadataAsync(
        AppDbContext dbContext,
        Guid documentId,
        int attempt,
        CancellationToken cancellationToken)
    {
        var document = await dbContext.Documents.FirstOrDefaultAsync(
            x => x.Id == documentId,
            cancellationToken);

        if (document is null)
        {
            return;
        }

        document.ProcessingAttemptCount = attempt;
        document.LastProcessingAttemptAtUtc = DateTime.UtcNow;
        document.Status = DocumentStatus.Processing;
        document.ErrorMessage = null;

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static async Task<DocumentStatus?> GetDocumentStatusAsync(
        AppDbContext dbContext,
        Guid documentId,
        CancellationToken cancellationToken)
    {
        return await dbContext.Documents
            .Where(x => x.Id == documentId)
            .Select(x => (DocumentStatus?)x.Status)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private async Task MarkDocumentAsFailedAsync(
        Guid documentId,
        string errorMessage,
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
            document.ErrorMessage = errorMessage[..Math.Min(errorMessage.Length, 2000)];
            document.ProcessedAtUtc = DateTime.UtcNow;
            document.LastProcessingAttemptAtUtc = DateTime.UtcNow;

            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not update failed state for document {DocumentId}", documentId);
        }
    }
}