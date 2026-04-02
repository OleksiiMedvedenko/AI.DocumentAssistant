using AI.DocumentAssistant.Domain.Enums;

namespace AI.DocumentAssistant.Application.Documents.Dtos;

public sealed class DocumentStatusDto
{
    public Guid Id { get; set; }
    public string OriginalFileName { get; set; } = default!;
    public DocumentStatus Status { get; set; }
    public DateTime UploadedAtUtc { get; set; }
    public DateTime? ProcessedAtUtc { get; set; }
    public string? ErrorMessage { get; set; }
}