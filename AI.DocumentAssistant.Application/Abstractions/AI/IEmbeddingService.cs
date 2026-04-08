namespace AI.DocumentAssistant.Application.Abstractions.AI;

public interface IEmbeddingService
{
    Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken cancellationToken);
}