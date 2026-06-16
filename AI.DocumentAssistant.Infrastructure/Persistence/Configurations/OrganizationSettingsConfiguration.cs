using AI.DocumentAssistant.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AI.DocumentAssistant.Infrastructure.Persistence.Configurations;

public sealed class OrganizationSettingsConfiguration : IEntityTypeConfiguration<OrganizationSettings>
{
    public void Configure(EntityTypeBuilder<OrganizationSettings> builder)
    {
        builder.ToTable("OrganizationSettings");
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => x.OrganizationId).IsUnique();
        builder.Property(x => x.InvitationLifetimeDays).HasDefaultValue(3);

        builder.HasOne(x => x.Organization)
            .WithOne(x => x.Settings)
            .HasForeignKey<OrganizationSettings>(x => x.OrganizationId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
