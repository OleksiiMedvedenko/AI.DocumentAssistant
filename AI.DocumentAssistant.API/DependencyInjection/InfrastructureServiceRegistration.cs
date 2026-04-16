using AI.DocumentAssistant.Application.Abstractions.AI;
using AI.DocumentAssistant.Application.Abstractions.Authentication;
using AI.DocumentAssistant.Application.Abstractions.Chats;
using AI.DocumentAssistant.Application.Abstractions.Common;
using AI.DocumentAssistant.Application.Abstractions.Communication;
using AI.DocumentAssistant.Application.Abstractions.Documents;
using AI.DocumentAssistant.Application.Auth.Models;
using AI.DocumentAssistant.Application.Chats.Services;
using AI.DocumentAssistant.Application.Services.AI;
using AI.DocumentAssistant.Application.Services.Authentication;
using AI.DocumentAssistant.Application.Services.Communication;
using AI.DocumentAssistant.Application.Services.DocumentProcessing;
using AI.DocumentAssistant.Application.Services.Storage;
using AI.DocumentAssistant.Application.Services.Time;
using AI.DocumentAssistant.Infrastructure.BackgroundProcessing;
using AI.DocumentAssistant.Infrastructure.Persistence;
using AI.DocumentAssistant.Infrastructure.Persistence.Abstractions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.Text;

namespace AI.DocumentAssistant.Infrastructure.DependencyInjection;

public static class InfrastructureServiceRegistration
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<JwtOptions>(configuration.GetSection(JwtOptions.SectionName));
        services.Configure<LocalStorageOptions>(configuration.GetSection(LocalStorageOptions.SectionName));
        services.Configure<OpenAiOptions>(configuration.GetSection(OpenAiOptions.SectionName));
        services.Configure<SmtpOptions>(configuration.GetSection(SmtpOptions.SectionName));
        services.Configure<EmailConfirmationOptions>(
            configuration.GetSection(EmailConfirmationOptions.SectionName));

        services.AddDbContext<AppDbContext>(options =>
            options.UseSqlServer(configuration.GetConnectionString("DefaultConnection")));

        services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<AppDbContext>());

        services.AddHttpContextAccessor();

        services.AddScoped<IPasswordHasher, PasswordHasher>();
        services.AddScoped<IJwtTokenService, JwtTokenService>();
        services.AddScoped<ICurrentUserService, CurrentUserService>();
        services.AddSingleton<ISystemClock, SystemClock>();
        services.AddScoped<IFileStorageService, LocalFileStorageService>();

        services.AddHttpClient<IOpenAiService, OpenAiService>();
        services.AddHttpClient<IEmbeddingService, OpenAiEmbeddingService>();

        services.AddScoped<IPdfTextExtractor, PdfPigTextExtractor>();
        services.AddScoped<IDocumentTextExtractor, PdfPigTextExtractor>();
        services.AddScoped<IDocumentTextExtractor, PlainTextDocumentTextExtractor>();
        services.AddScoped<IDocumentTextExtractor, CsvDocumentTextExtractor>();
        services.AddScoped<IDocumentTextExtractor, DocxDocumentTextExtractor>();

        services.AddScoped<IAccountEmailTemplateService, AccountEmailTemplateService>();
        services.AddScoped<IEmailSender, SmtpEmailSender>();

        services.AddSingleton<IDocumentProcessingQueue, DocumentProcessingQueue>();
        services.AddHostedService<QueuedDocumentProcessingBackgroundService>();

        services.Configure<ChatRetrievalOptions>(
            configuration.GetSection(ChatRetrievalOptions.SectionName));

        var jwtSection = configuration.GetSection(JwtOptions.SectionName);
        var jwtOptions = jwtSection.Get<JwtOptions>();

        if (jwtOptions is null)
        {
            throw new InvalidOperationException(
                $"JWT configuration section '{JwtOptions.SectionName}' is missing.");
        }

        if (string.IsNullOrWhiteSpace(jwtOptions.SecretKey))
        {
            throw new InvalidOperationException(
                $"JWT SecretKey is missing in section '{JwtOptions.SectionName}'.");
        }

        if (jwtOptions.SecretKey.Length < 32)
        {
            throw new InvalidOperationException(
                $"JWT SecretKey in section '{JwtOptions.SectionName}' is too short. Minimum 32 characters required.");
        }

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.SecretKey));

        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer = jwtOptions.Issuer,
                    ValidAudience = jwtOptions.Audience,
                    IssuerSigningKey = key,
                    ClockSkew = TimeSpan.Zero
                };
            });

        services.AddAuthorization();

        return services;
    }
}