namespace AI.DocumentAssistant.Application.Documents.Dtos
{
    public sealed class AskDocumentResultDto
    {
        public Guid ChatSessionId { get; set; }
        public string Answer { get; set; } = default!;
    }
}
