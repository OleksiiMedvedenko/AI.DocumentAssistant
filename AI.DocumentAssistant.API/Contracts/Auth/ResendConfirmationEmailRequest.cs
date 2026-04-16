namespace AI.DocumentAssistant.API.Contracts.Auth
{
    public class ResendConfirmationEmailRequest
    {
        public string Email { get; set; } = default!;
        public string ConfirmationUrl { get; set; } = default!;
        public string Language { get; set; } = "en";
    }
}
