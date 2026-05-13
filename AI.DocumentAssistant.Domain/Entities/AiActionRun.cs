namespace AI.DocumentAssistant.Domain.Entities;

public sealed class AiActionRun
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid DocumentId { get; set; }
    public Guid? TemplateId { get; set; }

    public string TemplateName { get; set; } = default!;
    public string ActionType { get; set; } = "custom";
    public string OutputFormat { get; set; } = "markdown";
    public string? Language { get; set; }
    public string Prompt { get; set; } = default!;

    public string Status { get; set; } = "pending";
    public string? ResultText { get; set; }
    public string? ResultJson { get; set; }
    public string? ResultFilePath { get; set; }
    public string? ResultFileName { get; set; }
    public string? ResultContentType { get; set; }
    public string? ErrorMessage { get; set; }

    public DateTime CreatedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }

    public User User { get; set; } = default!;
    public Document Document { get; set; } = default!;
    public AiActionTemplate? Template { get; set; }
}
