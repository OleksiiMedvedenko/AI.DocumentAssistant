namespace AI.DocumentAssistant.Domain.Entities
{
    public sealed class ChatSession
    {
        public Guid Id { get; set; }

        public Guid? DocumentId { get; set; }
        public Guid? FolderId { get; set; }

        public Guid UserId { get; set; }
        public DateTime CreatedAtUtc { get; set; }

        public Document? Document { get; set; }
        public DocumentFolder? Folder { get; set; }
        public User User { get; set; } = default!;

        public ICollection<ChatMessage> Messages { get; set; } = new List<ChatMessage>();
    }
}