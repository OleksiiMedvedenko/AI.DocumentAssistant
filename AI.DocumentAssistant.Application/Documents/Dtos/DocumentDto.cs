using AI.DocumentAssistant.Domain.Enums;

namespace AI.DocumentAssistant.Application.Documents.Dtos
{
    public sealed class DocumentDto
    {
        public Guid Id { get; set; }
        public string OriginalFileName { get; set; } = default!;
        public string ContentType { get; set; } = default!;
        public long SizeInBytes { get; set; }
        public DocumentStatus Status { get; set; }
        public DateTime UploadedAtUtc { get; set; }
    }
}
