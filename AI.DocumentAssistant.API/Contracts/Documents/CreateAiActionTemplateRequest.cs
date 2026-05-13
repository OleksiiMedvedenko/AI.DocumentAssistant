namespace AI.DocumentAssistant.API.Contracts.Documents;

public sealed class CreateAiActionTemplateRequest
{
    public Guid? FolderId { get; set; }
    public string Name { get; set; } = default!;
    public string? Description { get; set; }
    public string? DocumentType { get; set; }
    public string? ActionType { get; set; }
    public string Prompt { get; set; } = default!;
    public string? OutputFormat { get; set; }
    public string? Language { get; set; }
    public bool? SaveResult { get; set; }
}
