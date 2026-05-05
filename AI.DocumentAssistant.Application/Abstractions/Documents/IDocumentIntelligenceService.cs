using AI.DocumentAssistant.Application.Documents.Dtos;
using AI.DocumentAssistant.Domain.Entities;

namespace AI.DocumentAssistant.Application.Abstractions.Documents;

public interface IDocumentIntelligenceService
{
    Task<DocumentIntelligenceSnapshotDto> EnsureSnapshotAsync(Document document, CancellationToken cancellationToken);
    Task UpdateFolderProfileAsync(Guid userId, Guid folderId, CancellationToken cancellationToken);
    Task UpdateFolderProfilesAsync(Guid userId, IEnumerable<Guid> folderIds, CancellationToken cancellationToken);
    Task<Dictionary<Guid, decimal>> CalculateFolderSimilaritiesAsync(Document document, IReadOnlyCollection<DocumentFolder> folders, CancellationToken cancellationToken);
}
