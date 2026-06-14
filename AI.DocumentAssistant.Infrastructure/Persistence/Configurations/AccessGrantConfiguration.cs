using AI.DocumentAssistant.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AI.DocumentAssistant.Infrastructure.Persistence.Configurations;

public sealed class AccessGrantConfiguration : IEntityTypeConfiguration<AccessGrant>
{
    public void Configure(EntityTypeBuilder<AccessGrant> builder)
    {
        builder.ToTable("AccessGrants");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.ResourceType).HasConversion<string>().HasMaxLength(40).IsRequired();
        builder.Property(x => x.PermissionKey).HasMaxLength(120).IsRequired();
        builder.HasIndex(x => new { x.OrganizationId, x.ResourceType, x.ResourceId, x.PermissionKey });
        builder.HasOne(x => x.Organization)
            .WithMany(x => x.AccessGrants)
            .HasForeignKey(x => x.OrganizationId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.User)
            .WithMany(x => x.AccessGrants)
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Team)
            .WithMany(x => x.AccessGrants)
            .HasForeignKey(x => x.TeamId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Role)
            .WithMany()
            .HasForeignKey(x => x.RoleId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.GrantedByUser)
            .WithMany()
            .HasForeignKey(x => x.GrantedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
