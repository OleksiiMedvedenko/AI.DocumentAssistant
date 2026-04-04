using System.Text.RegularExpressions;
using AI.DocumentAssistant.Application.Abstractions.Chats;
using AI.DocumentAssistant.Domain.Entities;
using Microsoft.Extensions.Options;

namespace AI.DocumentAssistant.Application.Chats.Services;

public sealed class KeywordChunkRetrievalService : IChunkRetrievalService
{
    private readonly HashSet<string> _stopWords;
    private readonly ChatRetrievalOptions _options;

    public KeywordChunkRetrievalService(IOptions<ChatRetrievalOptions> options)
    {
        _options = options.Value;
        _stopWords = _options.StopWords.ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<DocumentChunk> GetBestMatchingChunks(
        IReadOnlyCollection<DocumentChunk> chunks,
        string question,
        IReadOnlyCollection<string>? chatHistory = null,
        int take = 6)
    {
        if (chunks.Count == 0)
        {
            return [];
        }

        var finalTake = take > 0 ? take : _options.DefaultTake;
        var combinedQuery = BuildCombinedQuery(question, chatHistory);
        var keywords = ExtractKeywords(combinedQuery);

        if (keywords.Count == 0)
        {
            return chunks.OrderBy(x => x.ChunkIndex).Take(finalTake).ToList();
        }

        var phraseBoostTerms = ExtractPhraseBoostTerms(question);

        var ranked = chunks
            .Select(chunk => new
            {
                Chunk = chunk,
                Score = CalculateScore(chunk.Text, keywords, phraseBoostTerms)
            })
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Chunk.ChunkIndex)
            .ToList();

        var best = ranked
            .Where(x => x.Score > 0)
            .Take(finalTake)
            .Select(x => x.Chunk)
            .ToList();

        if (best.Count == 0)
        {
            best = chunks.OrderBy(x => x.ChunkIndex).Take(finalTake).ToList();
        }

        if (_options.IncludeNeighborChunks)
        {
            best = ExpandWithNeighbors(chunks, best, finalTake);
        }

        return best
            .OrderBy(x => x.ChunkIndex)
            .ToList();
    }

    private string BuildCombinedQuery(string question, IReadOnlyCollection<string>? chatHistory)
    {
        if (chatHistory is null || chatHistory.Count == 0)
        {
            return question;
        }

        var history = string.Join(" ", chatHistory);
        return $"{history} {question}";
    }

    private HashSet<string> ExtractKeywords(string text)
    {
        var matches = Regex.Matches(text.ToLowerInvariant(), @"\p{L}[\p{L}\p{Nd}_-]*");

        return matches
            .Select(x => x.Value.Trim())
            .Where(x => x.Length >= 3)
            .Where(x => !_stopWords.Contains(x))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static List<string> ExtractPhraseBoostTerms(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        return Regex.Matches(text.Trim(), "\"([^\"]+)\"")
            .Select(x => x.Groups[1].Value.Trim())
            .Where(x => x.Length >= 3)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static int CalculateScore(
        string chunkText,
        HashSet<string> keywords,
        IReadOnlyCollection<string> phraseBoostTerms)
    {
        if (string.IsNullOrWhiteSpace(chunkText))
        {
            return 0;
        }

        var normalized = chunkText.ToLowerInvariant();
        var score = 0;
        var matchedKeywordCount = 0;

        foreach (var keyword in keywords)
        {
            var loweredKeyword = keyword.ToLowerInvariant();
            var occurrences = CountOccurrences(normalized, loweredKeyword);

            if (occurrences <= 0)
            {
                continue;
            }

            matchedKeywordCount++;
            score += occurrences * 8;

            if (normalized.Contains($"{loweredKeyword}:"))
            {
                score += 6;
            }

            if (normalized.Contains($"{loweredKeyword}\n") || normalized.StartsWith(loweredKeyword + " "))
            {
                score += 3;
            }
        }

        score += matchedKeywordCount * 5;

        foreach (var phrase in phraseBoostTerms)
        {
            if (normalized.Contains(phrase.ToLowerInvariant()))
            {
                score += 20;
            }
        }

        if (normalized.Length <= 1000)
        {
            score += 2;
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

    private static List<DocumentChunk> ExpandWithNeighbors(
        IReadOnlyCollection<DocumentChunk> allChunks,
        IReadOnlyCollection<DocumentChunk> bestChunks,
        int targetCount)
    {
        var byIndex = allChunks.ToDictionary(x => x.ChunkIndex);
        var selected = new HashSet<int>(bestChunks.Select(x => x.ChunkIndex));

        foreach (var chunk in bestChunks.OrderBy(x => x.ChunkIndex))
        {
            if (selected.Count >= targetCount)
            {
                break;
            }

            if (byIndex.TryGetValue(chunk.ChunkIndex - 1, out var previous))
            {
                selected.Add(previous.ChunkIndex);
            }

            if (selected.Count >= targetCount)
            {
                break;
            }

            if (byIndex.TryGetValue(chunk.ChunkIndex + 1, out var next))
            {
                selected.Add(next.ChunkIndex);
            }
        }

        return selected
            .Where(byIndex.ContainsKey)
            .Select(index => byIndex[index])
            .OrderBy(x => x.ChunkIndex)
            .Take(targetCount)
            .ToList();
    }
}