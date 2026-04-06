using AI.DocumentAssistant.Application.Usage.Dtos;
using AI.DocumentAssistant.Domain.Enums;

namespace AI.DocumentAssistant.Application.Auth.Dtos;

public sealed class CurrentUserDto
{
    public Guid Id { get; set; }

    public string Email { get; set; } = default!;

    public string? DisplayName { get; set; }

    public UserRole Role { get; set; }

    public bool IsActive { get; set; }

    public AuthProvider AuthProvider { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public UserUsageSummaryDto UsageSummary { get; set; } = new();
}