using AI.DocumentAssistant.Domain.Entities;

namespace AI.DocumentAssistant.Abstraction.Abstractions.Authentication
{
    public interface IJwtTokenService
    {
        string GenerateAccessToken(User user);
        RefreshToken GenerateRefreshToken();
    }
}
