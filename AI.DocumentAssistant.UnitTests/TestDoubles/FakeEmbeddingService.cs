using AI.DocumentAssistant.Application.Abstractions.AI;

namespace AI.DocumentAssistant.UnitTests.TestDoubles;

public sealed class FakeEmbeddingService : IEmbeddingService
{
    public Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken cancellationToken)
    {
        var normalized = text?.Trim().ToLowerInvariant() ?? string.Empty;
        var vector = new float[16];

        foreach (var ch in normalized)
        {
            vector[ch % vector.Length] += 1f;
        }

        return Task.FromResult(vector);
    }
}