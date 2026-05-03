using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AI.DocumentAssistant.Infrastructure.Persistence;
using AI.DocumentAssistant.UnitTests.Infrastructure;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AI.DocumentAssistant.UnitTests.Integration;

public sealed class DocumentFoldersEndpointsTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public DocumentFoldersEndpointsTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Create_Should_Normalize_Key_And_Return_Folder()
    {
        var token = await TestAuthHelper.RegisterAndLoginAsync(_factory, _client);
        TestAuthHelper.SetBearerToken(_client, token);

        var response = await _client.PostAsJsonAsync("/api/document-folders", new
        {
            Name = "  Faktury 2026  ",
            NamePl = "Faktury 2026",
            NameEn = "Invoices 2026",
            NameUa = "Рахунки 2026"
        });
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK, $"response body was: {body}");

        using var json = JsonDocument.Parse(body);
        json.RootElement.GetProperty("id").GetGuid().Should().NotBeEmpty();
        json.RootElement.GetProperty("key").GetString().Should().Be("invoices-2026");
        json.RootElement.GetProperty("name").GetString().Should().Be("Faktury 2026");
    }

    [Fact]
    public async Task Create_Should_Reject_Duplicate_Folder_Key_In_Same_Parent()
    {
        var token = await TestAuthHelper.RegisterAndLoginAsync(_factory, _client);
        TestAuthHelper.SetBearerToken(_client, token);

        var payload = new
        {
            Name = "Contracts",
            NamePl = "Umowy",
            NameEn = "Contracts",
            NameUa = "Договори"
        };

        var firstResponse = await _client.PostAsJsonAsync("/api/document-folders", payload);
        firstResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var secondResponse = await _client.PostAsJsonAsync("/api/document-folders", payload);
        var secondBody = await secondResponse.Content.ReadAsStringAsync();

        secondResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        secondBody.Should().Contain("same key");
    }

    [Fact]
    public async Task Delete_Should_Reject_Folder_That_Contains_Documents()
    {
        var email = TestAuthHelper.CreateUniqueEmail("folder-doc");
        var token = await TestAuthHelper.RegisterAndLoginAsync(_factory, _client, email);
        TestAuthHelper.SetBearerToken(_client, token);

        var createResponse = await _client.PostAsJsonAsync("/api/document-folders", new
        {
            Name = "Invoices",
            NamePl = "Faktury",
            NameEn = "Invoices",
            NameUa = "Рахунки"
        });
        var createBody = await createResponse.Content.ReadAsStringAsync();
        createResponse.StatusCode.Should().Be(HttpStatusCode.OK, $"response body was: {createBody}");

        using var json = JsonDocument.Parse(createBody);
        var folderId = json.RootElement.GetProperty("id").GetGuid();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await TestDataSeeder.SeedReadyDocumentAsync(
                db,
                email,
                "Invoice document content.",
                "invoice.txt",
                folderId);
        }

        var deleteResponse = await _client.DeleteAsync($"/api/document-folders/{folderId}");
        var deleteBody = await deleteResponse.Content.ReadAsStringAsync();

        deleteResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        deleteBody.Should().Contain("still contains documents");
    }
}
