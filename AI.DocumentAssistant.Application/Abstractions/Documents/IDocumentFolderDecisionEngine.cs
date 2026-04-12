using AI.DocumentAssistant.Application.Documents.Dtos;
using AI.DocumentAssistant.Domain.Entities;

namespace AI.DocumentAssistant.Application.Abstractions.Documents
{
    public interface IDocumentFolderDecisionEngine
    {
        Task<DocumentFolderDecisionResultDto> DecideAsync(
            Document document,
            DocumentFolderAnalysisResultDto analysis,
            IReadOnlyCollection<DocumentFolder> existingFolders,
            CancellationToken cancellationToken);
    }
}