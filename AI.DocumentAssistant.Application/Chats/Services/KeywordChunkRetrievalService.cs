using System.Text.RegularExpressions;
using AI.DocumentAssistant.Application.Abstractions.Chats;
using AI.DocumentAssistant.Domain.Entities;

namespace AI.DocumentAssistant.Application.Chats.Services;

public sealed class KeywordChunkRetrievalService : IChunkRetrievalService
{
    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "a", "an", "the", "and", "or", "but", "if", "then", "else",
        "is", "are", "was", "were", "be", "been", "being",
        "to", "of", "in", "on", "at", "for", "from", "with", "by", "about",
        "what", "which", "who", "whom", "when", "where", "why", "how",
        "czy", "co", "jak", "kiedy", "gdzie", "dlaczego", "który", "która", "ktore", "które",
        "i", "oraz", "lub", "ale", "że", "to", "jest", "są", "na", "w", "z", "do", "po", "od", "za"
    };

    public IReadOnlyList<DocumentChunk> GetBestMatchingChunks(
        IReadOnlyCollection<DocumentChunk> chunks,
        string question,
        int take = 6)
    {
        if (chunks.Count == 0)
        {
            return [];
        }

        if (string.IsNullOrWhiteSpace(question))
        {
            return chunks
                .OrderBy(x => x.ChunkIndex)
                .Take(take)
                .ToList();
        }

        var keywords = ExtractKeywords(question);

        if (keywords.Count == 0)
        {
            return chunks
                .OrderBy(x => x.ChunkIndex)
                .Take(take)
                .ToList();
        }

        var ranked = chunks
            .Select(chunk => new
            {
                Chunk = chunk,
                Score = CalculateScore(chunk.Text, keywords)
            })
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Chunk.ChunkIndex)
            .ToList();

        var best = ranked
            .Where(x => x.Score > 0)
            .Take(take)
            .Select(x => x.Chunk)
            .OrderBy(x => x.ChunkIndex)
            .ToList();

        if (best.Count > 0)
        {
            return best;
        }

        return chunks
            .OrderBy(x => x.ChunkIndex)
            .Take(take)
            .ToList();
    }

    private static HashSet<string> ExtractKeywords(string text)
    {
        var matches = Regex.Matches(text.ToLowerInvariant(), @"\p{L}[\p{L}\p{Nd}_-]*");

        return matches
            .Select(x => x.Value.Trim())
            .Where(x => x.Length >= 3)
            .Where(x => !StopWords.Contains(x))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static int CalculateScore(string chunkText, HashSet<string> keywords)
    {
        if (string.IsNullOrWhiteSpace(chunkText))
        {
            return 0;
        }

        var normalized = chunkText.ToLowerInvariant();
        var score = 0;

        foreach (var keyword in keywords)
        {
            var occurrences = CountOccurrences(normalized, keyword.ToLowerInvariant());

            if (occurrences > 0)
            {
                score += occurrences * 10;

                if (normalized.Contains($"{keyword}:"))
                {
                    score += 5;
                }

                if (normalized.Contains(keyword) && normalized.Length < 1200)
                {
                    score += 2;
                }
            }
        }

        return score;
    }

    private static int CountOccurrences(string source, string value)
    {
        var count = 0;
        var index = 0;

        while ((index = source.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }
}