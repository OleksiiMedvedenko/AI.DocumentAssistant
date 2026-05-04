using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AI.DocumentAssistant.Infrastructure.Persistence;
using AI.DocumentAssistant.IntegrationTests.Infrastructure;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AI.DocumentAssistant.IntegrationTests.Integration;

public sealed class UsageEndpointsTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public UsageEndpointsTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Usage_Should_Return_Default_Limits_For_New_User()
    {
        var token = await TestAuthHelper.RegisterAndLoginAsync(_factory, _client);
        TestAuthHelper.SetBearerToken(_client, token);

        var response = await _client.GetAsync("/api/usage/me");
        var responseBody = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK, $"response body was: {responseBody}");

        using var json = JsonDocument.Parse(responseBody);
        var root = json.RootElement;

        root.GetProperty("hasUnlimitedAiUsage").GetBoolean().Should().BeFalse();
        root.GetProperty("chatMessages").GetProperty("limit").GetInt32().Should().Be(100);
        root.GetProperty("documentUploads").GetProperty("limit").GetInt32().Should().Be(30);
        root.GetProperty("summarizations").GetProperty("used").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task Usage_Should_Increase_After_Summarization()
    {
        var email = TestAuthHelper.CreateUniqueEmail("usage-summary");
        var token = await TestAuthHelper.RegisterAndLoginAsync(_factory, _client, email);
        TestAuthHelper.SetBearerToken(_client, token);

        Guid documentId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (_, documentId) = await TestDataSeeder.SeedReadyDocumentAsync(
                db,
                email,
                "Usage tracking document content.");
        }

        var summarizeResponse = await _client.PostAsJsonAsync(
            $"/api/documents/{documentId}/summarize",
            new { Language = "en" });
        summarizeResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var usageResponse = await _client.GetAsync("/api/usage/me");
        var usageBody = await usageResponse.Content.ReadAsStringAsync();

        usageResponse.StatusCode.Should().Be(HttpStatusCode.OK, $"response body was: {usageBody}");

        using var json = JsonDocument.Parse(usageBody);
        json.RootElement.GetProperty("summarizations").GetProperty("used").GetInt32().Should().Be(1);
    }
}
