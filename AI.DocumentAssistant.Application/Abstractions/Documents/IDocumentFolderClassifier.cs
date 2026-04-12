using AI.DocumentAssistant.Application.Documents.Dtos;
using AI.DocumentAssistant.Domain.Entities;

namespace AI.DocumentAssistant.Application.Abstractions.Documents
{
    public interface IDocumentFolderClassifier
    {
        Task<DocumentFolderAnalysisResultDto> AnalyzeAsync(
            Document document,
            IReadOnlyCollection<DocumentFolder> existingFolders,
            CancellationToken cancellationToken);
    }
}