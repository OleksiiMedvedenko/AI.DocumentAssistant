namespace AI.DocumentAssistant.API.Contracts.Documents
{
    public sealed class AskDocumentRequest
    {
        public Guid? ChatSessionId { get; set; }
        public string Message { get; set; } = default!;
    }
}
