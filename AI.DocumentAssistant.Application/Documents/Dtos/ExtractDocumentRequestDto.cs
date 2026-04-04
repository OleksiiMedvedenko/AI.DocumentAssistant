namespace AI.DocumentAssistant.Application.Documents.Dtos;

public sealed class ExtractDocumentRequestDto
{
    public string? ExtractionType { get; set; }
    public IReadOnlyList<string> Fields { get; set; } = [];
    public string? Language { get; set; }
}