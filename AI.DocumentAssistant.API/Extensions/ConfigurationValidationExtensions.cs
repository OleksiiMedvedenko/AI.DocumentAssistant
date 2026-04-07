using AI.DocumentAssistant.API.Configuration;
using AI.DocumentAssistant.Application.Services.AI;
using AI.DocumentAssistant.Application.Services.Authentication;

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
            .Bind(configuration.GetSection("Jwt"))
            .Validate(options => !string.IsNullOrWhiteSpace(options.SecretKey),
                "Jwt:SecretKey is required.")
            .ValidateOnStart();

        services
            .AddOptions<OpenAiOptions>()
            .Bind(configuration.GetSection("OpenAI"))
            .Validate(options => !string.IsNullOrWhiteSpace(options.ApiKey),
                "OpenAI:ApiKey is required.")
            .ValidateOnStart();

        return services;
    }
}