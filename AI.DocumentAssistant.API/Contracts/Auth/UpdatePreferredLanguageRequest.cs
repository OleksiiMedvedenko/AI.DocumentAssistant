namespace AI.DocumentAssistant.API.Contracts.Auth;

public sealed class UpdatePreferredLanguageRequest
{
    public string Language { get; set; } = default!;
}
