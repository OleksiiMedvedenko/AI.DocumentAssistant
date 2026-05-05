namespace AI.DocumentAssistant.Domain.Entities;

public sealed class AiActionTemplate
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string Name { get; set; } = default!;
    public string? DocumentType { get; set; }
    public string Prompt { get; set; } = default!;
    public string OutputFormat { get; set; } = "text";
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }

    public User User { get; set; } = default!;
}
