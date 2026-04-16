namespace AI.DocumentAssistant.Application.Auth.Models
{
    public sealed class EmailConfirmationOptions
    {
        public const string SectionName = "EmailConfirmation";
        public int TokenLifetimeHours { get; set; } = 24;
        public int ResendCooldownSeconds { get; set; } = 60;
        public string[] AllowedFrontendHosts { get; set; } = Array.Empty<string>();
    }
}
