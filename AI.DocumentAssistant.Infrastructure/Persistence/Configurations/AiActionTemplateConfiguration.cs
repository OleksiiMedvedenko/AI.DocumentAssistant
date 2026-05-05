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
        builder.Property(x => x.DocumentType).HasMaxLength(100);
        builder.Property(x => x.Prompt).HasMaxLength(4000).IsRequired();
        builder.Property(x => x.OutputFormat).HasMaxLength(50).IsRequired();
        builder.HasIndex(x => new { x.UserId, x.DocumentType, x.Name });

        builder.HasOne(x => x.User)
            .WithMany(x => x.AiActionTemplates)
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
