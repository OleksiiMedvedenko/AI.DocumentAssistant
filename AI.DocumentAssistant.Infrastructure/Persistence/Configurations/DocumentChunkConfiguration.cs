using System.Text.Json;
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

            builder.Property(x => x.Text)
                .IsRequired();

            builder.Property(x => x.Embedding)
                .HasConversion(
                    value => value == null
                        ? null
                        : JsonSerializer.Serialize(value, (JsonSerializerOptions?)null),
                    value => string.IsNullOrWhiteSpace(value)
                        ? null
                        : JsonSerializer.Deserialize<float[]>(value, (JsonSerializerOptions?)null));

            builder.HasOne(x => x.Document)
                .WithMany(x => x.Chunks)
                .HasForeignKey(x => x.DocumentId)
                .OnDelete(DeleteBehavior.Cascade);
        }
    }
}