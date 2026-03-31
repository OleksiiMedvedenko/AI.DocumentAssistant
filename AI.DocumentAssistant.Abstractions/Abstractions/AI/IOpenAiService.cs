namespace AI.DocumentAssistant.Abstraction.Abstractions.AI
{
    public interface IOpenAiService
    {
        Task<string> GenerateSummaryAsync(string text, CancellationToken cancellationToken);
        Task<string> AnswerQuestionAsync(string documentContext, string question, CancellationToken cancellationToken);
        Task<string> ExtractStructuredDataAsync(string documentContext, string extractionType, CancellationToken cancellationToken);
    }
}
