namespace AI.DocumentAssistant.Application.Documents.Dtos
{
    public sealed class SummarizeResultDto
    {
        public Guid DocumentId { get; set; }
        public string Summary { get; set; } = default!;
    }
}
