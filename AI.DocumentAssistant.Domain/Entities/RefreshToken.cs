namespace AI.DocumentAssistant.Domain.Entities
{
    public sealed class RefreshToken
    {
        public Guid Id { get; set; }
        public Guid UserId { get; set; }
        public string Token { get; set; } = default!;
        public DateTime CreatedAtUtc { get; set; }
        public DateTime ExpiresAtUtc { get; set; }
        public bool IsRevoked { get; set; }

        public User User { get; set; } = default!;
    }
}
