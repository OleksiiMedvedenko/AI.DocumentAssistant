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
        services.AddScoped<IDocumentFolderService, DocumentFolderService>();
        services.AddScoped<IDocumentFolderClassifier, DocumentFolderClassifier>();

        services.AddScoped<IChatService, ChatService>();
        services.AddScoped<IChunkRetrievalService, HybridChunkRetrievalService>();
        services.AddScoped<IDocumentProcessingService, DocumentProcessingService>();
        services.AddScoped<IDocumentChunkingService, DocumentChunkingService>();
        services.AddScoped<IUsageTrackingService, UsageTrackingService>();
        services.AddScoped<IUsageQuotaService, UsageQuotaService>();

        services.AddScoped<KeywordChunkRetrievalService>();
        services.AddScoped<VectorChunkRetrievalService>();

        services.AddScoped<IFolderChatService, FolderChatService>();

        services.AddScoped<IDocumentFolderDecisionEngine, DocumentFolderDecisionEngine>();

        services.AddScoped<IDocumentPreviewConverter, DocumentPreviewConverter>();

        return services;
    }
}