using System.Text;
using System.Text.RegularExpressions;
using AI.DocumentAssistant.Application.Abstractions.Documents;

namespace AI.DocumentAssistant.Application.Services.DocumentProcessing;

public sealed class DocumentChunkingService : IDocumentChunkingService
{
    private static readonly Regex ParagraphSplitRegex = new(@"(\r\n|\r|\n){2,}", RegexOptions.Compiled);

    public IReadOnlyList<string> Chunk(string text, int chunkSize = 1600, int overlap = 240)
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

        var normalized = NormalizeText(text);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return [];
        }

        var paragraphs = SplitIntoParagraphs(normalized);
        var chunks = new List<string>();
        var current = new StringBuilder();

        foreach (var paragraph in paragraphs)
        {
            if (paragraph.Length > chunkSize)
            {
                FlushCurrentIfNeeded(chunks, current);

                foreach (var subChunk in SplitLargeParagraph(paragraph, chunkSize, overlap))
                {
                    if (!string.IsNullOrWhiteSpace(subChunk))
                    {
                        chunks.Add(subChunk.Trim());
                    }
                }

                continue;
            }

            if (current.Length == 0)
            {
                current.Append(paragraph);
                continue;
            }

            if (current.Length + 2 + paragraph.Length <= chunkSize)
            {
                current.AppendLine().AppendLine().Append(paragraph);
                continue;
            }

            chunks.Add(current.ToString().Trim());

            var overlapPrefix = BuildOverlapPrefix(current.ToString(), overlap);
            current.Clear();
            if (!string.IsNullOrWhiteSpace(overlapPrefix))
            {
                current.Append(overlapPrefix);
                if (current.Length > 0)
                {
                    current.AppendLine().AppendLine();
                }
            }

            current.Append(paragraph);
        }

        FlushCurrentIfNeeded(chunks, current);

        return chunks
            .Select(x => x.Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToList();
    }

    private static void FlushCurrentIfNeeded(List<string> chunks, StringBuilder current)
    {
        if (current.Length == 0)
        {
            return;
        }

        chunks.Add(current.ToString().Trim());
        current.Clear();
    }

    private static List<string> SplitIntoParagraphs(string input)
    {
        return ParagraphSplitRegex
            .Split(input)
            .Select(x => x?.Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Cast<string>()
            .ToList();
    }

    private static IReadOnlyList<string> SplitLargeParagraph(string paragraph, int chunkSize, int overlap)
    {
        var results = new List<string>();
        var start = 0;

        while (start < paragraph.Length)
        {
            var length = Math.Min(chunkSize, paragraph.Length - start);
            var candidate = paragraph.Substring(start, length);

            if (start + length < paragraph.Length)
            {
                var boundary = candidate.LastIndexOfAny(['.', '!', '?', ';', ':', '\n']);
                if (boundary > chunkSize / 2)
                {
                    candidate = candidate[..(boundary + 1)];
                    length = candidate.Length;
                }
            }

            candidate = candidate.Trim();
            if (!string.IsNullOrWhiteSpace(candidate))
            {
                results.Add(candidate);
            }

            start += Math.Max(1, length - overlap);
        }

        return results;
    }

    private static string BuildOverlapPrefix(string source, int overlap)
    {
        if (string.IsNullOrWhiteSpace(source) || overlap <= 0)
        {
            return string.Empty;
        }

        var tail = source.Length <= overlap ? source : source[^overlap..];
        var boundary = Math.Max(tail.IndexOf(' '), tail.IndexOf('\n'));
        if (boundary > 0 && boundary < tail.Length - 1)
        {
            tail = tail[(boundary + 1)..];
        }

        return tail.Trim();
    }

    private static string NormalizeText(string input)
    {
        var lines = input
            .Replace("\r\n", "\n")
            .Replace('\r', '\n')
            .Split('\n')
            .Select(line => Regex.Replace(line.Trim(), @"[ \t]+", " "))
            .ToList();

        var sb = new StringBuilder();
        var emptyCount = 0;

        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                emptyCount++;
                if (emptyCount <= 2)
                {
                    sb.AppendLine();
                }

                continue;
            }

            emptyCount = 0;
            sb.AppendLine(line);
        }

        return sb.ToString().Trim();
    }
}
