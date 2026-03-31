namespace AI.DocumentAssistant.Abstraction.Abstractions.Documents
{
    public interface IDocumentProcessingService
    {
        Task ProcessAsync(Guid documentId, CancellationToken cancellationToken);
    }
}
