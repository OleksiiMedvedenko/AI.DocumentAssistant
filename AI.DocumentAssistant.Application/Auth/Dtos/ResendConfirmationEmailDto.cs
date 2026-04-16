namespace AI.DocumentAssistant.Application.Auth.Dtos
{
    public sealed class ResendConfirmationEmailDto
    {
        public string Email { get; set; } = default!;
        public string ConfirmationUrl { get; set; } = default!;
        public string Language { get; set; } = "en";
    }
}
