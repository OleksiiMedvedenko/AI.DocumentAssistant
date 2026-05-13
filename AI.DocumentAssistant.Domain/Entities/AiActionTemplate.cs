namespace AI.DocumentAssistant.Domain.Entities;

public sealed class AiActionTemplate
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid? FolderId { get; set; }
    public string Name { get; set; } = default!;
    public string? Description { get; set; }
    public string? DocumentType { get; set; }
    public string ActionType { get; set; } = "custom";
    public string Prompt { get; set; } = default!;
    public string OutputFormat { get; set; } = "markdown";
    public string? Language { get; set; }
    public bool SaveResult { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }

    public User User { get; set; } = default!;
    public DocumentFolder? Folder { get; set; }
    public ICollection<AiActionRun> Runs { get; set; } = new List<AiActionRun>();
}
