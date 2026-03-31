using AI.DocumentAssistant.Application.Abstractions.AI;
using Microsoft.Extensions.Options;

namespace AI.DocumentAssistant.Application.Services.AI;

public sealed class OpenAiService : IOpenAiService
{
    private readonly OpenAiOptions _options;

    public OpenAiService(IOptions<OpenAiOptions> options)
    {
        _options = options.Value;
    }

    public Task<string> GenerateSummaryAsync(string text, CancellationToken cancellationToken)
    {
        return Task.FromResult($"[MOCK SUMMARY] {text[..Math.Min(text.Length, 500)]}");
    }

    public Task<string> AnswerQuestionAsync(string documentContext, string question, CancellationToken cancellationToken)
    {
        return Task.FromResult($"[MOCK ANSWER] Question: {question}");
    }

    public Task<string> ExtractStructuredDataAsync(string documentContext, string extractionType, CancellationToken cancellationToken)
    {
        return Task.FromResult("{}");
    }
}