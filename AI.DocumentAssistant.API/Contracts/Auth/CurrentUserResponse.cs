namespace AI.DocumentAssistant.API.Contracts.Auth;

public sealed class CurrentUserResponse
{
    public Guid Id { get; set; }

    public string Email { get; set; } = default!;

    public string? DisplayName { get; set; }

    public string Role { get; set; } = default!;

    public bool IsActive { get; set; }

    public string AuthProvider { get; set; } = default!;

    public DateTime CreatedAtUtc { get; set; }

    public CurrentUserUsageResponse Usage { get; set; } = new();
}

public sealed class CurrentUserUsageResponse
{
    public bool HasUnlimitedAiUsage { get; set; }

    public UsageMetricResponse ChatMessages { get; set; } = new();
    public UsageMetricResponse DocumentUploads { get; set; } = new();
    public UsageMetricResponse Summarizations { get; set; } = new();
    public UsageMetricResponse Extractions { get; set; } = new();
    public UsageMetricResponse Comparisons { get; set; } = new();
}

public sealed class UsageMetricResponse
{
    public int Limit { get; set; }
    public int Used { get; set; }
    public int Remaining { get; set; }
}