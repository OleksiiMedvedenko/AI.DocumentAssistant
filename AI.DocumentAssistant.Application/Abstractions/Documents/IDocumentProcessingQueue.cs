namespace AI.DocumentAssistant.Application.Abstractions.Documents;

public interface IDocumentProcessingQueue
{
    ValueTask EnqueueAsync(Guid documentId, CancellationToken cancellationToken);
    ValueTask<Guid> DequeueAsync(CancellationToken cancellationToken);
}