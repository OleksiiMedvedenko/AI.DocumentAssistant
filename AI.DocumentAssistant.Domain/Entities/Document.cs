using AI.DocumentAssistant.Domain.Enums;

namespace AI.DocumentAssistant.Domain.Entities
{
    public sealed class Document
    {
        public Guid Id { get; set; }
        public Guid UserId { get; set; }
        public Guid? FolderId { get; set; }

        public string FileName { get; set; } = default!;
        public string OriginalFileName { get; set; } = default!;
        public string ContentType { get; set; } = default!;
        public long SizeInBytes { get; set; }
        public string StoragePath { get; set; } = default!;

        public DocumentStatus Status { get; set; }

        public string? ExtractedText { get; set; }
        public string? Summary { get; set; }

        public DocumentOrganizationMode OrganizationMode { get; set; }
        public bool SmartOrganizeRequested { get; set; }
        public bool AllowSystemFolderCreation { get; set; }

        public string? FolderClassificationStatus { get; set; }
        public decimal? FolderClassificationConfidence { get; set; }
        public string? FolderClassificationReason { get; set; }
        public bool WasFolderAutoAssigned { get; set; }

        public DateTime UploadedAtUtc { get; set; }
        public DateTime? ProcessedAtUtc { get; set; }
        public string? ErrorMessage { get; set; }

        public int ProcessingAttemptCount { get; set; }
        public DateTime? LastProcessingAttemptAtUtc { get; set; }

        public User User { get; set; } = default!;
        public DocumentFolder? Folder { get; set; }

        public ICollection<DocumentChunk> Chunks { get; set; } = new List<DocumentChunk>();
        public ICollection<ChatSession> ChatSessions { get; set; } = new List<ChatSession>();
        public ICollection<ExtractedData> Extractions { get; set; } = new List<ExtractedData>();
    }
}