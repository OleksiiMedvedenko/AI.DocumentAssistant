namespace AI.DocumentAssistant.Application.Documents.Dtos;

public sealed class AiActionTemplateDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = default!;
    public string? DocumentType { get; set; }
    public string Prompt { get; set; } = default!;
    public string OutputFormat { get; set; } = default!;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }
}
