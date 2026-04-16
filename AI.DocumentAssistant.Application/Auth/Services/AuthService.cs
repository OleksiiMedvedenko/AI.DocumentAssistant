using AI.DocumentAssistant.Application.Abstractions.Authentication;
using AI.DocumentAssistant.Application.Abstractions.Common;
using AI.DocumentAssistant.Application.Abstractions.Communication;
using AI.DocumentAssistant.Application.Abstractions.Usage;
using AI.DocumentAssistant.Application.Auth;
using AI.DocumentAssistant.Application.Auth.Dtos;
using AI.DocumentAssistant.Application.Auth.Models;
using AI.DocumentAssistant.Application.Common.Exceptions;
using AI.DocumentAssistant.Domain.Entities;
using AI.DocumentAssistant.Domain.Enums;
using AI.DocumentAssistant.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using System.Web;

namespace AI.DocumentAssistant.Application.Auth.Services;

public sealed class AuthService
{
    private readonly AppDbContext _dbContext;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IJwtTokenService _jwtTokenService;
    private readonly ICurrentUserService _currentUserService;
    private readonly IUsageQuotaService _usageQuotaService;
    private readonly IEmailSender _emailSender;
    private readonly IAccountEmailTemplateService _accountEmailTemplateService;
    private readonly EmailConfirmationOptions _emailConfirmationOptions;
    private readonly ILogger<AuthService> _logger;

    public AuthService(
        AppDbContext dbContext,
        IPasswordHasher passwordHasher,
        IJwtTokenService jwtTokenService,
        ICurrentUserService currentUserService,
        IUsageQuotaService usageQuotaService,
        IEmailSender emailSender,
        IAccountEmailTemplateService accountEmailTemplateService,
        IOptions<EmailConfirmationOptions> emailConfirmationOptions,
        ILogger<AuthService> logger)
    {
        _dbContext = dbContext;
        _passwordHasher = passwordHasher;
        _jwtTokenService = jwtTokenService;
        _currentUserService = currentUserService;
        _usageQuotaService = usageQuotaService;
        _emailSender = emailSender;
        _accountEmailTemplateService = accountEmailTemplateService;
        _emailConfirmationOptions = emailConfirmationOptions.Value;
        _logger = logger;
    }

    public async Task RegisterAsync(RegisterUserDto dto, CancellationToken cancellationToken)
    {
        ValidateRegisterRequest(dto);

        var email = NormalizeEmail(dto.Email);

        var exists = await _dbContext.Users
            .AnyAsync(x => x.Email == email, cancellationToken);

        if (exists)
        {
            throw new BadRequestException("AUTH_EMAIL_ALREADY_EXISTS");
        }

        var rawToken = GenerateSecureToken();
        var now = DateTime.UtcNow;

        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = email,
            PasswordHash = _passwordHasher.Hash(dto.Password),
            CreatedAtUtc = now,
            Role = UserRole.User,
            AuthProvider = AuthProvider.Local,
            IsActive = true,
            EmailConfirmed = false,
            EmailConfirmationTokenHash = ComputeSha256(rawToken),
            EmailConfirmationTokenExpiresAtUtc = now.AddHours(_emailConfirmationOptions.TokenLifetimeHours),
            EmailConfirmationSentAtUtc = now,
            HasUnlimitedAiUsage = false
        };

        _dbContext.Users.Add(user);
        await _dbContext.SaveChangesAsync(cancellationToken);

        try
        {
            var confirmationUrl = BuildConfirmationUrl(dto.ConfirmationUrl, email, rawToken);
            var message = _accountEmailTemplateService.BuildConfirmationEmail(
                dto.Language,
                confirmationUrl,
                _emailConfirmationOptions.TokenLifetimeHours);

            await _emailSender.SendAsync(
                user.Email,
                message.Subject,
                message.HtmlBody,
                cancellationToken);
        }
        catch (ServiceUnavailableException ex)
        {
            _logger.LogError(ex, "User {Email} was created but confirmation email could not be sent", email);
            throw new ServiceUnavailableException("AUTH_CONFIRMATION_EMAIL_DELIVERY_FAILED");
        }
    }

    public async Task ConfirmEmailAsync(string email, string token, CancellationToken cancellationToken)
    {
        ValidateConfirmEmailRequest(email, token);

        var normalizedEmail = NormalizeEmail(email);

        var user = await _dbContext.Users
            .FirstOrDefaultAsync(x => x.Email == normalizedEmail, cancellationToken);

        if (user is null)
        {
            throw new BadRequestException("AUTH_CONFIRMATION_INVALID_OR_EXPIRED");
        }

        if (user.EmailConfirmed)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(user.EmailConfirmationTokenHash) ||
            user.EmailConfirmationTokenExpiresAtUtc is null ||
            user.EmailConfirmationTokenExpiresAtUtc <= DateTime.UtcNow)
        {
            throw new BadRequestException("AUTH_CONFIRMATION_INVALID_OR_EXPIRED");
        }

        var incomingHash = ComputeSha256(token);

        if (!string.Equals(user.EmailConfirmationTokenHash, incomingHash, StringComparison.Ordinal))
        {
            throw new BadRequestException("AUTH_CONFIRMATION_INVALID_OR_EXPIRED");
        }

        user.EmailConfirmed = true;
        user.EmailConfirmedAtUtc = DateTime.UtcNow;
        user.EmailConfirmationTokenHash = null;
        user.EmailConfirmationTokenExpiresAtUtc = null;
        user.EmailConfirmationSentAtUtc = null;

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task ResendConfirmationEmailAsync(ResendConfirmationEmailDto dto, CancellationToken cancellationToken)
    {
        ValidateResendConfirmationRequest(dto);

        var email = NormalizeEmail(dto.Email);

        var user = await _dbContext.Users
            .FirstOrDefaultAsync(x => x.Email == email, cancellationToken);

        if (user is null)
        {
            return;
        }

        if (user.EmailConfirmed)
        {
            throw new BadRequestException("AUTH_EMAIL_ALREADY_CONFIRMED");
        }

        var now = DateTime.UtcNow;
        var cooldown = TimeSpan.FromSeconds(_emailConfirmationOptions.ResendCooldownSeconds);

        if (user.EmailConfirmationSentAtUtc.HasValue &&
            now - user.EmailConfirmationSentAtUtc.Value < cooldown)
        {
            throw new BadRequestException("AUTH_CONFIRMATION_RESEND_COOLDOWN");
        }

        var rawToken = GenerateSecureToken();

        user.EmailConfirmationTokenHash = ComputeSha256(rawToken);
        user.EmailConfirmationTokenExpiresAtUtc = now.AddHours(_emailConfirmationOptions.TokenLifetimeHours);
        user.EmailConfirmationSentAtUtc = now;

        await _dbContext.SaveChangesAsync(cancellationToken);

        try
        {
            var confirmationUrl = BuildConfirmationUrl(dto.ConfirmationUrl, user.Email, rawToken);
            var message = _accountEmailTemplateService.BuildConfirmationEmail(
                dto.Language,
                confirmationUrl,
                _emailConfirmationOptions.TokenLifetimeHours);

            await _emailSender.SendAsync(
                user.Email,
                message.Subject,
                message.HtmlBody,
                cancellationToken);
        }
        catch (ServiceUnavailableException ex)
        {
            _logger.LogError(ex, "Confirmation email resend failed for {Email}", email);
            throw new ServiceUnavailableException("AUTH_CONFIRMATION_EMAIL_DELIVERY_FAILED");
        }
    }

    public async Task<AuthResultDto> LoginAsync(LoginUserDto dto, CancellationToken cancellationToken)
    {
        ValidateLoginRequest(dto);

        var email = NormalizeEmail(dto.Email);

        var user = await _dbContext.Users
            .FirstOrDefaultAsync(x => x.Email == email, cancellationToken);

        if (user is null || !_passwordHasher.Verify(dto.Password, user.PasswordHash))
        {
            throw new UnauthorizedException("AUTH_INVALID_CREDENTIALS");
        }

        if (!user.IsActive)
        {
            throw new UnauthorizedException("AUTH_ACCOUNT_INACTIVE");
        }

        if (!user.EmailConfirmed)
        {
            throw new UnauthorizedException("AUTH_EMAIL_NOT_CONFIRMED");
        }

        var accessToken = _jwtTokenService.GenerateAccessToken(user);
        var refreshToken = _jwtTokenService.GenerateRefreshToken();
        refreshToken.UserId = user.Id;

        _dbContext.RefreshTokens.Add(refreshToken);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return new AuthResultDto
        {
            AccessToken = accessToken,
            RefreshToken = refreshToken.Token,
            ExpiresIn = 3600
        };
    }

    public async Task<AuthResultDto> RefreshAsync(RefreshTokenDto dto, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(dto.RefreshToken))
        {
            throw new UnauthorizedException("AUTH_INVALID_REFRESH_TOKEN");
        }

        var token = await _dbContext.RefreshTokens
            .Include(x => x.User)
            .FirstOrDefaultAsync(x => x.Token == dto.RefreshToken, cancellationToken);

        if (token is null || token.IsRevoked || token.ExpiresAtUtc <= DateTime.UtcNow)
        {
            throw new UnauthorizedException("AUTH_INVALID_REFRESH_TOKEN");
        }

        if (!token.User.IsActive)
        {
            throw new UnauthorizedException("AUTH_ACCOUNT_INACTIVE");
        }

        if (!token.User.EmailConfirmed)
        {
            throw new UnauthorizedException("AUTH_EMAIL_NOT_CONFIRMED");
        }

        token.IsRevoked = true;

        var newRefreshToken = _jwtTokenService.GenerateRefreshToken();
        newRefreshToken.UserId = token.UserId;
        _dbContext.RefreshTokens.Add(newRefreshToken);

        await _dbContext.SaveChangesAsync(cancellationToken);

        return new AuthResultDto
        {
            AccessToken = _jwtTokenService.GenerateAccessToken(token.User),
            RefreshToken = newRefreshToken.Token,
            ExpiresIn = 3600
        };
    }

    public async Task<CurrentUserDto> GetCurrentUserAsync(CancellationToken cancellationToken)
    {
        var userId = _currentUserService.GetUserId();

        var user = await _dbContext.Users
            .FirstOrDefaultAsync(x => x.Id == userId, cancellationToken);

        if (user is null)
        {
            throw new UnauthorizedException("AUTH_NOT_AUTHENTICATED");
        }

        var usageSummary = await _usageQuotaService.GetMyUsageSummaryAsync(userId, cancellationToken);

        return new CurrentUserDto
        {
            Id = user.Id,
            Email = user.Email,
            DisplayName = user.DisplayName,
            Role = user.Role,
            IsActive = user.IsActive,
            AuthProvider = user.AuthProvider,
            CreatedAtUtc = user.CreatedAtUtc,
            UsageSummary = usageSummary
        };
    }

    private string BuildConfirmationUrl(string confirmationUrlFromFrontend, string email, string token)
    {
        if (string.IsNullOrWhiteSpace(confirmationUrlFromFrontend))
        {
            throw new BadRequestException("AUTH_CONFIRMATION_URL_REQUIRED");
        }

        if (!Uri.TryCreate(confirmationUrlFromFrontend, UriKind.Absolute, out var uri))
        {
            throw new BadRequestException("AUTH_CONFIRMATION_URL_INVALID");
        }

        if (!IsAllowedFrontendHost(uri))
        {
            throw new BadRequestException("AUTH_CONFIRMATION_URL_HOST_NOT_ALLOWED");
        }

        var builder = new UriBuilder(uri);
        var query = HttpUtility.ParseQueryString(builder.Query);
        query["email"] = email;
        query["token"] = token;
        builder.Query = query.ToString() ?? string.Empty;

        return builder.ToString();
    }

    private bool IsAllowedFrontendHost(Uri uri)
    {
        if (_emailConfirmationOptions.AllowedFrontendHosts.Length == 0)
        {
            return false;
        }

        return _emailConfirmationOptions.AllowedFrontendHosts.Any(x =>
            string.Equals(x.Trim(), uri.Host, StringComparison.OrdinalIgnoreCase));
    }

    private static void ValidateRegisterRequest(RegisterUserDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Email))
        {
            throw new BadRequestException("AUTH_EMAIL_REQUIRED");
        }

        if (!IsValidEmail(dto.Email))
        {
            throw new BadRequestException("AUTH_EMAIL_INVALID");
        }

        if (string.IsNullOrWhiteSpace(dto.Password))
        {
            throw new BadRequestException("AUTH_PASSWORD_REQUIRED");
        }

        if (dto.Password.Length < 8)
        {
            throw new BadRequestException("AUTH_PASSWORD_TOO_SHORT");
        }

        if (string.IsNullOrWhiteSpace(dto.ConfirmationUrl))
        {
            throw new BadRequestException("AUTH_CONFIRMATION_URL_REQUIRED");
        }
    }

    private static void ValidateLoginRequest(LoginUserDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Email))
        {
            throw new BadRequestException("AUTH_EMAIL_REQUIRED");
        }

        if (!IsValidEmail(dto.Email))
        {
            throw new BadRequestException("AUTH_EMAIL_INVALID");
        }

        if (string.IsNullOrWhiteSpace(dto.Password))
        {
            throw new BadRequestException("AUTH_PASSWORD_REQUIRED");
        }
    }

    private static void ValidateResendConfirmationRequest(ResendConfirmationEmailDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Email))
        {
            throw new BadRequestException("AUTH_EMAIL_REQUIRED");
        }

        if (!IsValidEmail(dto.Email))
        {
            throw new BadRequestException("AUTH_EMAIL_INVALID");
        }

        if (string.IsNullOrWhiteSpace(dto.ConfirmationUrl))
        {
            throw new BadRequestException("AUTH_CONFIRMATION_URL_REQUIRED");
        }
    }

    private static void ValidateConfirmEmailRequest(string email, string token)
    {
        if (string.IsNullOrWhiteSpace(email) || !IsValidEmail(email))
        {
            throw new BadRequestException("AUTH_CONFIRMATION_INVALID_OR_EXPIRED");
        }

        if (string.IsNullOrWhiteSpace(token))
        {
            throw new BadRequestException("AUTH_CONFIRMATION_INVALID_OR_EXPIRED");
        }
    }

    private static string NormalizeEmail(string email)
    {
        return email.Trim().ToLowerInvariant();
    }

    private static bool IsValidEmail(string email)
    {
        try
        {
            _ = new MailAddress(email);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string GenerateSecureToken()
    {
        Span<byte> bytes = stackalloc byte[32];
        RandomNumberGenerator.Fill(bytes);

        return Convert.ToBase64String(bytes)
            .Replace("+", "-")
            .Replace("/", "_")
            .Replace("=", string.Empty);
    }

    private static string ComputeSha256(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes);
    }
}