using AI.DocumentAssistant.Application.Abstractions.AI;

namespace AI.DocumentAssistant.UnitTests.TestDoubles;

public sealed class FakeOpenAiService : IOpenAiService
{
    public Task<string> GenerateSummaryAsync(
        string text,
        string? language,
        CancellationToken cancellationToken)
        => Task.FromResult($"SUMMARY::{Lang(language)}::{Trim(text)}");

    public Task<string> AnswerQuestionAsync(
        string documentContext,
        string question,
        string? language,
        CancellationToken cancellationToken)
        => Task.FromResult($"ANSWER::{Lang(language)}::{question}::CTX::{Trim(documentContext)}");

    public Task<string> ExtractStructuredDataAsync(
        string documentContext,
        string extractionType,
        string? language,
        CancellationToken cancellationToken)
        => Task.FromResult(
            $$"""
            {"extractionType":"{{extractionType}}","language":"{{Lang(language)}}","value":"fake","preview":"{{Escape(Trim(documentContext))}}"}
            """);

    public Task<string> CompareDocumentsAsync(
        string firstDocumentText,
        string secondDocumentText,
        string? comparisonPrompt,
        string? language,
        CancellationToken cancellationToken)
        => Task.FromResult(
            $"COMPARE::{Lang(language)}::{comparisonPrompt ?? "default"}::A::{Trim(firstDocumentText)}::B::{Trim(secondDocumentText)}");

    private static string Lang(string? language)
        => string.IsNullOrWhiteSpace(language) ? "default" : language.Trim();

    private static string Trim(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var normalized = text.Trim().Replace("\r", " ").Replace("\n", " ");
        return normalized.Length <= 120 ? normalized : normalized[..120];
    }

    private static string Escape(string value)
        => value.Replace("\\", "\\\\").Replace("\"", "\\\"");
}