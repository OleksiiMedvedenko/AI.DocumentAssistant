using AI.DocumentAssistant.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AI.DocumentAssistant.Infrastructure.Persistence.Configurations;

public sealed class AiActionTemplateConfiguration : IEntityTypeConfiguration<AiActionTemplate>
{
    public void Configure(EntityTypeBuilder<AiActionTemplate> builder)
    {
        builder.ToTable("AiActionTemplates");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Name).HasMaxLength(150).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(500);
        builder.Property(x => x.DocumentType).HasMaxLength(100);
        builder.Property(x => x.ActionType).HasMaxLength(50).IsRequired();
        builder.Property(x => x.Prompt).HasColumnType("nvarchar(max)").IsRequired();
        builder.Property(x => x.OutputFormat).HasMaxLength(50).IsRequired();
        builder.Property(x => x.Language).HasMaxLength(20);
        builder.Property(x => x.Visibility).HasConversion<string>().HasMaxLength(40).IsRequired();
        builder.HasIndex(x => new { x.UserId, x.DocumentType, x.Name });
        builder.HasIndex(x => new { x.UserId, x.FolderId });
        builder.HasIndex(x => new { x.OrganizationId, x.Visibility, x.DocumentType, x.Name });

        builder.HasOne(x => x.User)
            .WithMany(x => x.AiActionTemplates)
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.Organization)
            .WithMany()
            .HasForeignKey(x => x.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Folder)
            .WithMany(x => x.AiActionTemplates)
            .HasForeignKey(x => x.FolderId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
