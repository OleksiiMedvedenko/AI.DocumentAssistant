namespace AI.DocumentAssistant.Application.Auth.Dtos;

public sealed class CurrentUserDto
{
    public Guid Id { get; set; }
    public string Email { get; set; } = default!;
    public DateTime CreatedAtUtc { get; set; }
}