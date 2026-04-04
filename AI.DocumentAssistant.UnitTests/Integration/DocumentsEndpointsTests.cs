using AI.DocumentAssistant.Infrastructure.Persistence;
using AI.DocumentAssistant.UnitTests.Infrastructure;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace AI.DocumentAssistant.UnitTests.Integration;

public sealed class DocumentsEndpointsTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public DocumentsEndpointsTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Upload_Should_Return_Ok_For_Supported_File()
    {
        var token = await TestAuthHelper.RegisterAndLoginAsync(_client);
        TestAuthHelper.SetBearerToken(_client, token);

        using var content = new MultipartFormDataContent();
        using var fileContent = new ByteArrayContent("hello from test file"u8.ToArray());
        content.Add(fileContent, "file", "sample.txt");

        var response = await _client.PostAsync("/api/documents/upload", content);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("sample.txt");
    }

    [Fact]
    public async Task Summarize_Should_Return_Summary_For_Ready_Document()
    {
        var email = $"sum_{Guid.NewGuid():N}@test.local";
        var token = await TestAuthHelper.RegisterAndLoginAsync(_client, email);
        TestAuthHelper.SetBearerToken(_client, token);

        Guid documentId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (_, documentId) = await TestDataSeeder.SeedReadyDocumentAsync(
                db,
                email,
                "This is a very important document about architecture and testing.");
        }

        var response = await _client.PostAsync($"/api/documents/{documentId}/summarize", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("SUMMARY::");
    }

    [Fact]
    public async Task Extract_Should_Save_And_Return_Json()
    {
        var email = $"extract_{Guid.NewGuid():N}@test.local";
        var token = await TestAuthHelper.RegisterAndLoginAsync(_client, email);
        TestAuthHelper.SetBearerToken(_client, token);

        Guid documentId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (_, documentId) = await TestDataSeeder.SeedReadyDocumentAsync(
                db,
                email,
                "Name: John Doe Email: john@example.com Skills: C#, SQL, Azure");
        }

        var response = await _client.PostAsJsonAsync($"/api/documents/{documentId}/extract", new
        {
            ExtractionType = "generic",
            Fields = new[] { "name", "email", "skills" }
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("\"extractionType\":\"generic\"");
    }
}