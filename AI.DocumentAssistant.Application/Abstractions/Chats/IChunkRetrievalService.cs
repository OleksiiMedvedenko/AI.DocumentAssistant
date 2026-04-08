using AI.DocumentAssistant.Domain.Entities;

namespace AI.DocumentAssistant.Application.Abstractions.Chats;

public interface IChunkRetrievalService
{
    Task<IReadOnlyList<DocumentChunk>> GetBestMatchingChunksAsync(
        IReadOnlyCollection<DocumentChunk> chunks,
        string question,
        IReadOnlyCollection<string>? chatHistory = null,
        int take = 6,
        CancellationToken cancellationToken = default);
}