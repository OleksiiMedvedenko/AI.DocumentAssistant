namespace AI.DocumentAssistant.Domain.Entities;

public sealed class UserFolderRule
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid FolderId { get; set; }

    public string Pattern { get; set; } = default!;
    public string? DocumentType { get; set; }
    public string? Topic { get; set; }
    public decimal Weight { get; set; } = 1m;
    public bool CreatedFromCorrection { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? LastMatchedAtUtc { get; set; }

    public User User { get; set; } = default!;
    public DocumentFolder Folder { get; set; } = default!;
}
