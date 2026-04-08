using AI.DocumentAssistant.Application.Abstractions.AI;
using AI.DocumentAssistant.Application.Abstractions.Chats;
using AI.DocumentAssistant.Domain.Entities;

namespace AI.DocumentAssistant.Application.Chats.Services;

public sealed class VectorChunkRetrievalService : IChunkRetrievalService
{
    private const float MinSimilarityThreshold = 0.28f;

    private readonly IEmbeddingService _embeddingService;

    public VectorChunkRetrievalService(IEmbeddingService embeddingService)
    {
        _embeddingService = embeddingService;
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

        var chunksWithEmbeddings = chunks
            .Where(x => x.Embedding is { Length: > 0 })
            .ToList();

        if (chunksWithEmbeddings.Count == 0)
        {
            return [];
        }

        var finalTake = take > 0 ? take : 6;
        var query = BuildRetrievalQuery(question, chatHistory);

        var queryEmbedding = await _embeddingService.GenerateEmbeddingAsync(query, cancellationToken);

        var ranked = chunksWithEmbeddings
            .Select(chunk => new
            {
                Chunk = chunk,
                Score = CosineSimilarity(queryEmbedding, chunk.Embedding!)
            })
            .Where(x => x.Score >= MinSimilarityThreshold)
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Chunk.ChunkIndex)
            .Take(finalTake)
            .Select(x => x.Chunk)
            .OrderBy(x => x.ChunkIndex)
            .ToList();

        return ranked;
    }

    private static string BuildRetrievalQuery(string question, IReadOnlyCollection<string>? chatHistory)
    {
        var current = question?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(current))
        {
            return string.Empty;
        }

        if (!LooksLikeFollowUpQuestion(current))
        {
            return current;
        }

        if (chatHistory is null || chatHistory.Count == 0)
        {
            return current;
        }

        var lastUserMessage = chatHistory
            .LastOrDefault(x => !string.IsNullOrWhiteSpace(x))?
            .Trim();

        if (string.IsNullOrWhiteSpace(lastUserMessage))
        {
            return current;
        }

        return $"{lastUserMessage} {current}".Trim();
    }

    private static bool LooksLikeFollowUpQuestion(string question)
    {
        var q = question.Trim().ToLowerInvariant();

        string[] followUpMarkers =
        [
            "a ",
        "i ",
        "oraz ",
        "też",
        "także",
        "co z ",
        "a co z",
        "czy też",
        "what about",
        "and ",
        "also ",
        "does it",
        "is it",
        "that",
        "those",
        "them",
        "it ",
        "he ",
        "she ",
        "а ",
        "і ",
        "це"
        ];

        return q.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length <= 6
               || followUpMarkers.Any(marker => q.StartsWith(marker, StringComparison.Ordinal));
    }

    private static float CosineSimilarity(float[] left, float[] right)
    {
        var length = Math.Min(left.Length, right.Length);
        if (length == 0)
        {
            return -1f;
        }

        float dot = 0;
        float leftMagnitude = 0;
        float rightMagnitude = 0;

        for (var i = 0; i < length; i++)
        {
            dot += left[i] * right[i];
            leftMagnitude += left[i] * left[i];
            rightMagnitude += right[i] * right[i];
        }

        if (leftMagnitude <= 0 || rightMagnitude <= 0)
        {
            return -1f;
        }

        return dot / (float)(Math.Sqrt(leftMagnitude) * Math.Sqrt(rightMagnitude));
    }
}