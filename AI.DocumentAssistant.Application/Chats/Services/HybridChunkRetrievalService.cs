using System.Text.RegularExpressions;
using AI.DocumentAssistant.Application.Abstractions.AI;
using AI.DocumentAssistant.Application.Abstractions.Chats;
using AI.DocumentAssistant.Domain.Entities;
using Microsoft.Extensions.Options;

namespace AI.DocumentAssistant.Application.Chats.Services;

public sealed class HybridChunkRetrievalService : IChunkRetrievalService
{
    private static readonly Regex TokenRegex = new(@"\p{L}[\p{L}\p{Nd}_-]*", RegexOptions.Compiled);
    private static readonly Regex QuotedPhraseRegex = new("\"([^\"]+)\"", RegexOptions.Compiled);

    private readonly IEmbeddingService _embeddingService;
    private readonly ChatRetrievalOptions _options;
    private readonly HashSet<string> _stopWords;

    public HybridChunkRetrievalService(
        IEmbeddingService embeddingService,
        IOptions<ChatRetrievalOptions> options)
    {
        _embeddingService = embeddingService;
        _options = options.Value;
        _stopWords = _options.StopWords.ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public async Task<IReadOnlyList<DocumentChunk>> GetBestMatchingChunksAsync(
        IReadOnlyCollection<DocumentChunk> chunks,
        string question,
        IReadOnlyCollection<string>? chatHistory = null,
        int take = 6,
        CancellationToken cancellationToken = default)
    {
        if (chunks.Count == 0 || string.IsNullOrWhiteSpace(question))
        {
            return Array.Empty<DocumentChunk>();
        }

        var finalTake = take > 0 ? take : _options.DefaultTake;

        var orderedChunks = chunks
            .OrderBy(x => x.DocumentId)
            .ThenBy(x => x.ChunkIndex)
            .ToList();

        var retrievalQuery = BuildRetrievalQuery(question, chatHistory);
        var normalizedQuery = retrievalQuery.Trim();
        var keywords = ExtractKeywords(normalizedQuery);
        var phrases = ExtractQuotedPhrases(question);

        float[]? queryEmbedding = null;
        try
        {
            queryEmbedding = await _embeddingService.GenerateEmbeddingAsync(normalizedQuery, cancellationToken);
        }
        catch
        {
            // lexical fallback only
        }

        var scored = orderedChunks
            .Select(chunk => ScoreChunk(chunk, keywords, phrases, queryEmbedding))
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Chunk.DocumentId)
            .ThenBy(x => x.Chunk.ChunkIndex)
            .ToList();

        var best = scored
            .Where(x => x.Score >= _options.MinAcceptedScore)
            .Take(finalTake)
            .Select(x => x.Chunk)
            .ToList();

        if (best.Count == 0)
        {
            best = scored
                .Take(finalTake)
                .Select(x => x.Chunk)
                .ToList();
        }

        if (_options.IncludeNeighborChunks)
        {
            best = ExpandWithNeighbors(orderedChunks, scored, best, _options.MaxExpandedChunks, finalTake);
        }

        return best
            .DistinctBy(x => x.Id)
            .OrderBy(x => x.DocumentId)
            .ThenBy(x => x.ChunkIndex)
            .ToList();
    }

    private RankedChunk ScoreChunk(
        DocumentChunk chunk,
        HashSet<string> keywords,
        IReadOnlyCollection<string> phrases,
        float[]? queryEmbedding)
    {
        var normalizedText = NormalizeForMatch(chunk.Text);
        if (string.IsNullOrWhiteSpace(normalizedText))
        {
            return new RankedChunk(chunk, 0d);
        }

        var lexicalScore = CalculateLexicalScore(normalizedText, keywords, phrases);
        var semanticScore = CalculateSemanticScore(queryEmbedding, chunk.Embedding);
        var finalScore = (_options.LexicalWeight * lexicalScore) + (_options.SemanticWeight * semanticScore);

        return new RankedChunk(chunk, finalScore);
    }

    private double CalculateLexicalScore(
        string normalizedChunkText,
        HashSet<string> keywords,
        IReadOnlyCollection<string> phrases)
    {
        if (keywords.Count == 0 && phrases.Count == 0)
        {
            return 0d;
        }

        double matchedKeywords = 0;
        double totalOccurrences = 0;

        foreach (var keyword in keywords)
        {
            var occurrences = CountOccurrences(normalizedChunkText, keyword);
            if (occurrences <= 0)
            {
                continue;
            }

            matchedKeywords += 1;
            totalOccurrences += Math.Min(occurrences, 4);
        }

        var coverage = keywords.Count == 0 ? 0d : matchedKeywords / keywords.Count;
        var density = keywords.Count == 0 ? 0d : Math.Min(1d, totalOccurrences / (keywords.Count * 2d));
        var score = (coverage * 0.7d) + (density * 0.3d);

        foreach (var phrase in phrases)
        {
            if (normalizedChunkText.Contains(NormalizeForMatch(phrase), StringComparison.Ordinal))
            {
                score += _options.ExactPhraseBoost;
            }
        }

        return Math.Min(1.0d, score);
    }

    private static double CalculateSemanticScore(float[]? queryEmbedding, float[]? chunkEmbedding)
    {
        if (queryEmbedding is null || chunkEmbedding is null || queryEmbedding.Length == 0 || chunkEmbedding.Length == 0)
        {
            return 0d;
        }

        var length = Math.Min(queryEmbedding.Length, chunkEmbedding.Length);
        double dot = 0d;
        double queryNorm = 0d;
        double chunkNorm = 0d;

        for (var i = 0; i < length; i++)
        {
            dot += queryEmbedding[i] * chunkEmbedding[i];
            queryNorm += queryEmbedding[i] * queryEmbedding[i];
            chunkNorm += chunkEmbedding[i] * chunkEmbedding[i];
        }

        if (queryNorm <= 0d || chunkNorm <= 0d)
        {
            return 0d;
        }

        var cosine = dot / (Math.Sqrt(queryNorm) * Math.Sqrt(chunkNorm));
        return Math.Clamp((cosine + 1d) / 2d, 0d, 1d);
    }

    private HashSet<string> ExtractKeywords(string text)
    {
        return TokenRegex.Matches(text)
            .Select(x => NormalizeToken(x.Value))
            .Where(x => x.Length >= _options.MinLexicalTokensLength)
            .Where(x => !_stopWords.Contains(x))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static List<string> ExtractQuotedPhrases(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        return QuotedPhraseRegex.Matches(text)
            .Select(x => x.Groups[1].Value.Trim())
            .Where(x => x.Length >= 3)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string BuildRetrievalQuery(string question, IReadOnlyCollection<string>? chatHistory)
    {
        var current = question?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(current))
        {
            return string.Empty;
        }

        if (chatHistory is null || chatHistory.Count == 0 || !LooksLikeFollowUpQuestion(current))
        {
            return current;
        }

        var historyWindow = chatHistory
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .TakeLast(2)
            .Select(x => x.Trim());

        return string.Join(" ", historyWindow.Append(current));
    }

    private static bool LooksLikeFollowUpQuestion(string question)
    {
        var q = question.Trim().ToLowerInvariant();
        if (q.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length <= 6)
        {
            return true;
        }

        string[] markers = [
            "a ", "i ", "oraz ", "też", "także", "co z ", "a co z", "czy też",
            "what about", "and ", "also ", "does it", "is it", "that", "those", "them", "it ", "he ", "she ",
            "а ", "і ", "це", "що щодо", "а як щодо"
        ];

        return markers.Any(marker => q.StartsWith(marker, StringComparison.Ordinal));
    }

    private static string NormalizeForMatch(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var lowered = value.ToLowerInvariant();
        var collapsed = Regex.Replace(lowered, @"\s+", " ");
        return collapsed.Trim();
    }

    private static string NormalizeToken(string value)
    {
        return value.Trim().ToLowerInvariant();
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

    private List<DocumentChunk> ExpandWithNeighbors(
        IReadOnlyList<DocumentChunk> allChunks,
        IReadOnlyList<RankedChunk> ranked,
        IReadOnlyList<DocumentChunk> bestChunks,
        int maxExpanded,
        int requestedTake)
    {
        var byKey = allChunks.ToDictionary(GetChunkKey);
        var selected = new Dictionary<string, double>();

        foreach (var rankedChunk in ranked.Where(x => bestChunks.Any(y => y.Id == x.Chunk.Id)))
        {
            selected[GetChunkKey(rankedChunk.Chunk)] = rankedChunk.Score;
        }

        foreach (var chunk in bestChunks
                     .OrderBy(x => x.DocumentId)
                     .ThenBy(x => x.ChunkIndex))
        {
            if (selected.Count >= maxExpanded)
            {
                break;
            }

            TryAddNeighbor(chunk.DocumentId, chunk.ChunkIndex - 1, chunk.ChunkIndex, byKey, selected);

            if (selected.Count >= maxExpanded)
            {
                break;
            }

            TryAddNeighbor(chunk.DocumentId, chunk.ChunkIndex + 1, chunk.ChunkIndex, byKey, selected);
        }

        return selected
            .OrderByDescending(x => x.Value)
            .ThenBy(x => x.Key, StringComparer.Ordinal)
            .Take(Math.Max(requestedTake, Math.Min(maxExpanded, selected.Count)))
            .Select(x => byKey[x.Key])
            .OrderBy(x => x.DocumentId)
            .ThenBy(x => x.ChunkIndex)
            .ToList();
    }

    private void TryAddNeighbor(
        Guid documentId,
        int neighborIndex,
        int originIndex,
        IReadOnlyDictionary<string, DocumentChunk> byKey,
        IDictionary<string, double> selected)
    {
        var neighborKey = GetChunkKey(documentId, neighborIndex);

        if (!byKey.TryGetValue(neighborKey, out _))
        {
            return;
        }

        if (selected.ContainsKey(neighborKey))
        {
            return;
        }

        var distancePenalty = Math.Pow(_options.NeighborScorePenalty, Math.Abs(neighborIndex - originIndex));
        selected[neighborKey] = distancePenalty;
    }

    private static string GetChunkKey(DocumentChunk chunk)
    {
        return GetChunkKey(chunk.DocumentId, chunk.ChunkIndex);
    }

    private static string GetChunkKey(Guid documentId, int chunkIndex)
    {
        return $"{documentId:N}:{chunkIndex}";
    }

    private sealed record RankedChunk(DocumentChunk Chunk, double Score);
}