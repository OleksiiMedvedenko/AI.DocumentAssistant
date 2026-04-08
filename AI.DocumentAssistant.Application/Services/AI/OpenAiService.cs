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

    public Task<string> GenerateSummaryAsync(
        string text,
        string? language,
        CancellationToken cancellationToken)
    {
        var safeText = TrimInput(text, 18_000);
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

        return SendChatCompletionAsync(request, cancellationToken);
    }

    public Task<string> AnswerQuestionAsync(
    string documentContext,
    string question,
    string? language,
    CancellationToken cancellationToken)
    {
        var safeContext = TrimInput(documentContext, 28000);
        var safeQuestion = question?.Trim() ?? string.Empty;
        var languageInstruction = BuildLanguageInstruction(language, safeQuestion);
        var currentUtc = DateTime.UtcNow.ToString("yyyy-MM-dd");

        var request = new
        {
            model = _options.Model,
            temperature = 0.15,
            messages = new object[]
            {
            new
            {
                role = "developer",
                content =
                    "You are an intelligent document assistant. " +
                    "Use the provided document context as the main source of truth. " +
                    "Your task is not only to extract explicit facts, but also to make careful professional inferences when the document strongly suggests them. " +
                    "A strong inference may be based on job title, responsibilities, tools, systems, domain terminology, timeline, or surrounding details. " +
                    "Examples: if a candidate works in payroll/HR administration and uses Płatnik, kadry i płace, payroll systems, or listy płac, it is reasonable to infer involvement in salary calculation or payroll-related work unless the document suggests otherwise. " +
                    "Never present an inference as a confirmed fact. Label it clearly as likely, probable, suggested, or inferred. " +
                    "Decision policy:\n" +
                    "1. If the answer is explicitly stated, answer directly.\n" +
                    "2. If the answer is not explicit but strongly suggested by role, tools, systems, or responsibilities, answer with a careful inference.\n" +
                    "3. If the support is weak, say the document only suggests it weakly.\n" +
                    "4. If the context does not support it, say the document does not provide enough information.\n" +
                    "5. Mention the strongest supporting evidence briefly.\n" +
                    "6. Prefer concise, useful answers over generic refusals.\n" +
                    "7. If the question is about dates, timelines, current status, or whether something is current, assume today's UTC date is " + currentUtc + ".\n" +
                    languageInstruction
            },
            new
            {
                role = "user",
                content =
                    $"TODAY (UTC): {currentUtc}\n\n" +
                    $"QUESTION:\n{safeQuestion}\n\n" +
                    $"DOCUMENT CONTEXT:\n{safeContext}\n\n" +
                    "Return the final answer only."
            }
            }
        };

        return SendChatCompletionAsync(request, cancellationToken);
    }

    public Task<string> ExtractStructuredDataAsync(
        string documentContext,
        string extractionType,
        string? language,
        CancellationToken cancellationToken)
    {
        var safeContext = TrimInput(documentContext, 18_000);
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
                            fields = new { type = "object", additionalProperties = true }
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
                        "Extract structured information from the document. " +
                        "Return valid JSON matching the schema. " +
                        "Use only information supported by the document. " +
                        "Do not invent missing values. " +
                        $"The 'summary' field should follow this rule: {languageInstruction}"
                },
                new
                {
                    role = "user",
                    content = $"EXTRACTION TYPE: {safeType}\n\nDOCUMENT:\n{safeContext}"
                }
            }
        };

        return SendChatCompletionAsync(request, cancellationToken);
    }

    public Task<string> CompareDocumentsAsync(
        string firstDocumentText,
        string secondDocumentText,
        string? comparisonPrompt,
        string? language,
        CancellationToken cancellationToken)
    {
        var safeFirst = TrimInput(firstDocumentText, 14_000);
        var safeSecond = TrimInput(secondDocumentText, 14_000);

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

        return SendChatCompletionAsync(request, cancellationToken);
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
                if (string.IsNullOrWhiteSpace(text))
                {
                    throw new InvalidOperationException("OpenAI returned empty content.");
                }

                return text.Trim();
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
                if (string.IsNullOrWhiteSpace(result))
                {
                    throw new InvalidOperationException("OpenAI returned empty content.");
                }

                return result;
            }
        }

        throw new InvalidOperationException("OpenAI response did not contain message content.");
    }

    private static string BuildSummaryDeveloperPrompt(string languageInstruction)
    {
        return
            "You summarize documents clearly and accurately. " +
            "Return plain text only. " +
            "Do not invent facts. " +
            "Prefer concrete facts over generic statements. " +
            "Focus on the most important information explicitly present in the document. " +
            languageInstruction;
    }

    private static string BuildSummaryUserPrompt(string safeText)
    {
        return
            "Summarize the following document in a concise but useful way.\n" +
            "Focus on concrete facts explicitly present in the document.\n\n" +
            $"DOCUMENT:\n{safeText}";
    }

    private static string BuildQuestionAnsweringDeveloperPrompt(string languageInstruction)
    {
        return
            "You answer questions about a document. " +
            "Use the provided document context as the primary source. " +
            "Do not invent facts. " +
            "If something is inferred rather than explicitly stated, clearly say so. " +
            languageInstruction;
    }

    private static string BuildComparisonDeveloperPrompt(string languageInstruction)
    {
        return
            "You compare two documents accurately. Return plain text only. " +
            "Structure the answer with: Summary, Similarities, Differences, Missing or conflicting information, Conclusion. " +
            "Do not invent facts. " +
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

        return !string.IsNullOrWhiteSpace(fallbackUserText)
            ? "Respond in the same language as the user's request."
            : "Respond in English.";
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