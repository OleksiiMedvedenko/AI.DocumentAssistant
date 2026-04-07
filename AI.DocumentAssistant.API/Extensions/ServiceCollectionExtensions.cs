using AI.DocumentAssistant.Application.Abstractions.Chats;
using AI.DocumentAssistant.Application.Abstractions.Documents;
using AI.DocumentAssistant.Application.Abstractions.Usage;
using AI.DocumentAssistant.Application.Auth.Services;
using AI.DocumentAssistant.Application.Chats.Services;
using AI.DocumentAssistant.Application.Documents.Services;
using AI.DocumentAssistant.Application.Services.DocumentProcessing;
using AI.DocumentAssistant.Application.Usage.Services;

namespace AI.DocumentAssistant.API.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        services.AddScoped<AuthService>();
        services.AddScoped<IDocumentService, DocumentService>();
        services.AddScoped<IChatService, ChatService>();
        services.AddScoped<IChunkRetrievalService, KeywordChunkRetrievalService>();
        services.AddScoped<IDocumentProcessingService, DocumentProcessingService>();
        services.AddScoped<IDocumentChunkingService, DocumentChunkingService>();
        services.AddScoped<AuthService>();
        services.AddScoped<IUsageTrackingService, UsageTrackingService>();
        services.AddScoped<IUsageQuotaService, UsageQuotaService>();

        return services;
    }
}