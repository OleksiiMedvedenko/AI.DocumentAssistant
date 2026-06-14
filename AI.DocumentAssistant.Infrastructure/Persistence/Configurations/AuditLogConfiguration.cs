using AI.DocumentAssistant.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AI.DocumentAssistant.Infrastructure.Persistence.Configurations;

public sealed class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.ToTable("AuditLogs");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Action).HasConversion<string>().HasMaxLength(80).IsRequired();
        builder.Property(x => x.ResourceType).HasConversion<string>().HasMaxLength(40);
        builder.Property(x => x.DetailsJson).HasMaxLength(4000);
        builder.HasIndex(x => new { x.OrganizationId, x.OccurredAtUtc });
        builder.HasOne(x => x.Organization)
            .WithMany()
            .HasForeignKey(x => x.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.ActorUser)
            .WithMany(x => x.AuditLogs)
            .HasForeignKey(x => x.ActorUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
