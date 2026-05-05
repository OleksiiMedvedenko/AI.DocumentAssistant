namespace AI.DocumentAssistant.Application.Documents.Dtos;

public sealed class CreateAiActionTemplateRequestDto
{
    public string Name { get; set; } = default!;
    public string? DocumentType { get; set; }
    public string Prompt { get; set; } = default!;
    public string? OutputFormat { get; set; }
}
