using AI.DocumentAssistant.Application.Abstractions.AI;
using AI.DocumentAssistant.Application.Abstractions.Authentication;
using AI.DocumentAssistant.Application.Abstractions.Common;
using AI.DocumentAssistant.Application.Abstractions.Documents;
using AI.DocumentAssistant.Application.Chats.Services;
using AI.DocumentAssistant.Application.Services.AI;
using AI.DocumentAssistant.Application.Services.Authentication;
using AI.DocumentAssistant.Application.Services.DocumentProcessing;
using AI.DocumentAssistant.Application.Services.Storage;
using AI.DocumentAssistant.Application.Services.Time;
using AI.DocumentAssistant.Infrastructure.BackgroundProcessing;
using AI.DocumentAssistant.Infrastructure.Persistence;
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

        services.AddDbContext<AppDbContext>(options =>
            options.UseSqlServer(configuration.GetConnectionString("DefaultConnection")));

        services.AddHttpContextAccessor();

        services.AddScoped<IPasswordHasher, PasswordHasher>();
        services.AddScoped<IJwtTokenService, JwtTokenService>();
        services.AddScoped<ICurrentUserService, CurrentUserService>();

        services.AddSingleton<ISystemClock, SystemClock>();

        services.AddScoped<IFileStorageService, LocalFileStorageService>();

        services.AddHttpClient<IOpenAiService, OpenAiService>();

        services.AddScoped<IPdfTextExtractor, PdfPigTextExtractor>();
        services.AddScoped<IDocumentTextExtractor, PdfPigTextExtractor>();
        services.AddScoped<IDocumentTextExtractor, PlainTextDocumentTextExtractor>();
        services.AddScoped<IDocumentTextExtractor, CsvDocumentTextExtractor>();
        services.AddScoped<IDocumentTextExtractor, DocxDocumentTextExtractor>();

        services.AddSingleton<IDocumentProcessingQueue, DocumentProcessingQueue>();
        services.AddHostedService<QueuedDocumentProcessingBackgroundService>();

        services.Configure<ChatRetrievalOptions>(
            configuration.GetSection(ChatRetrievalOptions.SectionName));

        var jwtOptions = configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>()!;
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