using AI.DocumentAssistant.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace AI.DocumentAssistant.Infrastructure.Persistence.Abstractions
{
    public interface IApplicationDbContext
    {
        DbSet<User> Users { get; }
        DbSet<RefreshToken> RefreshTokens { get; }
        DbSet<Document> Documents { get; }
        DbSet<DocumentFolder> DocumentFolders { get; }
        DbSet<DocumentChunk> DocumentChunks { get; }
        DbSet<ChatSession> ChatSessions { get; }
        DbSet<ChatMessage> ChatMessages { get; }
        DbSet<ExtractedData> ExtractedData { get; }
        DbSet<UserUsageRecord> UserUsageRecords { get; }
        DbSet<UserQuotaOverride> UserQuotaOverrides { get; }

        Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
    }
}
