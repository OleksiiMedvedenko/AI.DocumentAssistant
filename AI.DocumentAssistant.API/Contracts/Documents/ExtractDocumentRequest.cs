namespace AI.DocumentAssistant.API.Contracts.Documents;

public sealed class ExtractDocumentRequest
{
    public string? ExtractionType { get; set; }
    public IReadOnlyList<string> Fields { get; set; } = [];
    public string? Language { get; set; }
}