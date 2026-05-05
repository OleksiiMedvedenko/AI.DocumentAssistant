using AI.DocumentAssistant.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AI.DocumentAssistant.Infrastructure.Persistence.Configurations;

public sealed class FolderEmbeddingProfileConfiguration : IEntityTypeConfiguration<FolderEmbeddingProfile>
{
    public void Configure(EntityTypeBuilder<FolderEmbeddingProfile> builder)
    {
        builder.ToTable("FolderEmbeddingProfiles");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.SourceText).HasMaxLength(4000).IsRequired();
        builder.Property(x => x.EmbeddingJson);

        builder.HasIndex(x => x.FolderId).IsUnique();
        builder.HasIndex(x => x.UserId);

        builder.HasOne(x => x.User)
            .WithMany(x => x.FolderEmbeddingProfiles)
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne(x => x.Folder)
            .WithOne(x => x.EmbeddingProfile)
            .HasForeignKey<FolderEmbeddingProfile>(x => x.FolderId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
