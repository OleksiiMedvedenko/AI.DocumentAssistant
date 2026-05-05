using AI.DocumentAssistant.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AI.DocumentAssistant.Infrastructure.Persistence.Configurations;

public sealed class DocumentIntelligenceSnapshotConfiguration : IEntityTypeConfiguration<DocumentIntelligenceSnapshot>
{
    public void Configure(EntityTypeBuilder<DocumentIntelligenceSnapshot> builder)
    {
        builder.ToTable("DocumentIntelligenceSnapshots");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.DocumentType).HasMaxLength(100).IsRequired();
        builder.Property(x => x.BusinessDomain).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Topic).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Confidence).HasColumnType("decimal(5,4)");
        builder.Property(x => x.Keywords).HasMaxLength(1000).IsRequired();
        builder.Property(x => x.Reason).HasMaxLength(1000).IsRequired();

        builder.HasIndex(x => x.DocumentId).IsUnique();
        builder.HasIndex(x => new { x.UserId, x.DocumentType, x.BusinessDomain });
        builder.HasIndex(x => new { x.UserId, x.Year, x.Month });

        builder.HasOne(x => x.Document)
            .WithOne(x => x.IntelligenceSnapshot)
            .HasForeignKey<DocumentIntelligenceSnapshot>(x => x.DocumentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.User)
            .WithMany(x => x.DocumentIntelligenceSnapshots)
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
