namespace AI.DocumentAssistant.Domain.Entities
{
    public sealed class ExtractedData
    {
        public Guid Id { get; set; }
        public Guid DocumentId { get; set; }
        public string ExtractionType { get; set; } = default!;
        public string JsonResult { get; set; } = default!;
        public DateTime CreatedAtUtc { get; set; }

        public Document Document { get; set; } = default!;
    }
}
