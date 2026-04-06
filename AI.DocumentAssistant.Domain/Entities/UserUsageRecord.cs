using AI.DocumentAssistant.Domain.Enums;

namespace AI.DocumentAssistant.Domain.Entities;

public sealed class UserUsageRecord
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public UsageType UsageType { get; set; }

    public int Quantity { get; set; } = 1;

    public int? InputTokens { get; set; }

    public int? OutputTokens { get; set; }

    public decimal? EstimatedCost { get; set; }

    public string? Model { get; set; }

    public string? ReferenceId { get; set; }

    public DateTime OccurredAtUtc { get; set; }

    public User User { get; set; } = default!;
}