using System.Net;
using System.Net.Http.Json;
using AI.DocumentAssistant.Infrastructure.Persistence;
using AI.DocumentAssistant.UnitTests.Infrastructure;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AI.DocumentAssistant.UnitTests.Integration;

public sealed class ChatAndCompareEndpointsTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public ChatAndCompareEndpointsTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Chat_Should_Return_Answer_And_Create_Session_For_Ready_Document()
    {
        var email = $"chat_{Guid.NewGuid():N}@test.local";
        var token = await TestAuthHelper.RegisterAndLoginAsync(_client, email);
        TestAuthHelper.SetBearerToken(_client, token);

        Guid documentId;

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (_, documentId) = await TestDataSeeder.SeedReadyDocumentAsync(
                db,
                email,
                "System architecture includes authentication, document upload, extraction, and chat.");
        }

        var response = await _client.PostAsJsonAsync(
            $"/api/documents/{documentId}/chat",
            new
            {
                Message = "What does this document say about authentication?",
                Language = "en"
            });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<AskDocumentResponseDto>();
        body.Should().NotBeNull();
        body!.ChatSessionId.Should().NotBeEmpty();
        body.Answer.Should().StartWith("ANSWER::en::");
    }

    [Fact]
    public async Task Compare_Should_Return_Language_Aware_Comparison_Result()
    {
        var email = $"compare_{Guid.NewGuid():N}@test.local";
        var token = await TestAuthHelper.RegisterAndLoginAsync(_client, email);
        TestAuthHelper.SetBearerToken(_client, token);

        Guid firstDocumentId;
        Guid secondDocumentId;

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            (_, firstDocumentId) = await TestDataSeeder.SeedReadyDocumentAsync(
                db,
                email,
                "Document A says the candidate has C#, .NET and Azure skills.",
                "first.txt");

            (_, secondDocumentId) = await TestDataSeeder.SeedReadyDocumentAsync(
                db,
                email,
                "Document B says the job requires .NET, Azure, SQL and communication.",
                "second.txt");
        }

        var response = await _client.PostAsJsonAsync(
            $"/api/documents/{firstDocumentId}/compare",
            new
            {
                SecondDocumentId = secondDocumentId,
                Prompt = "Compare candidate profile with job requirements.",
                Language = "ua"
            });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<CompareResponseDto>();
        body.Should().NotBeNull();
        body!.FirstDocumentId.Should().Be(firstDocumentId);
        body.SecondDocumentId.Should().Be(secondDocumentId);
        body.Result.Should().StartWith("COMPARE::ua::");
    }

    private sealed class AskDocumentResponseDto
    {
        public Guid ChatSessionId { get; set; }
        public string Answer { get; set; } = default!;
    }

    private sealed class CompareResponseDto
    {
        public Guid FirstDocumentId { get; set; }
        public Guid SecondDocumentId { get; set; }
        public string Result { get; set; } = default!;
    }
}