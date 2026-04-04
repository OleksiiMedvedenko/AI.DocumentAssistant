namespace AI.DocumentAssistant.Application.Documents.Dtos;

public sealed class ExtractedDataDto
{
    public Guid Id { get; set; }
    public Guid DocumentId { get; set; }
    public string ExtractionType { get; set; } = default!;
    public IReadOnlyList<string> Fields { get; set; } = [];
    public string JsonResult { get; set; } = default!;
    public DateTime CreatedAtUtc { get; set; }
}