using AI.DocumentAssistant.Application.Abstractions.Usage;
using AI.DocumentAssistant.Application.Common.Exceptions;
using AI.DocumentAssistant.Application.Usage.Dtos;
using AI.DocumentAssistant.Domain.Entities;
using AI.DocumentAssistant.Domain.Enums;
using AI.DocumentAssistant.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AI.DocumentAssistant.Application.Usage.Services;

public sealed class UsageQuotaService : IUsageQuotaService
{
    private readonly AppDbContext _dbContext;

    public UsageQuotaService(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task EnsureWithinQuotaAsync(
        Guid userId,
        UsageType usageType,
        int quantity,
        CancellationToken cancellationToken)
    {
        var summary = await GetUserUsageSummaryAsync(userId, cancellationToken);

        if (summary.HasUnlimitedAiUsage)
        {
            return;
        }

        var metric = usageType switch
        {
            UsageType.ChatMessage => summary.ChatMessages,
            UsageType.UploadDocument => summary.DocumentUploads,
            UsageType.SummarizeDocument => summary.Summarizations,
            UsageType.ExtractDocument => summary.Extractions,
            UsageType.CompareDocument => summary.Comparisons,
            _ => throw new BadRequestException("Unsupported usage type.")
        };

        if (metric.Used + quantity > metric.Limit)
        {
            throw new QuotaExceededException(GetQuotaExceededMessage(usageType, metric.Limit));
        }
    }

    public Task<UserUsageSummaryDto> GetMyUsageSummaryAsync(Guid userId, CancellationToken cancellationToken)
        => GetUserUsageSummaryAsync(userId, cancellationToken);

    public async Task<UserUsageSummaryDto> GetUserUsageSummaryAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await _dbContext.Users
            .FirstOrDefaultAsync(x => x.Id == userId, cancellationToken);

        if (user is null)
        {
            throw new NotFoundException("User was not found.");
        }

        var now = DateTime.UtcNow;
        var monthStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var nextMonthStart = monthStart.AddMonths(1);

        var activeOverride = await _dbContext.UserQuotaOverrides
            .Where(x => x.UserId == userId
                        && x.ValidFromUtc <= now
                        && (x.ValidToUtc == null || x.ValidToUtc >= now))
            .OrderByDescending(x => x.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        var hasUnlimited = activeOverride?.HasUnlimitedAiUsageOverride ?? user.HasUnlimitedAiUsage;

        var records = await _dbContext.UserUsageRecords
            .Where(x => x.UserId == userId
                        && x.OccurredAtUtc >= monthStart
                        && x.OccurredAtUtc < nextMonthStart)
            .ToListAsync(cancellationToken);

        int Sum(UsageType type) => records.Where(x => x.UsageType == type).Sum(x => x.Quantity);

        int GetLimit(Func<User, int> baseSelector, Func<UserQuotaOverride, int?> overrideSelector)
            => activeOverride is not null && overrideSelector(activeOverride).HasValue
                ? overrideSelector(activeOverride)!.Value
                : baseSelector(user);

        return new UserUsageSummaryDto
        {
            UserId = user.Id,
            HasUnlimitedAiUsage = hasUnlimited,
            ChatMessages = BuildMetric(
                GetLimit(x => x.MonthlyChatMessageLimit, x => x.MonthlyChatMessageLimitOverride),
                Sum(UsageType.ChatMessage),
                hasUnlimited),
            DocumentUploads = BuildMetric(
                GetLimit(x => x.MonthlyDocumentUploadLimit, x => x.MonthlyDocumentUploadLimitOverride),
                Sum(UsageType.UploadDocument),
                hasUnlimited),
            Summarizations = BuildMetric(
                GetLimit(x => x.MonthlySummarizationLimit, x => x.MonthlySummarizationLimitOverride),
                Sum(UsageType.SummarizeDocument),
                hasUnlimited),
            Extractions = BuildMetric(
                GetLimit(x => x.MonthlyExtractionLimit, x => x.MonthlyExtractionLimitOverride),
                Sum(UsageType.ExtractDocument),
                hasUnlimited),
            Comparisons = BuildMetric(
                GetLimit(x => x.MonthlyComparisonLimit, x => x.MonthlyComparisonLimitOverride),
                Sum(UsageType.CompareDocument),
                hasUnlimited)
        };
    }

    private static UsageMetricDto BuildMetric(int limit, int used, bool hasUnlimited)
    {
        if (hasUnlimited)
        {
            return new UsageMetricDto
            {
                Limit = int.MaxValue,
                Used = used,
                Remaining = int.MaxValue
            };
        }

        return new UsageMetricDto
        {
            Limit = limit,
            Used = used,
            Remaining = Math.Max(0, limit - used)
        };
    }

    private static string GetQuotaExceededMessage(UsageType usageType, int limit)
    {
        return usageType switch
        {
            UsageType.ChatMessage => $"Monthly chat message limit reached ({limit}).",
            UsageType.UploadDocument => $"Monthly document upload limit reached ({limit}).",
            UsageType.SummarizeDocument => $"Monthly summarization limit reached ({limit}).",
            UsageType.ExtractDocument => $"Monthly extraction limit reached ({limit}).",
            UsageType.CompareDocument => $"Monthly comparison limit reached ({limit}).",
            _ => "Monthly limit has been exceeded for this feature."
        };
    }
}