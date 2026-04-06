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
        var users = await _dbContext.Users
            .OrderBy(x => x.CreatedAtUtc)
            .Select(x => new
            {
                x.Id,
                x.Email,
                x.DisplayName,
                Role = x.Role.ToString(),
                x.IsActive,
                AuthProvider = x.AuthProvider.ToString(),
                x.HasUnlimitedAiUsage,
                x.MonthlyChatMessageLimit,
                x.MonthlyDocumentUploadLimit,
                x.MonthlySummarizationLimit,
                x.MonthlyExtractionLimit,
                x.MonthlyComparisonLimit,
                x.CreatedAtUtc
            })
            .ToListAsync(cancellationToken);

        return Ok(users);
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