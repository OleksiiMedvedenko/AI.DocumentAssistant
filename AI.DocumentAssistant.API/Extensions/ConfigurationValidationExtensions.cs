using AI.DocumentAssistant.API.Configuration;
using AI.DocumentAssistant.Application.Auth;
using AI.DocumentAssistant.Application.Auth.Models;
using AI.DocumentAssistant.Application.Services.AI;
using AI.DocumentAssistant.Application.Services.Authentication;
using AI.DocumentAssistant.Application.Services.Communication;

namespace AI.DocumentAssistant.API.Extensions;

public static class ConfigurationValidationExtensions
{
    public static IServiceCollection AddValidatedConfiguration(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services
            .AddOptions<DatabaseOptions>()
            .Bind(configuration.GetSection(DatabaseOptions.SectionName))
            .ValidateOnStart();

        services
            .AddOptions<JwtOptions>()
            .Bind(configuration.GetSection(JwtOptions.SectionName))
            .Validate(x => !string.IsNullOrWhiteSpace(x.SecretKey), "Jwt:SecretKey is required.")
            .Validate(x => x.SecretKey.Length >= 32, "Jwt:SecretKey must be at least 32 characters.")
            .ValidateOnStart();

        services
            .AddOptions<OpenAiOptions>()
            .Bind(configuration.GetSection(OpenAiOptions.SectionName))
            .Validate(x => !string.IsNullOrWhiteSpace(x.ApiKey), "OpenAI:ApiKey is required.")
            .ValidateOnStart();

        services
            .AddOptions<SmtpOptions>()
            .Bind(configuration.GetSection(SmtpOptions.SectionName))
            .Validate(x => !string.IsNullOrWhiteSpace(x.Host), "Smtp:Host is required.")
            .Validate(x => x.Port > 0, "Smtp:Port must be greater than zero.")
            .Validate(x => !string.IsNullOrWhiteSpace(x.UserName), "Smtp:UserName is required.")
            .Validate(x => !string.IsNullOrWhiteSpace(x.Password), "Smtp:Password is required.")
            .Validate(x => !string.IsNullOrWhiteSpace(x.FromEmail), "Smtp:FromEmail is required.")
            .Validate(x => x.MaxRetryAttempts > 0, "Smtp:MaxRetryAttempts must be greater than zero.")
            .Validate(x => x.RetryBaseDelayMilliseconds > 0, "Smtp:RetryBaseDelayMilliseconds must be greater than zero.")
            .ValidateOnStart();

        services
            .AddOptions<EmailConfirmationOptions>()
            .Bind(configuration.GetSection(EmailConfirmationOptions.SectionName))
            .Validate(x => x.TokenLifetimeHours > 0, "EmailConfirmation:TokenLifetimeHours must be greater than zero.")
            .Validate(x => x.ResendCooldownSeconds >= 0, "EmailConfirmation:ResendCooldownSeconds must be greater than or equal to zero.")
            .Validate(x => x.AllowedFrontendHosts.Length > 0, "EmailConfirmation:AllowedFrontendHosts must contain at least one host.")
            .ValidateOnStart();

        return services;
    }
}