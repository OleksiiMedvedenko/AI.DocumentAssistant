using AI.DocumentAssistant.Application.Abstractions.Usage;
using AI.DocumentAssistant.Domain.Entities;
using AI.DocumentAssistant.Domain.Enums;
using AI.DocumentAssistant.Infrastructure.Persistence;

namespace AI.DocumentAssistant.Application.Usage.Services;

public sealed class UsageTrackingService : IUsageTrackingService
{
    private readonly AppDbContext _dbContext;

    public UsageTrackingService(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task TrackAsync(
        Guid userId,
        UsageType usageType,
        int quantity,
        CancellationToken cancellationToken,
        int? inputTokens = null,
        int? outputTokens = null,
        decimal? estimatedCost = null,
        string? model = null,
        string? referenceId = null)
    {
        var record = new UserUsageRecord
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            UsageType = usageType,
            Quantity = quantity,
            InputTokens = inputTokens,
            OutputTokens = outputTokens,
            EstimatedCost = estimatedCost,
            Model = model,
            ReferenceId = referenceId,
            OccurredAtUtc = DateTime.UtcNow
        };

        _dbContext.UserUsageRecords.Add(record);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}