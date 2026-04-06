using AI.DocumentAssistant.API.Contracts.Auth;
using AI.DocumentAssistant.Application.Auth.Dtos;
using AI.DocumentAssistant.Application.Auth.Services;
using AI.DocumentAssistant.Application.Usage.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AI.DocumentAssistant.API.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController : ControllerBase
{
    private readonly AuthService _authService;

    public AuthController(AuthService authService)
    {
        _authService = authService;
    }

    [HttpPost("register")]
    public async Task<IActionResult> Register(RegisterRequest request, CancellationToken cancellationToken)
    {
        await _authService.RegisterAsync(new RegisterUserDto
        {
            Email = request.Email,
            Password = request.Password
        }, cancellationToken);

        return Ok();
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login(LoginRequest request, CancellationToken cancellationToken)
    {
        var result = await _authService.LoginAsync(new LoginUserDto
        {
            Email = request.Email,
            Password = request.Password
        }, cancellationToken);

        return Ok(new AuthResponse
        {
            AccessToken = result.AccessToken,
            RefreshToken = result.RefreshToken,
            ExpiresIn = result.ExpiresIn
        });
    }

    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh(RefreshTokenRequest request, CancellationToken cancellationToken)
    {
        var result = await _authService.RefreshAsync(new RefreshTokenDto
        {
            RefreshToken = request.RefreshToken
        }, cancellationToken);

        return Ok(new AuthResponse
        {
            AccessToken = result.AccessToken,
            RefreshToken = result.RefreshToken,
            ExpiresIn = result.ExpiresIn
        });
    }

    [Authorize]
    [HttpGet("me")]
    public async Task<IActionResult> Me(CancellationToken cancellationToken)
    {
        var result = await _authService.GetCurrentUserAsync(cancellationToken);

        return Ok(new CurrentUserResponse
        {
            Id = result.Id,
            Email = result.Email,
            DisplayName = result.DisplayName,
            Role = result.Role.ToString(),
            IsActive = result.IsActive,
            AuthProvider = result.AuthProvider.ToString(),
            CreatedAtUtc = result.CreatedAtUtc,
            Usage = new CurrentUserUsageResponse
            {
                HasUnlimitedAiUsage = result.UsageSummary.HasUnlimitedAiUsage,
                ChatMessages = Map(result.UsageSummary.ChatMessages),
                DocumentUploads = Map(result.UsageSummary.DocumentUploads),
                Summarizations = Map(result.UsageSummary.Summarizations),
                Extractions = Map(result.UsageSummary.Extractions),
                Comparisons = Map(result.UsageSummary.Comparisons)
            }
        });
    }

    private static UsageMetricResponse Map(UsageMetricDto dto)
    {
        return new UsageMetricResponse
        {
            Limit = dto.Limit,
            Used = dto.Used,
            Remaining = dto.Remaining
        };
    }
}