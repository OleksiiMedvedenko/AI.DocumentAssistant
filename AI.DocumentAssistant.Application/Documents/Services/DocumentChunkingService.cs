using System.Text;
using AI.DocumentAssistant.Application.Abstractions.Documents;

namespace AI.DocumentAssistant.Application.Services.DocumentProcessing;

public sealed class DocumentChunkingService : IDocumentChunkingService
{
    public IReadOnlyList<string> Chunk(string text, int chunkSize = 1200, int overlap = 200)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        if (chunkSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(chunkSize));
        }

        if (overlap < 0 || overlap >= chunkSize)
        {
            throw new ArgumentOutOfRangeException(nameof(overlap));
        }

        var normalized = NormalizeWhitespace(text);
        var chunks = new List<string>();

        var start = 0;
        while (start < normalized.Length)
        {
            var length = Math.Min(chunkSize, normalized.Length - start);
            var candidate = normalized.Substring(start, length);

            if (start + length < normalized.Length)
            {
                var lastSentenceBreak = candidate.LastIndexOfAny(['.', '!', '?', '\n']);
                if (lastSentenceBreak > chunkSize / 2)
                {
                    candidate = candidate[..(lastSentenceBreak + 1)];
                    length = candidate.Length;
                }
            }

            candidate = candidate.Trim();
            if (!string.IsNullOrWhiteSpace(candidate))
            {
                chunks.Add(candidate);
            }

            start += Math.Max(1, length - overlap);
        }

        return chunks;
    }

    private static string NormalizeWhitespace(string input)
    {
        var sb = new StringBuilder(input.Length);
        var previousWasWhitespace = false;

        foreach (var ch in input)
        {
            var isWhitespace = char.IsWhiteSpace(ch);

            if (isWhitespace)
            {
                if (!previousWasWhitespace)
                {
                    sb.Append(' ');
                }
            }
            else
            {
                sb.Append(ch);
            }

            previousWasWhitespace = isWhitespace;
        }

        return sb.ToString().Trim();
    }
}