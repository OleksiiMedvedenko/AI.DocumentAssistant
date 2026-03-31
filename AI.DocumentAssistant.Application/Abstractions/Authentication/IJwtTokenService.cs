using AI.DocumentAssistant.Domain.Entities;

namespace AI.DocumentAssistant.Application.Abstractions.Authentication
{
    public interface IJwtTokenService
    {
        string GenerateAccessToken(User user);
        RefreshToken GenerateRefreshToken();
    }
}
