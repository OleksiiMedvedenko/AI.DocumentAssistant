using System.Reflection.Metadata;

namespace AI.DocumentAssistant.Domain.Entities
{
    public sealed class User
    {
        public Guid Id { get; set; }
        public string Email { get; set; } = default!;
        public string PasswordHash { get; set; } = default!;
        public DateTime CreatedAtUtc { get; set; }

        public ICollection<Document> Documents { get; set; } = new List<Document>();
        public ICollection<RefreshToken> RefreshTokens { get; set; } = new List<RefreshToken>();
    }
}
