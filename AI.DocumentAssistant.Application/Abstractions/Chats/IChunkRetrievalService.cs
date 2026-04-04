using AI.DocumentAssistant.Domain.Entities;

namespace AI.DocumentAssistant.Application.Abstractions.Chats;

public interface IChunkRetrievalService
{
    IReadOnlyList<DocumentChunk> GetBestMatchingChunks(
        IReadOnlyCollection<DocumentChunk> chunks,
        string question,
        IReadOnlyCollection<string>? chatHistory = null,
        int take = 6);
}