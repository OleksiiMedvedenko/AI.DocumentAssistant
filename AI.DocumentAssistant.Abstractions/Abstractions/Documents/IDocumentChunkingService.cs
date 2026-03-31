namespace AI.DocumentAssistant.Abstraction.Abstractions.Documents
{
    public interface IDocumentChunkingService
    {
        IReadOnlyList<string> Chunk(string text, int maxChunkLength = 2000);
    }
}
