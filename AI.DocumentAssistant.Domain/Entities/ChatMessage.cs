using AI.DocumentAssistant.Domain.Enums;

namespace AI.DocumentAssistant.Domain.Entities
{
    public sealed class ChatMessage
    {
        public Guid Id { get; set; }
        public Guid ChatSessionId { get; set; }
        public ChatRole Role { get; set; }
        public string Content { get; set; } = default!;
        public DateTime CreatedAtUtc { get; set; }

        public ChatSession ChatSession { get; set; } = default!;
    }
}
