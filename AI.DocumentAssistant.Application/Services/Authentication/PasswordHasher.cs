using AI.DocumentAssistant.Application.Abstractions.Authentication;

namespace AI.DocumentAssistant.Application.Services.Authentication
{
    public sealed class PasswordHasher : IPasswordHasher
    {
        public string Hash(string password)
        {
            return BCrypt.Net.BCrypt.HashPassword(password);
        }

        public bool Verify(string password, string passwordHash)
        {
            return BCrypt.Net.BCrypt.Verify(password, passwordHash);
        }
    }
}
