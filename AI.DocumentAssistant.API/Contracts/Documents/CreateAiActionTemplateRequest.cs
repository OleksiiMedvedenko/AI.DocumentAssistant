namespace AI.DocumentAssistant.API.Contracts.Documents;

public sealed class CreateAiActionTemplateRequest
{
    public string Name { get; set; } = default!;
    public string? DocumentType { get; set; }
    public string Prompt { get; set; } = default!;
    public string? OutputFormat { get; set; }
}
