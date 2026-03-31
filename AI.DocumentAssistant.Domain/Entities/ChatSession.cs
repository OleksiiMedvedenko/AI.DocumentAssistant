namespace AI.DocumentAssistant.Domain.Entities
{
    public sealed class ChatSession
    {
        public Guid Id { get; set; }
        public Guid DocumentId { get; set; }
        public Guid UserId { get; set; }
        public DateTime CreatedAtUtc { get; set; }

        public Document Document { get; set; } = default!;
        public User User { get; set; } = default!;
        public ICollection<ChatMessage> Messages { get; set; } = new List<ChatMessage>();
    }
}
