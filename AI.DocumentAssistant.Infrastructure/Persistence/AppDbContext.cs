using AI.DocumentAssistant.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace AI.DocumentAssistant.Infrastructure.Persistence;

public sealed class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<User> Users => Set<User>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<Document> Documents => Set<Document>();
    public DbSet<DocumentChunk> DocumentChunks => Set<DocumentChunk>();
    public DbSet<ChatSession> ChatSessions => Set<ChatSession>();
    public DbSet<ChatMessage> ChatMessages => Set<ChatMessage>();
    public DbSet<ExtractedData> ExtractedData => Set<ExtractedData>();
    public DbSet<UserUsageRecord> UserUsageRecords => Set<UserUsageRecord>();
    public DbSet<UserQuotaOverride> UserQuotaOverrides => Set<UserQuotaOverride>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        modelBuilder.Entity<User>(builder =>
        {
            builder.HasIndex(x => x.Email).IsUnique();
        });

        modelBuilder.Entity<UserUsageRecord>(builder =>
        {
            builder.HasKey(x => x.Id);

            builder.HasIndex(x => new { x.UserId, x.OccurredAtUtc });
            builder.HasIndex(x => new { x.UserId, x.UsageType, x.OccurredAtUtc });

            builder.Property(x => x.EstimatedCost).HasColumnType("decimal(18,6)");

            builder.HasOne(x => x.User)
                .WithMany(x => x.UsageRecords)
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<UserQuotaOverride>(builder =>
        {
            builder.HasKey(x => x.Id);

            builder.HasIndex(x => new { x.UserId, x.ValidFromUtc, x.ValidToUtc });

            builder.HasOne(x => x.User)
                .WithMany(x => x.QuotaOverrides)
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        base.OnModelCreating(modelBuilder);
    }
}