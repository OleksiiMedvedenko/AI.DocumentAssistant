using AI.DocumentAssistant.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AI.DocumentAssistant.Infrastructure.Persistence.Configurations;

public sealed class AiActionRunConfiguration : IEntityTypeConfiguration<AiActionRun>
{
    public void Configure(EntityTypeBuilder<AiActionRun> builder)
    {
        builder.ToTable("AiActionRuns");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.TemplateName).HasMaxLength(200).IsRequired();
        builder.Property(x => x.ActionType).HasMaxLength(50).IsRequired();
        builder.Property(x => x.OutputFormat).HasMaxLength(50).IsRequired();
        builder.Property(x => x.Language).HasMaxLength(20);
        builder.Property(x => x.Prompt).HasColumnType("nvarchar(max)").IsRequired();
        builder.Property(x => x.Status).HasMaxLength(50).IsRequired();
        builder.Property(x => x.ResultText).HasColumnType("nvarchar(max)");
        builder.Property(x => x.ResultJson).HasColumnType("nvarchar(max)");
        builder.Property(x => x.ResultFilePath).HasMaxLength(1000);
        builder.Property(x => x.ResultFileName).HasMaxLength(255);
        builder.Property(x => x.ResultContentType).HasMaxLength(120);
        builder.Property(x => x.ErrorMessage).HasMaxLength(2000);

        builder.HasIndex(x => new { x.UserId, x.DocumentId, x.CreatedAtUtc });
        builder.HasIndex(x => new { x.UserId, x.TemplateId, x.CreatedAtUtc });
        builder.HasIndex(x => new { x.UserId, x.Status });

        builder.HasOne(x => x.User)
            .WithMany(x => x.AiActionRuns)
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne(x => x.Document)
            .WithMany(x => x.AiActionRuns)
            .HasForeignKey(x => x.DocumentId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne(x => x.Template)
            .WithMany(x => x.Runs)
            .HasForeignKey(x => x.TemplateId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
