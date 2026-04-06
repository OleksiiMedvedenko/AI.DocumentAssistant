using AI.DocumentAssistant.Domain.Enums;

namespace AI.DocumentAssistant.Application.Abstractions.Usage;

public interface IUsageTrackingService
{
    Task TrackAsync(
        Guid userId,
        UsageType usageType,
        int quantity,
        CancellationToken cancellationToken,
        int? inputTokens = null,
        int? outputTokens = null,
        decimal? estimatedCost = null,
        string? model = null,
        string? referenceId = null);
}