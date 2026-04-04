namespace AI.DocumentAssistant.Application.Documents.Dtos;

public sealed class ExtractDocumentResultDto
{
    public Guid DocumentId { get; set; }
    public string ExtractionType { get; set; } = default!;
    public string RawJson { get; set; } = default!;
}