using AI.DocumentAssistant.Application.Documents.Dtos;
using Microsoft.AspNetCore.Http;

namespace AI.DocumentAssistant.Application.Abstractions.Documents;

public interface IDocumentService
{
    Task<DocumentDto> UploadAsync(IFormFile file, CancellationToken cancellationToken);
    Task<IReadOnlyList<DocumentDto>> GetAllAsync(CancellationToken cancellationToken);
    Task<DocumentDetailsDto> GetByIdAsync(Guid documentId, CancellationToken cancellationToken);
    Task DeleteAsync(Guid documentId, CancellationToken cancellationToken);
    Task<SummarizeResultDto> SummarizeAsync(Guid documentId, CancellationToken cancellationToken);
}