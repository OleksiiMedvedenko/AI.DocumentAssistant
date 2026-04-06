using AI.DocumentAssistant.API.Contracts.Admin;
using AI.DocumentAssistant.Application.Common.Exceptions;
using AI.DocumentAssistant.Domain.Entities;
using AI.DocumentAssistant.Domain.Enums;
using AI.DocumentAssistant.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AI.DocumentAssistant.API.Controllers;

[ApiController]
[Authorize(Roles = "Admin")]
[Route("api/admin/users")]
public sealed class AdminUsersController : ControllerBase
{
    private readonly AppDbContext _dbContext;

    public AdminUsersController(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var monthStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var nextMonthStart = monthStart.AddMonths(1);

        var users = await _dbContext.Users
            .OrderBy(x => x.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        var userIds = users.Select(x => x.Id).ToList();

        var activeOverrides = await _dbContext.UserQuotaOverrides
            .Where(x =>
                userIds.Contains(x.UserId) &&
                x.ValidFromUtc <= now &&
                (x.ValidToUtc == null || x.ValidToUtc >= now))
            .GroupBy(x => x.UserId)
            .Select(g => g.OrderByDescending(x => x.CreatedAtUtc).First())
            .ToListAsync(cancellationToken);

        var usageRecords = await _dbContext.UserUsageRecords
            .Where(x =>
                userIds.Contains(x.UserId) &&
                x.OccurredAtUtc >= monthStart &&
                x.OccurredAtUtc < nextMonthStart)
            .ToListAsync(cancellationToken);

        var overridesByUserId = activeOverrides.ToDictionary(x => x.UserId, x => x);
        var usageByUserId = usageRecords
            .GroupBy(x => x.UserId)
            .ToDictionary(x => x.Key, x => x.ToList());

        var result = users.Select(user =>
        {
            overridesByUserId.TryGetValue(user.Id, out var activeOverride);
            usageByUserId.TryGetValue(user.Id, out var userUsage);
            userUsage ??= new List<UserUsageRecord>();

            var hasUnlimited = activeOverride?.HasUnlimitedAiUsageOverride ?? user.HasUnlimitedAiUsage;

            int effectiveChatLimit = activeOverride?.MonthlyChatMessageLimitOverride ?? user.MonthlyChatMessageLimit;
            int effectiveUploadLimit = activeOverride?.MonthlyDocumentUploadLimitOverride ?? user.MonthlyDocumentUploadLimit;
            int effectiveSummaryLimit = activeOverride?.MonthlySummarizationLimitOverride ?? user.MonthlySummarizationLimit;
            int effectiveExtractionLimit = activeOverride?.MonthlyExtractionLimitOverride ?? user.MonthlyExtractionLimit;
            int effectiveComparisonLimit = activeOverride?.MonthlyComparisonLimitOverride ?? user.MonthlyComparisonLimit;

            int chatUsed = userUsage.Where(x => x.UsageType == UsageType.ChatMessage).Sum(x => x.Quantity);
            int uploadUsed = userUsage.Where(x => x.UsageType == UsageType.UploadDocument).Sum(x => x.Quantity);
            int summaryUsed = userUsage.Where(x => x.UsageType == UsageType.SummarizeDocument).Sum(x => x.Quantity);
            int extractionUsed = userUsage.Where(x => x.UsageType == UsageType.ExtractDocument).Sum(x => x.Quantity);
            int comparisonUsed = userUsage.Where(x => x.UsageType == UsageType.CompareDocument).Sum(x => x.Quantity);

            return new
            {
                user.Id,
                user.Email,
                user.DisplayName,
                Role = user.Role.ToString(),
                user.IsActive,
                AuthProvider = user.AuthProvider.ToString(),
                HasUnlimitedAiUsage = hasUnlimited,
                MonthlyChatMessageLimit = effectiveChatLimit,
                MonthlyDocumentUploadLimit = effectiveUploadLimit,
                MonthlySummarizationLimit = effectiveSummaryLimit,
                MonthlyExtractionLimit = effectiveExtractionLimit,
                MonthlyComparisonLimit = effectiveComparisonLimit,
                Usage = new
                {
                    ChatMessages = new
                    {
                        Used = chatUsed,
                        Remaining = hasUnlimited ? (int?)null : Math.Max(0, effectiveChatLimit - chatUsed)
                    },
                    DocumentUploads = new
                    {
                        Used = uploadUsed,
                        Remaining = hasUnlimited ? (int?)null : Math.Max(0, effectiveUploadLimit - uploadUsed)
                    },
                    Summarizations = new
                    {
                        Used = summaryUsed,
                        Remaining = hasUnlimited ? (int?)null : Math.Max(0, effectiveSummaryLimit - summaryUsed)
                    },
                    Extractions = new
                    {
                        Used = extractionUsed,
                        Remaining = hasUnlimited ? (int?)null : Math.Max(0, effectiveExtractionLimit - extractionUsed)
                    },
                    Comparisons = new
                    {
                        Used = comparisonUsed,
                        Remaining = hasUnlimited ? (int?)null : Math.Max(0, effectiveComparisonLimit - comparisonUsed)
                    }
                },
                HasActiveOverride = activeOverride != null,
                OverrideReason = activeOverride?.Reason,
                user.CreatedAtUtc
            };
        });

        return Ok(result);
    }

    [HttpPatch("{userId:guid}/role")]
    public async Task<IActionResult> UpdateRole(Guid userId, UpdateUserRoleRequest request, CancellationToken cancellationToken)
    {
        var user = await _dbContext.Users.FirstOrDefaultAsync(x => x.Id == userId, cancellationToken);
        if (user is null)
        {
            throw new NotFoundException("User was not found.");
        }

        if (!Enum.TryParse<UserRole>(request.Role, true, out var role))
        {
            throw new BadRequestException("Invalid role.");
        }

        user.Role = role;
        await _dbContext.SaveChangesAsync(cancellationToken);

        return Ok();
    }

    [HttpPatch("{userId:guid}/active-status")]
    public async Task<IActionResult> UpdateActiveStatus(Guid userId, UpdateUserActiveStatusRequest request, CancellationToken cancellationToken)
    {
        var user = await _dbContext.Users.FirstOrDefaultAsync(x => x.Id == userId, cancellationToken);
        if (user is null)
        {
            throw new NotFoundException("User was not found.");
        }

        user.IsActive = request.IsActive;
        await _dbContext.SaveChangesAsync(cancellationToken);

        return Ok();
    }

    [HttpPatch("{userId:guid}/limits")]
    public async Task<IActionResult> UpdateLimits(Guid userId, UpdateUserLimitsRequest request, CancellationToken cancellationToken)
    {
        var user = await _dbContext.Users.FirstOrDefaultAsync(x => x.Id == userId, cancellationToken);
        if (user is null)
        {
            throw new NotFoundException("User was not found.");
        }

        var now = DateTime.UtcNow;

        var entity = new UserQuotaOverride
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            HasUnlimitedAiUsageOverride = request.HasUnlimitedAiUsage,
            MonthlyChatMessageLimitOverride = request.MonthlyChatMessageLimit,
            MonthlyDocumentUploadLimitOverride = request.MonthlyDocumentUploadLimit,
            MonthlySummarizationLimitOverride = request.MonthlySummarizationLimit,
            MonthlyExtractionLimitOverride = request.MonthlyExtractionLimit,
            MonthlyComparisonLimitOverride = request.MonthlyComparisonLimit,
            Reason = request.Reason,
            ValidFromUtc = now,
            ValidToUtc = request.ValidToUtc,
            CreatedAtUtc = now
        };

        _dbContext.UserQuotaOverrides.Add(entity);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return Ok();
    }

    [HttpDelete("{userId:guid}/limits")]
    public async Task<IActionResult> RemoveActiveOverrides(Guid userId, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;

        var overrides = await _dbContext.UserQuotaOverrides
            .Where(x => x.UserId == userId
                        && x.ValidFromUtc <= now
                        && (x.ValidToUtc == null || x.ValidToUtc >= now))
            .ToListAsync(cancellationToken);

        foreach (var item in overrides)
        {
            item.ValidToUtc = now;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return Ok();
    }
}