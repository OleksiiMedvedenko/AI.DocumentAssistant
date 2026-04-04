namespace AI.DocumentAssistant.Application.Abstractions.AI;

public interface IOpenAiService
{
    Task<string> GenerateSummaryAsync(
        string text,
        string? language,
        CancellationToken cancellationToken);

    Task<string> AnswerQuestionAsync(
        string documentContext,
        string question,
        string? language,
        CancellationToken cancellationToken);

    Task<string> ExtractStructuredDataAsync(
        string documentContext,
        string extractionType,
        string? language,
        CancellationToken cancellationToken);

    Task<string> CompareDocumentsAsync(
        string firstDocumentText,
        string secondDocumentText,
        string? comparisonPrompt,
        string? language,
        CancellationToken cancellationToken);
}