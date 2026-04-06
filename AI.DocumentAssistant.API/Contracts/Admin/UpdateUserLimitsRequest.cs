namespace AI.DocumentAssistant.API.Contracts.Admin;

public sealed class UpdateUserLimitsRequest
{
    public int? MonthlyChatMessageLimit { get; set; }
    public int? MonthlyDocumentUploadLimit { get; set; }
    public int? MonthlySummarizationLimit { get; set; }
    public int? MonthlyExtractionLimit { get; set; }
    public int? MonthlyComparisonLimit { get; set; }

    public bool? HasUnlimitedAiUsage { get; set; }

    public string? Reason { get; set; }

    public DateTime? ValidToUtc { get; set; }
}