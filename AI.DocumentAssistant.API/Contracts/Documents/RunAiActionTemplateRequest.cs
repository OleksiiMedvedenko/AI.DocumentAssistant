namespace AI.DocumentAssistant.API.Contracts.Documents;

public sealed class RunAiActionTemplateRequest
{
    public string? Language { get; set; }
    public string? OutputFormat { get; set; }
    public bool? SaveResult { get; set; }
}
