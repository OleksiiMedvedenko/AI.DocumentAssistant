namespace AI.DocumentAssistant.Application.Abstractions.Documents
{
    public interface IDocumentProcessingService
    {
        Task ProcessAsync(Guid documentId, CancellationToken cancellationToken);
    }
}
