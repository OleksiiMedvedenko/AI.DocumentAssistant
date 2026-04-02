namespace AI.DocumentAssistant.Application.Chats.Services;

public sealed class ChatRetrievalOptions
{
    public const string SectionName = "ChatRetrieval";

    public int DefaultTake { get; set; } = 6;

    public List<string> StopWords { get; set; } = new()
    {
        "a", "an", "the", "and", "or", "but", "if", "then", "else",
        "is", "are", "was", "were", "be", "been", "being",
        "to", "of", "in", "on", "at", "for", "from", "with", "by", "about",
        "what", "which", "who", "whom", "when", "where", "why", "how",
        "czy", "co", "jak", "kiedy", "gdzie", "dlaczego", "który", "która", "ktore", "które",
        "i", "oraz", "lub", "ale", "że", "to", "jest", "są", "na", "w", "z", "do", "po", "od", "za"
    };
}