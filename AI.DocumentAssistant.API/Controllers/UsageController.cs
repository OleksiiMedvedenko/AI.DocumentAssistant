using AI.DocumentAssistant.Application.Abstractions.Common;
using AI.DocumentAssistant.Application.Abstractions.Usage;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AI.DocumentAssistant.API.Controllers;

[ApiController]
[Authorize]
[Route("api/usage")]
public sealed class UsageController : ControllerBase
{
    private readonly ICurrentUserService _currentUserService;
    private readonly IUsageQuotaService _usageQuotaService;

    public UsageController(
        ICurrentUserService currentUserService,
        IUsageQuotaService usageQuotaService)
    {
        _currentUserService = currentUserService;
        _usageQuotaService = usageQuotaService;
    }

    [HttpGet("me")]
    public async Task<IActionResult> GetMyUsage(CancellationToken cancellationToken)
    {
        var userId = _currentUserService.GetUserId();
        var result = await _usageQuotaService.GetMyUsageSummaryAsync(userId, cancellationToken);
        return Ok(result);
    }
}