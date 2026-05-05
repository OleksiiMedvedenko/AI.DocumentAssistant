using AI.DocumentAssistant.Domain.Enums;

namespace AI.DocumentAssistant.Application.Documents.Dtos;

public sealed class RelatedDocumentDto
{
    public Guid DocumentId { get; set; }
    public string OriginalFileName { get; set; } = default!;
    public Guid? FolderId { get; set; }
    public string? FolderName { get; set; }
    public DocumentStatus Status { get; set; }
    public decimal Score { get; set; }
    public string Reason { get; set; } = default!;
    public DateTime UploadedAtUtc { get; set; }
}
