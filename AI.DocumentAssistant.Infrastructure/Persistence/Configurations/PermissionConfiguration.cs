using AI.DocumentAssistant.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AI.DocumentAssistant.Infrastructure.Persistence.Configurations;

public sealed class PermissionConfiguration : IEntityTypeConfiguration<Permission>
{
    public void Configure(EntityTypeBuilder<Permission> builder)
    {
        builder.ToTable("Permissions");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Key).HasMaxLength(120).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(400).IsRequired();
        builder.HasIndex(x => x.Key).IsUnique();
    }
}
