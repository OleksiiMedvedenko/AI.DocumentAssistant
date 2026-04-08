using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using AI.DocumentAssistant.Application.Abstractions.AI;
using Microsoft.Extensions.Options;

namespace AI.DocumentAssistant.Application.Services.AI;

public sealed class OpenAiEmbeddingService : IEmbeddingService
{
    private readonly HttpClient _httpClient;
    private readonly OpenAiOptions _options;

    public OpenAiEmbeddingService(HttpClient httpClient, IOptions<OpenAiOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;

        _httpClient.BaseAddress = new Uri(_options.BaseUrl);
        _httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", _options.ApiKey);
    }

    public async Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken cancellationToken)
    {
        var input = string.IsNullOrWhiteSpace(text) ? string.Empty : text.Trim();

        var request = new
        {
            model = _options.EmbeddingModel,
            input
        };

        using var response = await _httpClient.PostAsJsonAsync(
            "embeddings",
            request,
            cancellationToken);

        var content = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"OpenAI embeddings request failed: {(int)response.StatusCode} {response.ReasonPhrase}. Body: {content}");
        }

        using var json = JsonDocument.Parse(content);
        var root = json.RootElement;

        var data = root.GetProperty("data");
        if (data.GetArrayLength() == 0)
        {
            throw new InvalidOperationException("OpenAI returned no embeddings.");
        }

        var embedding = data[0].GetProperty("embedding");

        var result = new float[embedding.GetArrayLength()];
        var index = 0;

        foreach (var item in embedding.EnumerateArray())
        {
            result[index++] = item.GetSingle();
        }

        return result;
    }
}