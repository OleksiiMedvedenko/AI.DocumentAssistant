namespace AI.DocumentAssistant.API.Contracts.Auth;

public sealed class CurrentUserResponse
{
    public Guid Id { get; set; }
    public string Email { get; set; } = default!;
    public DateTime CreatedAtUtc { get; set; }
}