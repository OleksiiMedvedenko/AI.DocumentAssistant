using AI.DocumentAssistant.Application.Abstractions.AI;

namespace AI.DocumentAssistant.UnitTests.TestDoubles;

public sealed class FakeOpenAiService : IOpenAiService
{
    public Task<string> GenerateSummaryAsync(string text, CancellationToken cancellationToken)
        => Task.FromResult($"SUMMARY::{Trim(text)}");

    public Task<string> AnswerQuestionAsync(string documentContext, string question, CancellationToken cancellationToken)
        => Task.FromResult($"ANSWER::{question}::CTX::{Trim(documentContext)}");

    public Task<string> ExtractStructuredDataAsync(string documentContext, string extractionType, CancellationToken cancellationToken)
        => Task.FromResult($$"""
        {
          "extractionType": "{{extractionType}}",
          "value": "fake",
          "preview": "{{Escape(Trim(documentContext))}}"
        }
        """);

    public Task<string> CompareDocumentsAsync(
        string firstDocumentText,
        string secondDocumentText,
        string? comparisonPrompt,
        CancellationToken cancellationToken)
        => Task.FromResult(
            $"COMPARE::{comparisonPrompt ?? "default"}::A::{Trim(firstDocumentText)}::B::{Trim(secondDocumentText)}");

    private static string Trim(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var normalized = text.Trim().Replace("\r", " ").Replace("\n", " ");
        return normalized.Length <= 80 ? normalized : normalized[..80];
    }

    private static string Escape(string value)
        => value.Replace("\\", "\\\\").Replace("\"", "\\\"");

    public Task<string> GenerateSummaryAsync(string text, string? language, CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }

    public Task<string> AnswerQuestionAsync(string documentContext, string question, string? language, CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }

    public Task<string> ExtractStructuredDataAsync(string documentContext, string extractionType, string? language, CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }

    public Task<string> CompareDocumentsAsync(string firstDocumentText, string secondDocumentText, string? comparisonPrompt, string? language, CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }
}