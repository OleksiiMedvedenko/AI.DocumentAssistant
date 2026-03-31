using AI.DocumentAssistant.Domain.Enums;

namespace AI.DocumentAssistant.Application.Documents.Dtos
{
    public sealed class DocumentDetailsDto
    {
        public Guid Id { get; set; }
        public string OriginalFileName { get; set; } = default!;
        public string ContentType { get; set; } = default!;
        public long SizeInBytes { get; set; }
        public DocumentStatus Status { get; set; }
        public string? Summary { get; set; }
        public DateTime UploadedAtUtc { get; set; }
        public DateTime? ProcessedAtUtc { get; set; }
        public string? ErrorMessage { get; set; }
    }
}
