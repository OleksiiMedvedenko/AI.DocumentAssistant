using AI.DocumentAssistant.Domain.Enums;

namespace AI.DocumentAssistant.Domain.Entities
{
    public sealed class Document
    {
        public Guid Id { get; set; }
        public Guid UserId { get; set; }
        public string FileName { get; set; } = default!;
        public string OriginalFileName { get; set; } = default!;
        public string ContentType { get; set; } = default!;
        public long SizeInBytes { get; set; }
        public string StoragePath { get; set; } = default!;
        public DocumentStatus Status { get; set; }
        public string? ExtractedText { get; set; }
        public string? Summary { get; set; }
        public DateTime UploadedAtUtc { get; set; }
        public DateTime? ProcessedAtUtc { get; set; }
        public string? ErrorMessage { get; set; }

        public User User { get; set; } = default!;
        public ICollection<DocumentChunk> Chunks { get; set; } = new List<DocumentChunk>();
        public ICollection<ChatSession> ChatSessions { get; set; } = new List<ChatSession>();
        public ICollection<ExtractedData> Extractions { get; set; } = new List<ExtractedData>();
    }
}
