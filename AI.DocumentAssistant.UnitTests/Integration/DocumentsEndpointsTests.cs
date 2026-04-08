using System.Net;
using System.Net.Http.Json;
using AI.DocumentAssistant.Infrastructure.Persistence;
using AI.DocumentAssistant.UnitTests.Infrastructure;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
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
    public async Task Upload_Should_Return_Metadata_For_Supported_File()
    {
        var token = await TestAuthHelper.RegisterAndLoginAsync(_client);
        TestAuthHelper.SetBearerToken(_client, token);

        using var content = new MultipartFormDataContent();
        using var fileContent = new ByteArrayContent("hello from test file"u8.ToArray());

        content.Add(fileContent, "file", "sample.txt");

        var response = await _client.PostAsync("/api/documents/upload", content);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<UploadDocumentResponseDto>();
        body.Should().NotBeNull();
        body!.OriginalFileName.Should().Be("sample.txt");
        body.Id.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Summarize_Should_Return_Language_Aware_Summary_For_Ready_Document()
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
                "This document describes architecture, authentication, and automated testing.");
        }

        var response = await _client.PostAsJsonAsync(
            $"/api/documents/{documentId}/summarize",
            new { Language = "en" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<SummarizeResponseDto>();
        body.Should().NotBeNull();
        body!.DocumentId.Should().Be(documentId);
        body.Summary.Should().StartWith("SUMMARY::en::");
    }

    [Fact]
    public async Task Extract_Should_Save_And_Return_Json_With_Language()
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

        var response = await _client.PostAsJsonAsync(
            $"/api/documents/{documentId}/extract",
            new
            {
                ExtractionType = "generic",
                Fields = new[] { "name", "email", "skills" },
                Language = "pl"
            });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<ExtractResponseDto>();
        body.Should().NotBeNull();
        body!.DocumentId.Should().Be(documentId);
        body.JsonResult.Should().Contain("\"extractionType\":\"generic\"");
        body.JsonResult.Should().Contain("\"language\":\"pl\"");
    }

    private sealed class UploadDocumentResponseDto
    {
        public Guid Id { get; set; }
        public string OriginalFileName { get; set; } = default!;
    }

    private sealed class SummarizeResponseDto
    {
        public Guid DocumentId { get; set; }
        public string Summary { get; set; } = default!;
    }

    private sealed class ExtractResponseDto
    {
        public Guid Id { get; set; }
        public Guid DocumentId { get; set; }
        public string JsonResult { get; set; } = default!;
    }
}