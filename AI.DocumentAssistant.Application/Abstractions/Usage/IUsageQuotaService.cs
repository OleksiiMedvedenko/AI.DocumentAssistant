using AI.DocumentAssistant.Application.Usage.Dtos;
using AI.DocumentAssistant.Domain.Enums;

namespace AI.DocumentAssistant.Application.Abstractions.Usage;

public interface IUsageQuotaService
{
    Task EnsureWithinQuotaAsync(Guid userId, UsageType usageType, int quantity, CancellationToken cancellationToken);
    Task<UserUsageSummaryDto> GetMyUsageSummaryAsync(Guid userId, CancellationToken cancellationToken);
    Task<UserUsageSummaryDto> GetUserUsageSummaryAsync(Guid userId, CancellationToken cancellationToken);
}