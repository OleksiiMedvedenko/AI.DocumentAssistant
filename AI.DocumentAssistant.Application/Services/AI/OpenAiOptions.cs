namespace AI.DocumentAssistant.Application.Services.AI;

public sealed class OpenAiOptions
{
    public const string SectionName = "OpenAI";

    public string ApiKey { get; set; } = default!;
    public string Model { get; set; } = "gpt-4o-mini";
    public string BaseUrl { get; set; } = "https://api.openai.com/v1/";
    public double Temperature { get; set; } = 0.2;
}