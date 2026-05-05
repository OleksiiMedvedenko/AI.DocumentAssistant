using AI.DocumentAssistant.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AI.DocumentAssistant.Infrastructure.Persistence.Configurations;

public sealed class UserFolderRuleConfiguration : IEntityTypeConfiguration<UserFolderRule>
{
    public void Configure(EntityTypeBuilder<UserFolderRule> builder)
    {
        builder.ToTable("UserFolderRules");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Pattern)
            .HasMaxLength(300)
            .IsRequired();

        builder.Property(x => x.DocumentType)
            .HasMaxLength(100);

        builder.Property(x => x.Topic)
            .HasMaxLength(100);

        builder.Property(x => x.Weight)
            .HasColumnType("decimal(8,4)");

        builder.HasIndex(x => new { x.UserId, x.FolderId, x.Pattern });

        builder.HasOne(x => x.User)
            .WithMany(x => x.FolderRules)
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne(x => x.Folder)
            .WithMany(x => x.UserRules)
            .HasForeignKey(x => x.FolderId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}