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
            builder.Property(x => x.Visibility).HasConversion<string>().HasMaxLength(40).IsRequired();

            builder.HasIndex(x => new { x.UserId, x.ParentFolderId, x.Key }).IsUnique();
            builder.HasIndex(x => new { x.OrganizationId, x.ParentFolderId, x.Key });

            builder.HasOne(x => x.User)
                .WithMany(x => x.DocumentFolders)
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasOne(x => x.Organization)
                .WithMany(x => x.Folders)
                .HasForeignKey(x => x.OrganizationId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.HasOne(x => x.ParentFolder)
                .WithMany(x => x.Children)
                .HasForeignKey(x => x.ParentFolderId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.HasMany(x => x.Documents)
                .WithOne(x => x.Folder)
                .HasForeignKey(x => x.FolderId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.HasMany(x => x.ChatSessions)
                .WithOne(x => x.Folder)
                .HasForeignKey(x => x.FolderId)
                .OnDelete(DeleteBehavior.Restrict);
        }
    }
}