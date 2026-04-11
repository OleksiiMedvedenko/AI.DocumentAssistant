using AI.DocumentAssistant.Application.Documents.Dtos;

namespace AI.DocumentAssistant.Application.Abstractions.Documents
{
    public interface IDocumentFolderService
    {
        Task<List<DocumentFolderDto>> GetTreeAsync(CancellationToken cancellationToken);
        Task<DocumentFolderDto> CreateAsync(CreateDocumentFolderRequestDto request, CancellationToken cancellationToken);
        Task<DocumentFolderDto> UpdateAsync(Guid folderId, UpdateDocumentFolderRequestDto request, CancellationToken cancellationToken);
        Task DeleteAsync(Guid folderId, CancellationToken cancellationToken);
    }
}
