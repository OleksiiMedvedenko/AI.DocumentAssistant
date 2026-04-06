namespace AI.DocumentAssistant.Domain.Entities;

public sealed class UserQuotaOverride
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public bool? HasUnlimitedAiUsageOverride { get; set; }

    public int? MonthlyChatMessageLimitOverride { get; set; }

    public int? MonthlyDocumentUploadLimitOverride { get; set; }

    public int? MonthlySummarizationLimitOverride { get; set; }

    public int? MonthlyExtractionLimitOverride { get; set; }

    public int? MonthlyComparisonLimitOverride { get; set; }

    public string? Reason { get; set; }

    public DateTime ValidFromUtc { get; set; }

    public DateTime? ValidToUtc { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public User User { get; set; } = default!;
}