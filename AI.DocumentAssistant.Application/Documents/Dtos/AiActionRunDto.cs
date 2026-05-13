namespace AI.DocumentAssistant.Application.Documents.Dtos;

public sealed class AiActionRunDto
{
    public Guid Id { get; set; }
    public Guid DocumentId { get; set; }
    public Guid? TemplateId { get; set; }
    public string TemplateName { get; set; } = default!;
    public string ActionType { get; set; } = default!;
    public string OutputFormat { get; set; } = default!;
    public string? Language { get; set; }
    public string Status { get; set; } = default!;
    public string? ResultText { get; set; }
    public string? ResultJson { get; set; }
    public bool HasFile { get; set; }
    public string? ResultFileName { get; set; }
    public string? ResultContentType { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
}
