using AI.DocumentAssistant.Application.Abstractions.Chats;
using AI.DocumentAssistant.Domain.Entities;

namespace AI.DocumentAssistant.Application.Chats.Services;

public sealed class HybridChunkRetrievalService : IChunkRetrievalService
{
    private readonly VectorChunkRetrievalService _vectorChunkRetrievalService;
    private readonly KeywordChunkRetrievalService _keywordChunkRetrievalService;

    public HybridChunkRetrievalService(
        VectorChunkRetrievalService vectorChunkRetrievalService,
        KeywordChunkRetrievalService keywordChunkRetrievalService)
    {
        _vectorChunkRetrievalService = vectorChunkRetrievalService;
        _keywordChunkRetrievalService = keywordChunkRetrievalService;
    }

    public async Task<IReadOnlyList<DocumentChunk>> GetBestMatchingChunksAsync(
        IReadOnlyCollection<DocumentChunk> chunks,
        string question,
        IReadOnlyCollection<string>? chatHistory = null,
        int take = 6,
        CancellationToken cancellationToken = default)
    {
        if (chunks.Count == 0)
        {
            return [];
        }

        var finalTake = take > 0 ? take : 6;

        var keywordResult = await _keywordChunkRetrievalService.GetBestMatchingChunksAsync(
            chunks,
            question,
            chatHistory,
            Math.Max(finalTake, 10),
            cancellationToken);

        var vectorResult = await _vectorChunkRetrievalService.GetBestMatchingChunksAsync(
            chunks,
            question,
            chatHistory,
            Math.Max(finalTake, 10),
            cancellationToken);

        var scores = new Dictionary<Guid, double>();
        var byId = chunks.ToDictionary(x => x.Id);

        AddRankScores(keywordResult, scores, 1.0);
        AddRankScores(vectorResult, scores, 1.25);

        var merged = scores
            .OrderByDescending(x => x.Value)
            .Select(x => byId[x.Key])
            .ToList();

        var expanded = ExpandNeighbors(chunks, merged, finalTake);

        return expanded
            .OrderBy(x => x.ChunkIndex)
            .Take(finalTake)
            .ToList();
    }

    private static void AddRankScores(
        IReadOnlyList<DocumentChunk> ranked,
        IDictionary<Guid, double> scores,
        double weight)
    {
        for (var i = 0; i < ranked.Count; i++)
        {
            var chunk = ranked[i];
            var score = weight * (1.0 / (i + 1));

            if (scores.TryGetValue(chunk.Id, out var existing))
            {
                scores[chunk.Id] = existing + score;
            }
            else
            {
                scores[chunk.Id] = score;
            }
        }
    }

    private static List<DocumentChunk> ExpandNeighbors(
        IReadOnlyCollection<DocumentChunk> allChunks,
        IReadOnlyList<DocumentChunk> ranked,
        int targetCount)
    {
        var ordered = allChunks.OrderBy(x => x.ChunkIndex).ToList();
        var byIndex = ordered.ToDictionary(x => x.ChunkIndex);
        var selected = new HashSet<Guid>();
        var result = new List<DocumentChunk>();

        foreach (var chunk in ranked)
        {
            if (selected.Add(chunk.Id))
            {
                result.Add(chunk);
            }

            if (result.Count >= targetCount)
            {
                break;
            }

            if (byIndex.TryGetValue(chunk.ChunkIndex - 1, out var prev) && selected.Add(prev.Id))
            {
                result.Add(prev);
            }

            if (result.Count >= targetCount)
            {
                break;
            }

            if (byIndex.TryGetValue(chunk.ChunkIndex + 1, out var next) && selected.Add(next.Id))
            {
                result.Add(next);
            }

            if (result.Count >= targetCount)
            {
                break;
            }
        }

        return result;
    }
}