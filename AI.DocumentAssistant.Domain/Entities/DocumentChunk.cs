namespace AI.DocumentAssistant.Domain.Entities
{
    public sealed class DocumentChunk
    {
        public Guid Id { get; set; }
        public Guid DocumentId { get; set; }
        public int ChunkIndex { get; set; }
        public string Text { get; set; } = default!;
        public float[]? Embedding { get; set; }

        public Document Document { get; set; } = default!;
    }
}