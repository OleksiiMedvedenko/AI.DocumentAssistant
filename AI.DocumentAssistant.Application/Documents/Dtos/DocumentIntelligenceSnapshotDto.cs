namespace AI.DocumentAssistant.Application.Documents.Dtos;

public sealed class DocumentIntelligenceSnapshotDto
{
    public Guid DocumentId { get; set; }
    public string DocumentType { get; set; } = "other";
    public string BusinessDomain { get; set; } = "unknown";
    public string Topic { get; set; } = "unknown";
    public int? Year { get; set; }
    public int? Month { get; set; }
    public DateTime? DueDate { get; set; }
    public DateTime? EffectiveDate { get; set; }
    public decimal Confidence { get; set; }
    public IReadOnlyList<string> Keywords { get; set; } = [];
    public string Reason { get; set; } = string.Empty;
    public DateTime UpdatedAtUtc { get; set; }
}
