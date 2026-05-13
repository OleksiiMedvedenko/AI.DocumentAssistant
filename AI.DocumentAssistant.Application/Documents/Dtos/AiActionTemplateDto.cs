namespace AI.DocumentAssistant.Application.Documents.Dtos;

public sealed class AiActionTemplateDto
{
    public Guid Id { get; set; }
    public Guid? FolderId { get; set; }
    public string Name { get; set; } = default!;
    public string? Description { get; set; }
    public string? DocumentType { get; set; }
    public string ActionType { get; set; } = default!;
    public string Prompt { get; set; } = default!;
    public string OutputFormat { get; set; } = default!;
    public string? Language { get; set; }
    public bool SaveResult { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }
}
