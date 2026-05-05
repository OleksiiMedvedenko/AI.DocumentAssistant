using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AI.DocumentAssistant.Domain.Entities;
using AI.DocumentAssistant.Infrastructure.Persistence;
using AI.DocumentAssistant.IntegrationTests.Infrastructure;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AI.DocumentAssistant.IntegrationTests.Integration;

public sealed class SmartFolderAssistantEndpointsTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public SmartFolderAssistantEndpointsTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Folder_Suggestions_Should_Be_Listed_And_Accepted()
    {
        var email = TestAuthHelper.CreateUniqueEmail("smart-folders");
        var token = await TestAuthHelper.RegisterAndLoginAsync(_factory, _client, email);
        TestAuthHelper.SetBearerToken(_client, token);

        Guid documentId;
        Guid folderId;
        Guid suggestionId;

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var (userId, seededDocumentId) = await TestDataSeeder.SeedReadyDocumentAsync(
                db,
                email,
                "Invoice FS/2026 total VAT seller buyer payment due.",
                "invoice.txt");

            documentId = seededDocumentId;

            var folder = new DocumentFolder
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Key = "invoices",
                Name = "Invoices",
                NamePl = "Faktury",
                NameEn = "Invoices",
                NameUa = "Рахунки",
                IsSystemGenerated = false,
                CreatedAtUtc = DateTime.UtcNow
            };

            db.DocumentFolders.Add(folder);
            folderId = folder.Id;

            var suggestion = new DocumentFolderSuggestion
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                DocumentId = documentId,
                ExistingFolderId = folder.Id,
                ProposedKey = folder.Key,
                ProposedName = folder.Name,
                ProposedNamePl = folder.NamePl,
                ProposedNameEn = folder.NameEn,
                ProposedNameUa = folder.NameUa,
                Score = 0.91m,
                Rank = 1,
                Reason = "Invoice indicators detected.",
                Status = "pending",
                CreatedAtUtc = DateTime.UtcNow
            };

            db.DocumentFolderSuggestions.Add(suggestion);
            suggestionId = suggestion.Id;
            await db.SaveChangesAsync();
        }

        var listResponse = await _client.GetAsync($"/api/documents/{documentId}/folder-suggestions");
        var listBody = await listResponse.Content.ReadAsStringAsync();

        listResponse.StatusCode.Should().Be(HttpStatusCode.OK, $"response body was: {listBody}");
        listBody.Should().Contain(suggestionId.ToString());
        listBody.Should().Contain("Invoice indicators detected");

        var acceptResponse = await _client.PostAsync($"/api/documents/{documentId}/folder-suggestions/{suggestionId}/accept", null);
        var acceptBody = await acceptResponse.Content.ReadAsStringAsync();

        acceptResponse.StatusCode.Should().Be(HttpStatusCode.OK, $"response body was: {acceptBody}");
        acceptBody.Should().Contain(folderId.ToString());
        acceptBody.Should().Contain("accepted-suggestion");
    }

    [Fact]
    public async Task Dashboard_Inbox_And_Related_Documents_Should_Return_User_Workspace_State()
    {
        var email = TestAuthHelper.CreateUniqueEmail("workspace");
        var token = await TestAuthHelper.RegisterAndLoginAsync(_factory, _client, email);
        TestAuthHelper.SetBearerToken(_client, token);

        Guid sourceDocumentId;

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            (_, sourceDocumentId) = await TestDataSeeder.SeedReadyDocumentAsync(
                db,
                email,
                "KSeF API documentation endpoints authentication integration guide.",
                "ksef-api-docs.txt");

            await TestDataSeeder.SeedReadyDocumentAsync(
                db,
                email,
                "OpenAPI REST API documentation endpoints and authentication guide.",
                "openapi-docs.txt");
        }

        var dashboardResponse = await _client.GetAsync("/api/documents/dashboard");
        var dashboardBody = await dashboardResponse.Content.ReadAsStringAsync();
        dashboardResponse.StatusCode.Should().Be(HttpStatusCode.OK, $"response body was: {dashboardBody}");
        dashboardBody.Should().Contain("totalDocuments");

        var inboxResponse = await _client.GetAsync("/api/documents/inbox");
        var inboxBody = await inboxResponse.Content.ReadAsStringAsync();
        inboxResponse.StatusCode.Should().Be(HttpStatusCode.OK, $"response body was: {inboxBody}");

        var relatedResponse = await _client.GetAsync($"/api/documents/{sourceDocumentId}/related");
        var relatedBody = await relatedResponse.Content.ReadAsStringAsync();
        relatedResponse.StatusCode.Should().Be(HttpStatusCode.OK, $"response body was: {relatedBody}");
        relatedBody.Should().Contain("openapi-docs.txt");
    }

    [Fact]
    public async Task Duplicate_Folder_Suggestions_Should_Detect_Similar_Sibling_Folders()
    {
        var token = await TestAuthHelper.RegisterAndLoginAsync(_factory, _client);
        TestAuthHelper.SetBearerToken(_client, token);

        await CreateFolderAsync("Invoices", "Invoices");
        await CreateFolderAsync("Invoice", "Invoice");

        var response = await _client.GetAsync("/api/document-folders/duplicate-suggestions");
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK, $"response body was: {body}");
        body.Should().Contain("Invoices");
        body.Should().Contain("Invoice");
    }

    private async Task<Guid> CreateFolderAsync(string name, string nameEn)
    {
        var response = await _client.PostAsJsonAsync("/api/document-folders", new
        {
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
