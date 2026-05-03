using System.Net.Http.Headers;
using System.Net.Http.Json;
using AI.DocumentAssistant.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AI.DocumentAssistant.UnitTests.Infrastructure;

public static class TestAuthHelper
{
    private const string DefaultPassword = "P@ssword123!";
    private const string DefaultConfirmationUrl = "http://localhost:5173/confirm-email";

    public static async Task<string> RegisterAndLoginAsync(
        CustomWebApplicationFactory factory,
        HttpClient client,
        string? email = null)
    {
        email ??= CreateUniqueEmail();

        await RegisterAsync(client, email, DefaultPassword);
        await ConfirmUserEmailAsync(factory, email);

        var loginResponse = await client.PostAsJsonAsync("/api/auth/login", new
        {
            Email = email,
            Password = DefaultPassword
        });

        if (!loginResponse.IsSuccessStatusCode)
        {
            var errorBody = await loginResponse.Content.ReadAsStringAsync();

            throw new HttpRequestException(
                $"Login failed for '{email}' with status {(int)loginResponse.StatusCode} ({loginResponse.StatusCode}). Body: {errorBody}");
        }

        var auth = await loginResponse.Content.ReadFromJsonAsync<AuthResponse>();

        auth.Should().NotBeNull();
        auth!.AccessToken.Should().NotBeNullOrWhiteSpace();
        auth.RefreshToken.Should().NotBeNullOrWhiteSpace();

        return auth.AccessToken;
    }

    public static async Task RegisterAsync(
        HttpClient client,
        string email,
        string password = DefaultPassword)
    {
        var registerResponse = await client.PostAsJsonAsync("/api/auth/register", new
        {
            Email = email,
            Password = password,
            ConfirmationUrl = DefaultConfirmationUrl,

            ConfirmPassword = password,
            DisplayName = "Test User",
            FirstName = "Test",
            LastName = "User",
            Language = "en",
            PreferredLanguage = "en",
            AcceptTerms = true,
            TermsAccepted = true
        });

        if (!registerResponse.IsSuccessStatusCode)
        {
            var errorBody = await registerResponse.Content.ReadAsStringAsync();

            throw new HttpRequestException(
                $"Register failed for '{email}' with status {(int)registerResponse.StatusCode} ({registerResponse.StatusCode}). Body: {errorBody}");
        }
    }

    public static async Task ConfirmUserEmailAsync(
        CustomWebApplicationFactory factory,
        string email)
    {
        using var scope = factory.Services.CreateScope();

        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var normalizedEmail = email.Trim().ToLowerInvariant();

        var user = await db.Users.SingleAsync(x => x.Email == normalizedEmail);

        user.EmailConfirmed = true;
        user.EmailConfirmedAtUtc = DateTime.UtcNow;
        user.EmailConfirmationTokenHash = null;
        user.EmailConfirmationTokenExpiresAtUtc = null;
        user.EmailConfirmationSentAtUtc = null;

        await db.SaveChangesAsync();
    }

    public static void SetBearerToken(HttpClient client, string token)
    {
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    public static string CreateUniqueEmail(string prefix = "user")
    {
        return $"{prefix}-{Guid.NewGuid():N}@test.local";
    }

    private sealed class AuthResponse
    {
        public string AccessToken { get; set; } = default!;
        public string RefreshToken { get; set; } = default!;
        public int ExpiresIn { get; set; }
    }
}