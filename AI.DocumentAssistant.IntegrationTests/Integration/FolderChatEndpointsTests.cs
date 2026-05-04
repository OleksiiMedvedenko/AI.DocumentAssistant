using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AI.DocumentAssistant.Infrastructure.Persistence;
using AI.DocumentAssistant.IntegrationTests.Infrastructure;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AI.DocumentAssistant.IntegrationTests.Integration;

public sealed class FolderChatEndpointsTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public FolderChatEndpointsTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task FolderChat_Should_Use_Documents_From_Child_Folders_And_Return_Sessions_And_Messages()
    {
        var email = TestAuthHelper.CreateUniqueEmail("folder-chat");
        var token = await TestAuthHelper.RegisterAndLoginAsync(_factory, _client, email);
        TestAuthHelper.SetBearerToken(_client, token);

        var parentFolderId = await CreateFolderAsync("Projects", "Projects");
        var childFolderId = await CreateFolderAsync("KSeF", "KSeF", parentFolderId);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await TestDataSeeder.SeedReadyDocumentAsync(
                db,
                email,
                "KSeF project document: invoice FS/42/2026 was issued for ACME and total is 999 PLN.",
                "ksef-invoice.txt",
                childFolderId);
        }

        var question = "What invoice is described in this folder?";
        var askResponse = await _client.PostAsJsonAsync($"/api/document-folders/{parentFolderId}/chat", new
        {
            Message = question,
            Language = "en"
        });
        var askBody = await askResponse.Content.ReadAsStringAsync();

        askResponse.StatusCode.Should().Be(HttpStatusCode.OK, $"response body was: {askBody}");

        using var askJson = JsonDocument.Parse(askBody);
        var sessionId = askJson.RootElement.GetProperty("chatSessionId").GetGuid();
        askJson.RootElement.GetProperty("answer").GetString().Should().StartWith("ANSWER::en::");

        var sessionsResponse = await _client.GetAsync($"/api/document-folders/{parentFolderId}/chat/sessions");
        var sessionsBody = await sessionsResponse.Content.ReadAsStringAsync();

        sessionsResponse.StatusCode.Should().Be(HttpStatusCode.OK, $"response body was: {sessionsBody}");

        using var sessionsJson = JsonDocument.Parse(sessionsBody);
        var session = sessionsJson.RootElement.EnumerateArray().Should().ContainSingle().Which;
        session.GetProperty("id").GetGuid().Should().Be(sessionId);
        session.GetProperty("documentId").GetGuid().Should().Be(Guid.Empty);
        session.GetProperty("messageCount").GetInt32().Should().Be(2);
        session.GetProperty("title").GetString().Should().Be(question);

        var messagesResponse = await _client.GetAsync($"/api/document-folders/{parentFolderId}/chat/sessions/{sessionId}/messages");
        var messagesBody = await messagesResponse.Content.ReadAsStringAsync();

        messagesResponse.StatusCode.Should().Be(HttpStatusCode.OK, $"response body was: {messagesBody}");

        using var messagesJson = JsonDocument.Parse(messagesBody);
        messagesJson.RootElement.EnumerateArray().Should().HaveCount(2);
        messagesBody.Should().Contain(question);
        messagesBody.Should().Contain("ANSWER::en::");
    }

    [Fact]
    public async Task FolderChat_Should_Reject_Empty_Folder()
    {
        var email = TestAuthHelper.CreateUniqueEmail("empty-folder-chat");
        var token = await TestAuthHelper.RegisterAndLoginAsync(_factory, _client, email);
        TestAuthHelper.SetBearerToken(_client, token);

        var folderId = await CreateFolderAsync("Empty", "Empty");

        var response = await _client.PostAsJsonAsync($"/api/document-folders/{folderId}/chat", new
        {
            Message = "What is inside?",
            Language = "en"
        });
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        body.Should().Contain("Folder does not contain processed documents");
    }

    private async Task<Guid> CreateFolderAsync(string name, string nameEn, Guid? parentFolderId = null)
    {
        var response = await _client.PostAsJsonAsync("/api/document-folders", new
        {
            ParentFolderId = parentFolderId,
            Name = name,
            NamePl = name,
            NameEn = nameEn,
            NameUa = name
        });
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK, $"response body was: {body}");

        using var json = JsonDocument.Parse(body);
        return json.RootElement.GetProperty("id").GetGuid();
    }
}
