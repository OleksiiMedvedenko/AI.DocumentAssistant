namespace AI.DocumentAssistant.Domain.Entities;

public sealed class DocumentIntelligenceSnapshot
{
    public Guid Id { get; set; }
    public Guid DocumentId { get; set; }
    public Guid UserId { get; set; }

    public string DocumentType { get; set; } = "other";
    public string BusinessDomain { get; set; } = "unknown";
    public string Topic { get; set; } = "unknown";
    public int? Year { get; set; }
    public int? Month { get; set; }
    public DateTime? DueDate { get; set; }
    public DateTime? EffectiveDate { get; set; }
    public decimal Confidence { get; set; }
    public string Keywords { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }

    public Document Document { get; set; } = default!;
    public User User { get; set; } = default!;
}
