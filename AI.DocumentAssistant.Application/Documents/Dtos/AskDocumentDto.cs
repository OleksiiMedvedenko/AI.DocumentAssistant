namespace AI.DocumentAssistant.Application.Documents.Dtos
{
    public sealed class AskDocumentDto
    {
        public Guid? ChatSessionId { get; set; }
        public string Message { get; set; } = default!;
    }
}
