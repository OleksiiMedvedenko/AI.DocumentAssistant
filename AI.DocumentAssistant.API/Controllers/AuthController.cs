using AI.DocumentAssistant.API.Contracts.Auth;
using AI.DocumentAssistant.Application.Auth.Dtos;
using AI.DocumentAssistant.Application.Auth.Services;
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
            CreatedAtUtc = result.CreatedAtUtc
        });
    }
}