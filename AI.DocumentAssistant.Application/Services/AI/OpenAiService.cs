using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using AI.DocumentAssistant.Application.Abstractions.AI;
using Microsoft.Extensions.Options;

namespace AI.DocumentAssistant.Application.Services.AI;

public sealed class OpenAiService : IOpenAiService
{
    private readonly HttpClient _httpClient;
    private readonly OpenAiOptions _options;

    public OpenAiService(HttpClient httpClient, IOptions<OpenAiOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;

        _httpClient.BaseAddress = new Uri(_options.BaseUrl);
        _httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", _options.ApiKey);
    }

    public async Task<string> GenerateSummaryAsync(
        string text,
        string? language,
        CancellationToken cancellationToken)
    {
        var safeText = TrimInput(text, 18000);
        var languageInstruction = BuildLanguageInstruction(language);

        var request = new
        {
            model = _options.Model,
            temperature = 0.1,
            messages = new object[]
            {
                new
                {
                    role = "developer",
                    content = BuildSummaryDeveloperPrompt(languageInstruction)
                },
                new
                {
                    role = "user",
                    content = BuildSummaryUserPrompt(safeText)
                }
            }
        };

        return await SendChatCompletionAsync(request, cancellationToken);
    }

    public async Task<string> AnswerQuestionAsync(
        string documentContext,
        string question,
        string? language,
        CancellationToken cancellationToken)
    {
        var safeContext = TrimInput(documentContext, 20000);
        var safeQuestion = question?.Trim() ?? string.Empty;
        var languageInstruction = BuildLanguageInstruction(language, safeQuestion);

        var request = new
        {
            model = _options.Model,
            temperature = 0.1,
            messages = new object[]
            {
                new
                {
                    role = "developer",
                    content = BuildQuestionAnsweringDeveloperPrompt(languageInstruction)
                },
                new
                {
                    role = "user",
                    content =
                        $"QUESTION:\n{safeQuestion}\n\nDOCUMENT CONTEXT:\n{safeContext}"
                }
            }
        };

        return await SendChatCompletionAsync(request, cancellationToken);
    }

    public async Task<string> ExtractStructuredDataAsync(
        string documentContext,
        string extractionType,
        string? language,
        CancellationToken cancellationToken)
    {
        var safeContext = TrimInput(documentContext, 18000);
        var safeType = string.IsNullOrWhiteSpace(extractionType) ? "generic" : extractionType.Trim();
        var languageInstruction = BuildLanguageInstruction(language, safeType);

        var request = new
        {
            model = _options.Model,
            temperature = 0.0,
            response_format = new
            {
                type = "json_schema",
                json_schema = new
                {
                    name = "document_extraction",
                    schema = new
                    {
                        type = "object",
                        properties = new
                        {
                            extractionType = new { type = "string" },
                            summary = new { type = "string" },
                            fields = new
                            {
                                type = "object",
                                additionalProperties = true
                            }
                        },
                        required = new[] { "extractionType", "summary", "fields" },
                        additionalProperties = false
                    }
                }
            },
            messages = new object[]
            {
                new
                {
                    role = "developer",
                    content =
                        "Extract structured information from the document. Return valid JSON matching the schema. " +
                        "Use only information supported by the document. " +
                        "Do not invent missing values. " +
                        $"The 'summary' field should follow this rule: {languageInstruction}"
                },
                new
                {
                    role = "user",
                    content =
                        $"EXTRACTION TYPE: {safeType}\n\nDOCUMENT:\n{safeContext}"
                }
            }
        };

        return await SendChatCompletionAsync(request, cancellationToken);
    }

    public async Task<string> CompareDocumentsAsync(
        string firstDocumentText,
        string secondDocumentText,
        string? comparisonPrompt,
        string? language,
        CancellationToken cancellationToken)
    {
        var safeFirst = TrimInput(firstDocumentText, 14000);
        var safeSecond = TrimInput(secondDocumentText, 14000);
        var safePrompt = string.IsNullOrWhiteSpace(comparisonPrompt)
            ? "Compare the two documents. Focus on similarities, differences, missing information, and the most important conclusions."
            : comparisonPrompt.Trim();

        var languageInstruction = BuildLanguageInstruction(language, safePrompt);

        var request = new
        {
            model = _options.Model,
            temperature = 0.1,
            messages = new object[]
            {
                new
                {
                    role = "developer",
                    content = BuildComparisonDeveloperPrompt(languageInstruction)
                },
                new
                {
                    role = "user",
                    content =
                        $"COMPARISON TASK:\n{safePrompt}\n\nDOCUMENT A:\n{safeFirst}\n\nDOCUMENT B:\n{safeSecond}"
                }
            }
        };

        return await SendChatCompletionAsync(request, cancellationToken);
    }

    private async Task<string> SendChatCompletionAsync(object requestBody, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.PostAsJsonAsync(
            "chat/completions",
            requestBody,
            cancellationToken);

        var content = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"OpenAI request failed: {(int)response.StatusCode} {response.ReasonPhrase}. Body: {content}");
        }

        using var json = JsonDocument.Parse(content);
        var root = json.RootElement;

        var choices = root.GetProperty("choices");
        if (choices.GetArrayLength() == 0)
        {
            throw new InvalidOperationException("OpenAI returned no choices.");
        }

        var message = choices[0].GetProperty("message");

        if (message.TryGetProperty("content", out var contentElement))
        {
            if (contentElement.ValueKind == JsonValueKind.String)
            {
                var text = contentElement.GetString();
                return string.IsNullOrWhiteSpace(text)
                    ? throw new InvalidOperationException("OpenAI returned empty content.")
                    : text.Trim();
            }

            if (contentElement.ValueKind == JsonValueKind.Array)
            {
                var sb = new StringBuilder();

                foreach (var item in contentElement.EnumerateArray())
                {
                    if (item.TryGetProperty("text", out var textPart))
                    {
                        sb.Append(textPart.GetString());
                    }
                }

                var result = sb.ToString().Trim();
                return string.IsNullOrWhiteSpace(result)
                    ? throw new InvalidOperationException("OpenAI returned empty content.")
                    : result;
            }
        }

        throw new InvalidOperationException("OpenAI response did not contain message content.");
    }

    private static string BuildSummaryDeveloperPrompt(string languageInstruction)
    {
        return
            "You summarize documents clearly and accurately. Return plain text only. " +
            "Do not invent facts. " +
            "Prefer concrete facts over generic statements. " +
            "Focus on the most important information that is explicitly present in the document. " +
            "When the document looks like a CV or resume, prioritize the following when present: current or most recent role, most recent employer, employment dates, education, skills, certifications, and languages. " +
            "If dates are present, include them when they help explain the timeline. " +
            "If the document contains current employment indicators such as 'present', 'current', 'nadal', 'до тепер', include that fact clearly. " +
            "Do not add information that is not supported by the document. " +
            languageInstruction;
    }

    private static string BuildSummaryUserPrompt(string safeText)
    {
        return
            "Summarize the following document in a concise but useful way.\n" +
            "Focus on concrete facts that are explicitly present in the document.\n" +
            "Avoid generic wording.\n" +
            "If this is a CV or resume, make sure to include when present:\n" +
            "- current or most recent role\n" +
            "- most recent employer\n" +
            "- employment dates and whether the role is current\n" +
            "- earlier relevant experience\n" +
            "- education\n" +
            "- key skills\n" +
            "- languages or certifications\n\n" +
            $"DOCUMENT:\n{safeText}";
    }

    private static string BuildQuestionAnsweringDeveloperPrompt(string languageInstruction)
    {
        return
            "You are an AI assistant that answers questions about a document. " +
            "Use the provided document context as the primary source. " +
            "Do not invent facts that are not supported by the document. " +
            "If the answer is explicitly stated in the document, answer directly. " +
            "If the answer is not stated directly but can be reasonably inferred from the document, you may provide an inference, but you must clearly label it as an inference. " +
            "When giving an inference, briefly mention which part of the document supports it. " +
            "If the document does not contain enough information even for a reasonable inference, say that it is not found in the document. " +
            "Prefer exact facts over broad interpretation. " +
            "Keep answers concise, relevant, and honest about uncertainty. " +
            languageInstruction;
    }

    private static string BuildComparisonDeveloperPrompt(string languageInstruction)
    {
        return
            "You compare two documents accurately. Return plain text only. " +
            "Structure the answer with: Summary, Similarities, Differences, Missing or conflicting information, Conclusion. " +
            "Prefer concrete facts over generic statements. " +
            "Do not invent facts that are not supported by the documents. " +
            "When useful, mention dates, names, roles, quantities, and explicit factual differences. " +
            languageInstruction;
    }

    private static string BuildLanguageInstruction(string? language, string? fallbackUserText = null)
    {
        if (!string.IsNullOrWhiteSpace(language))
        {
            return language.Trim().ToLowerInvariant() switch
            {
                "en" => "Respond in English.",
                "pl" => "Respond in Polish.",
                "ua" => "Respond in Ukrainian.",
                "uk" => "Respond in Ukrainian.",
                _ => $"Respond in the language indicated by this code if possible: {language.Trim()}."
            };
        }

        if (!string.IsNullOrWhiteSpace(fallbackUserText))
        {
            return "Respond in the same language as the user's request.";
        }

        return "Respond in English.";
    }

    private static string TrimInput(string input, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return string.Empty;
        }

        var normalized = input.Trim();
        return normalized.Length <= maxLength
            ? normalized
            : normalized[..maxLength];
    }
}