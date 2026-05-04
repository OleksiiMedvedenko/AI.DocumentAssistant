using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AI.DocumentAssistant.Infrastructure.Persistence;
using AI.DocumentAssistant.IntegrationTests.Infrastructure;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AI.DocumentAssistant.IntegrationTests.Integration;

public sealed class ChatSessionsEndpointsTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public ChatSessionsEndpointsTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Chat_Should_Create_Session_List_Messages_And_Continue_Existing_Session()
    {
        var email = TestAuthHelper.CreateUniqueEmail("chat-session");
        var token = await TestAuthHelper.RegisterAndLoginAsync(_factory, _client, email);
        TestAuthHelper.SetBearerToken(_client, token);

        Guid documentId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (_, documentId) = await TestDataSeeder.SeedReadyDocumentAsync(
                db,
                email,
                "The document describes authentication, refresh tokens, usage quotas and document processing.");
        }

        var firstQuestion = "What does this document say about authentication?";
        var firstResponse = await _client.PostAsJsonAsync($"/api/documents/{documentId}/chat", new
        {
            Message = firstQuestion,
            Language = "en"
        });
        var firstBody = await firstResponse.Content.ReadAsStringAsync();

        firstResponse.StatusCode.Should().Be(HttpStatusCode.OK, $"response body was: {firstBody}");

        using var firstJson = JsonDocument.Parse(firstBody);
        var sessionId = firstJson.RootElement.GetProperty("chatSessionId").GetGuid();
        firstJson.RootElement.GetProperty("answer").GetString().Should().StartWith("ANSWER::en::");

        var secondQuestion = "And what about usage quotas?";
        var secondResponse = await _client.PostAsJsonAsync($"/api/documents/{documentId}/chat", new
        {
            ChatSessionId = sessionId,
            Message = secondQuestion,
            Language = "en"
        });
        var secondBody = await secondResponse.Content.ReadAsStringAsync();

        secondResponse.StatusCode.Should().Be(HttpStatusCode.OK, $"response body was: {secondBody}");
        secondBody.Should().Contain(sessionId.ToString());

        var messagesResponse = await _client.GetAsync($"/api/documents/{documentId}/chat/messages?chatSessionId={sessionId}");
        var messagesBody = await messagesResponse.Content.ReadAsStringAsync();

        messagesResponse.StatusCode.Should().Be(HttpStatusCode.OK, $"response body was: {messagesBody}");

        using var messagesJson = JsonDocument.Parse(messagesBody);
        var messages = messagesJson.RootElement.EnumerateArray().ToList();
        messages.Should().HaveCount(4);
        messagesBody.Should().Contain(firstQuestion);
        messagesBody.Should().Contain(secondQuestion);
        messagesBody.Should().Contain("ANSWER::en::");

        var sessionsResponse = await _client.GetAsync($"/api/documents/{documentId}/chat/sessions");
        var sessionsBody = await sessionsResponse.Content.ReadAsStringAsync();

        sessionsResponse.StatusCode.Should().Be(HttpStatusCode.OK, $"response body was: {sessionsBody}");

        using var sessionsJson = JsonDocument.Parse(sessionsBody);
        var session = sessionsJson.RootElement.EnumerateArray().Should().ContainSingle().Which;
        session.GetProperty("id").GetGuid().Should().Be(sessionId);
        session.GetProperty("documentId").GetGuid().Should().Be(documentId);
        session.GetProperty("messageCount").GetInt32().Should().Be(4);
        session.GetProperty("title").GetString().Should().StartWith("What does this document say");
    }

    [Fact]
    public async Task Chat_Should_Reject_Unknown_Or_Cross_Document_Session()
    {
        var email = TestAuthHelper.CreateUniqueEmail("chat-wrong-session");
        var token = await TestAuthHelper.RegisterAndLoginAsync(_factory, _client, email);
        TestAuthHelper.SetBearerToken(_client, token);

        Guid firstDocumentId;
        Guid secondDocumentId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (_, firstDocumentId) = await TestDataSeeder.SeedReadyDocumentAsync(db, email, "First document.", "first.txt");
            (_, secondDocumentId) = await TestDataSeeder.SeedReadyDocumentAsync(db, email, "Second document.", "second.txt");
        }

        var firstResponse = await _client.PostAsJsonAsync($"/api/documents/{firstDocumentId}/chat", new
        {
            Message = "Create a session",
            Language = "en"
        });
        var firstBody = await firstResponse.Content.ReadAsStringAsync();

        firstResponse.StatusCode.Should().Be(HttpStatusCode.OK, $"response body was: {firstBody}");

        using var firstJson = JsonDocument.Parse(firstBody);
        var sessionId = firstJson.RootElement.GetProperty("chatSessionId").GetGuid();

        var response = await _client.PostAsJsonAsync($"/api/documents/{secondDocumentId}/chat", new
        {
            ChatSessionId = sessionId,
            Message = "Try to reuse the wrong session",
            Language = "en"
        });
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        body.Should().Contain("Chat session not found");
    }
}
