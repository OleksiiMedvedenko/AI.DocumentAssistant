namespace AI.DocumentAssistant.Application.Chats.Services;

public sealed class ChatRetrievalOptions
{
    public const string SectionName = "ChatRetrieval";

    public int DefaultTake { get; set; } = 8;
    public int MaxContextCharacters { get; set; } = 12_000;
    public int HistoryMessagesToUse { get; set; } = 4;
    public bool IncludeNeighborChunks { get; set; } = true;
    public int MaxExpandedChunks { get; set; } = 12;
    public int MinLexicalTokensLength { get; set; } = 2;
    public double LexicalWeight { get; set; } = 0.40;
    public double SemanticWeight { get; set; } = 0.60;
    public double ExactPhraseBoost { get; set; } = 0.12;
    public double NeighborScorePenalty { get; set; } = 0.92;
    public double MinAcceptedScore { get; set; } = 0.08;

    public List<string> StopWords { get; set; } = new()
    {
        "a", "an", "the", "and", "or", "but", "is", "are", "was", "were", "be", "been", "to", "of", "in", "on", "at", "for", "from", "with", "by",
        "what", "which", "who", "when", "where", "why", "how", "does", "did", "do", "it", "this", "that", "these", "those",
        "czy", "co", "jak", "kiedy", "gdzie", "dlaczego", "i", "oraz", "lub", "ale", "że", "to", "jest", "są", "na", "w", "z", "do", "się", "ten", "ta", "te",
        "що", "як", "коли", "де", "чому", "і", "або", "але", "це", "є", "у", "в", "з", "до", "та", "цей", "ця", "ці"
    };
}
