using AI.DocumentAssistant.Domain.Enums;

namespace AI.DocumentAssistant.Application.Documents.Dtos
{
    public sealed class ChatMessageDto
    {
        public Guid Id { get; set; }
        public ChatRole Role { get; set; }
        public string Content { get; set; } = default!;
        public DateTime CreatedAtUtc { get; set; }
    }
}
