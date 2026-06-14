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
        DbSet<Organization> Organizations { get; }
        DbSet<OrganizationMember> OrganizationMembers { get; }
        DbSet<OrganizationInvitation> OrganizationInvitations { get; }
        DbSet<Role> Roles { get; }
        DbSet<Permission> Permissions { get; }
        DbSet<RolePermission> RolePermissions { get; }
        DbSet<OrganizationMemberRole> OrganizationMemberRoles { get; }
        DbSet<Team> Teams { get; }
        DbSet<TeamMember> TeamMembers { get; }
        DbSet<AccessGrant> AccessGrants { get; }
        DbSet<AuditLog> AuditLogs { get; }

        Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
    }
}
