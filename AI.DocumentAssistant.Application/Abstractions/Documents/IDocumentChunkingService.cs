namespace AI.DocumentAssistant.Application.Abstractions.Documents;

public interface IDocumentChunkingService
{
    IReadOnlyList<string> Chunk(string text, int chunkSize = 1200, int overlap = 200);
}