namespace AI.DocumentAssistant.API.Contracts.Auth;

public sealed class RefreshTokenRequest
{
    public string RefreshToken { get; set; } = default!;
}