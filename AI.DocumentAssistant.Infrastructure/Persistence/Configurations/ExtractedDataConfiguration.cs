using AI.DocumentAssistant.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AI.DocumentAssistant.Infrastructure.Persistence.Configurations
{
    public sealed class ExtractedDataConfiguration : IEntityTypeConfiguration<ExtractedData>
    {
        public void Configure(EntityTypeBuilder<ExtractedData> builder)
        {
            builder.ToTable("ExtractedData");
            builder.HasKey(x => x.Id);
            builder.Property(x => x.ExtractionType).HasMaxLength(100).IsRequired();
            builder.Property(x => x.JsonResult).IsRequired();
            builder.HasOne(x => x.Document)
                .WithMany(x => x.Extractions)
                .HasForeignKey(x => x.DocumentId)
                .OnDelete(DeleteBehavior.Cascade);
        }
    }
}
