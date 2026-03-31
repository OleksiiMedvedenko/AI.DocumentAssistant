namespace AI.DocumentAssistant.API.Contracts.Auth
{
    public sealed class AuthResponse
    {
        public string AccessToken { get; set; } = default!;
        public string RefreshToken { get; set; } = default!;
        public int ExpiresIn { get; set; }
    }
}
