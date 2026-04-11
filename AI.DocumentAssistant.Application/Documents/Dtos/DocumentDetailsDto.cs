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

        public Guid? FolderId { get; set; }
        public string? FolderName { get; set; }
        public string? FolderNamePl { get; set; }
        public string? FolderNameEn { get; set; }
        public string? FolderNameUa { get; set; }

        public string? FolderClassificationStatus { get; set; }
        public decimal? FolderClassificationConfidence { get; set; }
        public bool WasFolderAutoAssigned { get; set; }
    }
}
