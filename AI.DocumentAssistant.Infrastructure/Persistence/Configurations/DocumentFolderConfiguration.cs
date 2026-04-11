using AI.DocumentAssistant.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AI.DocumentAssistant.Infrastructure.Persistence.Configurations
{
    public sealed class DocumentFolderConfiguration : IEntityTypeConfiguration<DocumentFolder>
    {
        public void Configure(EntityTypeBuilder<DocumentFolder> builder)
        {
            builder.ToTable("DocumentFolders");

            builder.HasKey(x => x.Id);

            builder.Property(x => x.Key).HasMaxLength(150).IsRequired();
            builder.Property(x => x.Name).HasMaxLength(150).IsRequired();
            builder.Property(x => x.NamePl).HasMaxLength(150).IsRequired();
            builder.Property(x => x.NameEn).HasMaxLength(150).IsRequired();
            builder.Property(x => x.NameUa).HasMaxLength(150).IsRequired();

            builder.HasIndex(x => new { x.UserId, x.ParentFolderId, x.Key }).IsUnique();

            builder.HasOne(x => x.User)
                .WithMany(x => x.DocumentFolders)
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasOne(x => x.ParentFolder)
                .WithMany(x => x.Children)
                .HasForeignKey(x => x.ParentFolderId)
                .OnDelete(DeleteBehavior.Restrict);
        }
    }
}
