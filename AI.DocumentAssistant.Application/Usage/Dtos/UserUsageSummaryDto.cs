namespace AI.DocumentAssistant.Application.Usage.Dtos
{
    public sealed class UserUsageSummaryDto
    {
        public Guid UserId { get; set; }

        public bool HasUnlimitedAiUsage { get; set; }

        public UsageMetricDto ChatMessages { get; set; } = new();
        public UsageMetricDto DocumentUploads { get; set; } = new();
        public UsageMetricDto Summarizations { get; set; } = new();
        public UsageMetricDto Extractions { get; set; } = new();
        public UsageMetricDto Comparisons { get; set; } = new();
    }
}
