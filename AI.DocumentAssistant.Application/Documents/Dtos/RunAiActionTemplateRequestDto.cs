namespace AI.DocumentAssistant.Application.Documents.Dtos;

public sealed class RunAiActionTemplateRequestDto
{
    public string? Language { get; set; }
    public string? OutputFormat { get; set; }
    public bool? SaveResult { get; set; }
}
