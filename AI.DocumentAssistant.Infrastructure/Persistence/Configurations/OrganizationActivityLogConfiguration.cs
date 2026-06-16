using AI.DocumentAssistant.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AI.DocumentAssistant.Infrastructure.Persistence.Configurations;

public sealed class OrganizationActivityLogConfiguration : IEntityTypeConfiguration<OrganizationActivityLog>
{
    public void Configure(EntityTypeBuilder<OrganizationActivityLog> builder)
    {
        builder.ToTable("OrganizationActivityLogs");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.ActionType).HasConversion<int>();
        builder.Property(x => x.PayloadJson).IsRequired();

        builder.HasIndex(x => new { x.OrganizationId, x.CreatedAtUtc });
        builder.HasIndex(x => x.ActorUserId);

        builder.HasOne(x => x.Organization)
            .WithMany(x => x.ActivityLogs)
            .HasForeignKey(x => x.OrganizationId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.ActorUser)
            .WithMany(x => x.OrganizationActivityLogs)
            .HasForeignKey(x => x.ActorUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
