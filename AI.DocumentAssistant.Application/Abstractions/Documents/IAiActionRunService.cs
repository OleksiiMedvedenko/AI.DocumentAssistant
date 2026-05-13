using AI.DocumentAssistant.Application.Documents.Dtos;

namespace AI.DocumentAssistant.Application.Abstractions.Documents;

public interface IAiActionRunService
{
    Task<AiActionRunDto> RunTemplateAsync(
        Guid documentId,
        Guid templateId,
        RunAiActionTemplateRequestDto request,
        CancellationToken cancellationToken);

    Task<List<AiActionRunDto>> GetDocumentRunsAsync(Guid documentId, CancellationToken cancellationToken);
    Task<AiActionRunDto> GetByIdAsync(Guid runId, CancellationToken cancellationToken);
    Task<AiActionRunDownloadDto> OpenDownloadAsync(Guid runId, CancellationToken cancellationToken);
}
