using System.Net;
using System.Net.Http.Json;
using AI.DocumentAssistant.Infrastructure.Persistence;
using AI.DocumentAssistant.UnitTests.Infrastructure;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AI.DocumentAssistant.UnitTests.Integration;

public sealed class DocumentsSecurityAndValidationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public DocumentsSecurityAndValidationTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Theory]
    [InlineData("/api/documents")]
    [InlineData("/api/usage/me")]
    [InlineData("/api/document-folders/tree")]
    public async Task Protected_Endpoints_Should_Reject_Anonymous_Requests(string url)
    {
        var response = await _client.GetAsync(url);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Upload_Should_Reject_Unsupported_File_Extension()
    {
        var token = await TestAuthHelper.RegisterAndLoginAsync(_factory, _client);
        TestAuthHelper.SetBearerToken(_client, token);

        using var content = new MultipartFormDataContent();
        using var fileContent = new ByteArrayContent("not a supported document"u8.ToArray());
        content.Add(fileContent, "file", "malware.exe");

        var response = await _client.PostAsync("/api/documents/upload", content);
        var responseBody = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        responseBody.Should().Contain("Unsupported file type");
    }

    [Fact]
    public async Task Summarize_Should_Not_Allow_Access_To_Another_Users_Document()
    {
        var ownerEmail = TestAuthHelper.CreateUniqueEmail("owner");
        var ownerToken = await TestAuthHelper.RegisterAndLoginAsync(_factory, _client, ownerEmail);
        TestAuthHelper.SetBearerToken(_client, ownerToken);

        Guid ownerDocumentId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (_, ownerDocumentId) = await TestDataSeeder.SeedReadyDocumentAsync(
                db,
                ownerEmail,
                "Private owner document that must not leak to other users.");
        }

        var otherClient = _factory.CreateClient();
        var otherToken = await TestAuthHelper.RegisterAndLoginAsync(
            _factory,
            otherClient,
            TestAuthHelper.CreateUniqueEmail("other"));
        TestAuthHelper.SetBearerToken(otherClient, otherToken);

        var response = await otherClient.PostAsJsonAsync(
            $"/api/documents/{ownerDocumentId}/summarize",
            new { Language = "en" });
        var responseBody = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        responseBody.Should().Contain("Document not found");
    }

    [Fact]
    public async Task Compare_Should_Reject_Same_Document_On_Both_Sides()
    {
        var email = TestAuthHelper.CreateUniqueEmail("same-compare");
        var token = await TestAuthHelper.RegisterAndLoginAsync(_factory, _client, email);
        TestAuthHelper.SetBearerToken(_client, token);

        Guid documentId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (_, documentId) = await TestDataSeeder.SeedReadyDocumentAsync(
                db,
                email,
                "Comparison source document.");
        }

        var response = await _client.PostAsJsonAsync(
            $"/api/documents/{documentId}/compare",
            new { SecondDocumentId = documentId, Prompt = "Compare", Language = "en" });
        var responseBody = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        responseBody.Should().Contain("two different documents");
    }
}
