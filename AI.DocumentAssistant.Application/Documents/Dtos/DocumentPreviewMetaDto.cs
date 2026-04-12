namespace AI.DocumentAssistant.Application.Documents.Dtos
{
    public sealed class DocumentPreviewMetaDto
    {
        public Guid DocumentId { get; set; }
        public string FileName { get; set; } = default!;
        public string ContentType { get; set; } = default!;
        public string PreviewKind { get; set; } = default!;
        public bool CanInlinePreview { get; set; }
        public string? Message { get; set; }
    }
}
