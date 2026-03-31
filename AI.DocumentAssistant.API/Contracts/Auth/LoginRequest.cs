namespace AI.DocumentAssistant.API.Contracts.Auth
{
    public sealed class LoginRequest
    {
        public string Email { get; set; } = default!;
        public string Password { get; set; } = default!;
    }
}
