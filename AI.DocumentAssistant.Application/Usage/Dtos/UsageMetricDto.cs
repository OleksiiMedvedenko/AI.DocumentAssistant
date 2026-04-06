namespace AI.DocumentAssistant.Application.Usage.Dtos
{
    public sealed class UsageMetricDto
    {
        public int Limit { get; set; }
        public int Used { get; set; }
        public int Remaining { get; set; }
    }
}
