using AI.DocumentAssistant.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AI.DocumentAssistant.Infrastructure.Persistence.Configurations;

public sealed class OrganizationConfiguration : IEntityTypeConfiguration<Organization>
{
    public void Configure(EntityTypeBuilder<Organization> builder)
    {
        builder.ToTable("Organizations");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Name).HasMaxLength(180).IsRequired();
        builder.Property(x => x.Slug).HasMaxLength(180).IsRequired();
        builder.HasIndex(x => x.Slug).IsUnique();
        builder.HasIndex(x => x.OwnerUserId);
        builder.HasOne(x => x.OwnerUser)
            .WithMany(x => x.OwnedOrganizations)
            .HasForeignKey(x => x.OwnerUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
