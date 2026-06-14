using AI.DocumentAssistant.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AI.DocumentAssistant.Infrastructure.Persistence.Configurations
{
    public sealed class DocumentConfiguration : IEntityTypeConfiguration<Document>
    {
        public void Configure(EntityTypeBuilder<Document> builder)
        {
            builder.ToTable("Documents");

            builder.HasKey(x => x.Id);

            builder.Property(x => x.FileName)
                .HasMaxLength(255)
                .IsRequired();

            builder.Property(x => x.OriginalFileName)
                .HasMaxLength(255)
                .IsRequired();

            builder.Property(x => x.ContentType)
                .HasMaxLength(100)
                .IsRequired();

            builder.Property(x => x.StoragePath)
                .HasMaxLength(500)
                .IsRequired();

            builder.Property(x => x.Summary)
                .HasMaxLength(4000);

            builder.Property(x => x.OrganizationMode)
                .IsRequired();

            builder.Property(x => x.Visibility)
                .HasConversion<string>()
                .HasMaxLength(40)
                .IsRequired();

            builder.Property(x => x.FolderClassificationStatus)
                .HasMaxLength(50);

            builder.Property(x => x.FolderClassificationReason)
                .HasMaxLength(1000);

            builder.Property(x => x.FolderClassificationConfidence)
                .HasColumnType("decimal(5,4)");

            builder.HasOne(x => x.User)
                .WithMany(x => x.Documents)
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasOne(x => x.Organization)
                .WithMany(x => x.Documents)
                .HasForeignKey(x => x.OrganizationId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.HasOne(x => x.Folder)
                .WithMany(x => x.Documents)
                .HasForeignKey(x => x.FolderId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.HasIndex(x => new { x.UserId, x.FolderId, x.UploadedAtUtc });
            builder.HasIndex(x => new { x.UserId, x.OrganizationMode, x.UploadedAtUtc });
            builder.HasIndex(x => new { x.OrganizationId, x.Visibility, x.UploadedAtUtc });
        }
    }
}