using AI.DocumentAssistant.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AI.DocumentAssistant.Infrastructure.Persistence.Configurations;

public sealed class DocumentFolderSuggestionConfiguration : IEntityTypeConfiguration<DocumentFolderSuggestion>
{
    public void Configure(EntityTypeBuilder<DocumentFolderSuggestion> builder)
    {
        builder.ToTable("DocumentFolderSuggestions");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.ProposedKey).HasMaxLength(150).IsRequired();
        builder.Property(x => x.ProposedName).HasMaxLength(150).IsRequired();
        builder.Property(x => x.ProposedNamePl).HasMaxLength(150).IsRequired();
        builder.Property(x => x.ProposedNameEn).HasMaxLength(150).IsRequired();
        builder.Property(x => x.ProposedNameUa).HasMaxLength(150).IsRequired();
        builder.Property(x => x.Score).HasColumnType("decimal(5,4)");
        builder.Property(x => x.RuleScore).HasColumnType("decimal(5,4)");
        builder.Property(x => x.SemanticScore).HasColumnType("decimal(5,4)");
        builder.Property(x => x.UserHistoryScore).HasColumnType("decimal(5,4)");
        builder.Property(x => x.FinalScore).HasColumnType("decimal(5,4)");
        builder.Property(x => x.Reason).HasMaxLength(1000).IsRequired();
        builder.Property(x => x.Status).HasMaxLength(50).IsRequired();

        builder.HasIndex(x => new { x.UserId, x.DocumentId, x.Status });
        builder.HasIndex(x => new { x.DocumentId, x.Rank });

        builder.HasOne(x => x.Document)
            .WithMany(x => x.FolderSuggestions)
            .HasForeignKey(x => x.DocumentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.User)
            .WithMany(x => x.DocumentFolderSuggestions)
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.ExistingFolder)
            .WithMany(x => x.Suggestions)
            .HasForeignKey(x => x.ExistingFolderId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.ProposedParentFolder)
            .WithMany()
            .HasForeignKey(x => x.ProposedParentFolderId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
