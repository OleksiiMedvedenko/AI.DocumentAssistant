using AI.DocumentAssistant.Application.Abstractions.Documents;

namespace AI.DocumentAssistant.Application.Documents.Services
{
    public sealed class DocumentChunkingService : IDocumentChunkingService
    {
        public IReadOnlyList<string> Chunk(string text, int maxChunkLength = 2000)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return Array.Empty<string>();
            }

            var normalized = text.Replace("\r\n", "\n").Trim();
            var chunks = new List<string>();

            for (var i = 0; i < normalized.Length; i += maxChunkLength)
            {
                var length = Math.Min(maxChunkLength, normalized.Length - i);
                chunks.Add(normalized.Substring(i, length));
            }

            return chunks;
        }
    }
}
