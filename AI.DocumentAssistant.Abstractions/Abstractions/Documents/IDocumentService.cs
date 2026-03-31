namespace AI.DocumentAssistant.Abstraction.Abstractions.Documents
{
    public interface IDocumentService
    {
        Task<DocumentDto> UploadAsync(IFormFile file, CancellationToken cancellationToken);
        Task<IReadOnlyCollection<DocumentDto>> GetAllAsync(CancellationToken cancellationToken);
        Task<DocumentDetailsDto> GetByIdAsync(Guid documentId, CancellationToken cancellationToken);
        Task DeleteAsync(Guid documentId, CancellationToken cancellationToken);
        Task<SummarizeResultDto> SummarizeAsync(Guid documentId, CancellationToken cancellationToken);
    }
}
