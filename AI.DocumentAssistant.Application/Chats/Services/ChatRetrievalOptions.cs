namespace AI.DocumentAssistant.Application.Chats.Services;

public sealed class ChatRetrievalOptions
{
    public const string SectionName = "ChatRetrieval";

    public int DefaultTake { get; set; } = 6;
    public int MaxContextCharacters { get; set; } = 12000;
    public int HistoryMessagesToUse { get; set; } = 4;
    public bool IncludeNeighborChunks { get; set; } = true;

    public List<string> StopWords { get; set; } = new()
    {
        "a", "an", "the", "and", "or", "but",
        "is", "are", "was", "were", "be", "been",
        "to", "of", "in", "on", "at", "for", "from", "with", "by",
        "what", "which", "who", "when", "where", "why", "how",
        "czy", "co", "jak", "kiedy", "gdzie", "dlaczego",
        "i", "oraz", "lub", "ale", "że", "to", "jest", "są", "na", "w", "z", "do"
    };
}