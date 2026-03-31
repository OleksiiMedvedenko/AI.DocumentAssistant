namespace AI.DocumentAssistant.Application.Auth.Dtos
{
    public sealed class AuthResultDto
    {
        public string AccessToken { get; set; } = default!;
        public string RefreshToken { get; set; } = default!;
        public int ExpiresIn { get; set; }
    }
}
