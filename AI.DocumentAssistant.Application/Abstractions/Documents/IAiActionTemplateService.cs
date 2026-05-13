using AI.DocumentAssistant.Application.Documents.Dtos;

namespace AI.DocumentAssistant.Application.Abstractions.Documents;

public interface IAiActionTemplateService
{
    Task<List<AiActionTemplateDto>> GetAllAsync(string? documentType, Guid? folderId, CancellationToken cancellationToken);
    Task<AiActionTemplateDto> CreateAsync(CreateAiActionTemplateRequestDto request, CancellationToken cancellationToken);
    Task<AiActionTemplateDto> UpdateAsync(Guid templateId, CreateAiActionTemplateRequestDto request, CancellationToken cancellationToken);
    Task DeleteAsync(Guid templateId, CancellationToken cancellationToken);
}
