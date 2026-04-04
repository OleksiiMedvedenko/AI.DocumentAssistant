using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace AI.DocumentAssistant.UnitTests.Infrastructure;

public static class TestAuthHelper
{
    public static async Task<string> RegisterAndLoginAsync(HttpClient client, string? email = null)
    {
        email ??= $"user_{Guid.NewGuid():N}@test.local";
        var password = "P@ssword123!";

        var registerResponse = await client.PostAsJsonAsync("/api/auth/register", new
        {
            Email = email,
            Password = password
        });

        registerResponse.EnsureSuccessStatusCode();

        var loginResponse = await client.PostAsJsonAsync("/api/auth/login", new
        {
            Email = email,
            Password = password
        });

        loginResponse.EnsureSuccessStatusCode();

        var auth = await loginResponse.Content.ReadFromJsonAsync<AuthResponse>();
        return auth!.AccessToken;
    }

    public static void SetBearerToken(HttpClient client, string token)
    {
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    private sealed class AuthResponse
    {
        public string AccessToken { get; set; } = default!;
        public string RefreshToken { get; set; } = default!;
        public int ExpiresIn { get; set; }
    }
}