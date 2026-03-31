using AI.DocumentAssistant.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AI.DocumentAssistant.Infrastructure.Persistence.Configurations
{
    public sealed class DocumentChunkConfiguration : IEntityTypeConfiguration<DocumentChunk>
    {
        public void Configure(EntityTypeBuilder<DocumentChunk> builder)
        {
            builder.ToTable("DocumentChunks");
            builder.HasKey(x => x.Id);
            builder.Property(x => x.Text).IsRequired();
            builder.HasOne(x => x.Document)
                .WithMany(x => x.Chunks)
                .HasForeignKey(x => x.DocumentId)
                .OnDelete(DeleteBehavior.Cascade);
        }
    }
}
