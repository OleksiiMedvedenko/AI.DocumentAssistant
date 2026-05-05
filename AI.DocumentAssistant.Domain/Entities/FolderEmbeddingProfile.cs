namespace AI.DocumentAssistant.Domain.Entities;

public sealed class FolderEmbeddingProfile
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid FolderId { get; set; }

    public string SourceText { get; set; } = string.Empty;
    public string EmbeddingJson { get; set; } = "[]";
    public int DocumentCount { get; set; }
    public DateTime UpdatedAtUtc { get; set; }

    public User User { get; set; } = default!;
    public DocumentFolder Folder { get; set; } = default!;
}
