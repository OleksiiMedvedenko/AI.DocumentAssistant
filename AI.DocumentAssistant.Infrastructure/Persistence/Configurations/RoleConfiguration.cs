using AI.DocumentAssistant.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AI.DocumentAssistant.Infrastructure.Persistence.Configurations;

public sealed class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    public void Configure(EntityTypeBuilder<Role> builder)
    {
        builder.ToTable("Roles");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Name).HasMaxLength(80).IsRequired();
        builder.Property(x => x.NormalizedName).HasMaxLength(80).IsRequired();
        builder.Property(x => x.Scope).HasMaxLength(40).IsRequired();
        builder.HasIndex(x => new { x.OrganizationId, x.NormalizedName }).IsUnique();
        builder.HasOne(x => x.Organization)
            .WithMany(x => x.Roles)
            .HasForeignKey(x => x.OrganizationId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
