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

    public async Task<string> GenerateSummaryAsync(string text, CancellationToken cancellationToken)
    {
        var safeText = TrimInput(text, 18000);

        var request = new
        {
            model = _options.Model,
            temperature = _options.Temperature,
            messages = new object[]
            {
                new
                {
                    role = "developer",
                    content = "You summarize documents clearly and accurately. Return plain text only."
                },
                new
                {
                    role = "user",
                    content =
                        $"Summarize the following document in a concise but useful way. Include the main purpose, key points, and notable facts.\n\nDOCUMENT:\n{safeText}"
                }
            }
        };

        return await SendChatCompletionAsync(request, cancellationToken);
    }

    public async Task<string> AnswerQuestionAsync(
        string documentContext,
        string question,
        CancellationToken cancellationToken)
    {
        var safeContext = TrimInput(documentContext, 20000);
        var safeQuestion = question?.Trim() ?? string.Empty;

        var request = new
        {
            model = _options.Model,
            temperature = 0.1,
            messages = new object[]
            {
                new
                {
                    role = "developer",
                    content =
                        "You answer questions using ONLY the provided document context. If the answer is not in the context, say that the document does not contain enough information."
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
        CancellationToken cancellationToken)
    {
        var safeContext = TrimInput(documentContext, 18000);
        var safeType = string.IsNullOrWhiteSpace(extractionType) ? "generic" : extractionType.Trim();

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
                        "Extract structured information from the document. Return valid JSON matching the schema."
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
                        sb.AppendLine(textPart.GetString());
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