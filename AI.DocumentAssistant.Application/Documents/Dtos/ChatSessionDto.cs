namespace AI.DocumentAssistant.Application.Documents.Dtos;

public sealed class ChatSessionDto
{
    public Guid Id { get; set; }
    public Guid DocumentId { get; set; }
    public string Title { get; set; } = default!;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime LastMessageAtUtc { get; set; }
    public int MessageCount { get; set; }
}