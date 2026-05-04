using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AI.DocumentAssistant.Infrastructure.Persistence;
using AI.DocumentAssistant.IntegrationTests.Infrastructure;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AI.DocumentAssistant.IntegrationTests.Integration;

public sealed class DocumentLifecycleEndpointsTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public DocumentLifecycleEndpointsTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task GetAll_Should_Filter_By_Folder_And_Move_Document_Between_Folders()
    {
        var email = TestAuthHelper.CreateUniqueEmail("document-lifecycle");
        var token = await TestAuthHelper.RegisterAndLoginAsync(_factory, _client, email);
        TestAuthHelper.SetBearerToken(_client, token);

        var sourceFolderId = await CreateFolderAsync("Source", "Source");
        var targetFolderId = await CreateFolderAsync("Target", "Target");

        Guid looseDocumentId;
        Guid folderDocumentId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (_, looseDocumentId) = await TestDataSeeder.SeedReadyDocumentAsync(
                db,
                email,
                "Loose document content.",
                "loose.txt");

            (_, folderDocumentId) = await TestDataSeeder.SeedReadyDocumentAsync(
                db,
                email,
                "Source folder document content.",
                "folder-doc.txt",
                sourceFolderId);
        }

        var sourceListResponse = await _client.GetAsync($"/api/documents?folderId={sourceFolderId}");
        var sourceListBody = await sourceListResponse.Content.ReadAsStringAsync();

        sourceListResponse.StatusCode.Should().Be(HttpStatusCode.OK, $"response body was: {sourceListBody}");
        sourceListBody.Should().Contain(folderDocumentId.ToString());
        sourceListBody.Should().NotContain(looseDocumentId.ToString());

        var moveResponse = await _client.PatchAsJsonAsync($"/api/documents/{looseDocumentId}/folder", new
        {
            FolderId = targetFolderId
        });
        var moveBody = await moveResponse.Content.ReadAsStringAsync();

        moveResponse.StatusCode.Should().Be(HttpStatusCode.OK, $"response body was: {moveBody}");
        moveBody.Should().Contain(targetFolderId.ToString());

        var targetListResponse = await _client.GetAsync($"/api/documents?folderId={targetFolderId}");
        var targetListBody = await targetListResponse.Content.ReadAsStringAsync();

        targetListResponse.StatusCode.Should().Be(HttpStatusCode.OK, $"response body was: {targetListBody}");
        targetListBody.Should().Contain(looseDocumentId.ToString());
        targetListBody.Should().NotContain(folderDocumentId.ToString());

        var removeFolderResponse = await _client.PatchAsJsonAsync($"/api/documents/{looseDocumentId}/folder", new
        {
            FolderId = (Guid?)null
        });
        var removeFolderBody = await removeFolderResponse.Content.ReadAsStringAsync();

        removeFolderResponse.StatusCode.Should().Be(HttpStatusCode.OK, $"response body was: {removeFolderBody}");

        using var json = JsonDocument.Parse(removeFolderBody);
        json.RootElement.GetProperty("folderId").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task Delete_Should_Remove_Document_And_Block_Later_Access()
    {
        var email = TestAuthHelper.CreateUniqueEmail("delete-doc");
        var token = await TestAuthHelper.RegisterAndLoginAsync(_factory, _client, email);
        TestAuthHelper.SetBearerToken(_client, token);

        Guid documentId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (_, documentId) = await TestDataSeeder.SeedReadyDocumentAsync(
                db,
                email,
                "Document that will be deleted.",
                "delete-me.txt");
        }

        var deleteResponse = await _client.DeleteAsync($"/api/documents/{documentId}");
        var deleteBody = await deleteResponse.Content.ReadAsStringAsync();

        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent, $"response body was: {deleteBody}");

        var getResponse = await _client.GetAsync($"/api/documents/{documentId}");
        var getBody = await getResponse.Content.ReadAsStringAsync();

        getResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
        getBody.Should().Contain("Document not found");
    }

    [Fact]
    public async Task Extractions_Should_Be_Listed_After_Extraction()
    {
        var email = TestAuthHelper.CreateUniqueEmail("extract-lifecycle");
        var token = await TestAuthHelper.RegisterAndLoginAsync(_factory, _client, email);
        TestAuthHelper.SetBearerToken(_client, token);

        Guid documentId;

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            (_, documentId) = await TestDataSeeder.SeedReadyDocumentAsync(
                db,
                email,
                "Invoice FS/1/2026 Total: 123.45 PLN Seller: Test Company");
        }

        var extractResponse = await _client.PostAsJsonAsync(
            $"/api/documents/{documentId}/extract",
            new
            {
                ExtractionType = "invoice",
                Fields = new[] { "number", "total", "seller" },
                Language = "en"
            });

        var extractBody = await extractResponse.Content.ReadAsStringAsync();

        extractResponse.StatusCode.Should().Be(
            HttpStatusCode.OK,
            $"response body was: {extractBody}");

        var extraction = await extractResponse.Content.ReadFromJsonAsync<ExtractResponseDto>();

        extraction.Should().NotBeNull();
        extraction!.Id.Should().NotBeEmpty();
        extraction.DocumentId.Should().Be(documentId);
        extraction.ExtractionType.Should().Be("invoice");
        extraction.JsonResult.Should().Contain("\"extractionType\":\"invoice\"");
        extraction.JsonResult.Should().Contain("\"language\":\"en\"");

        var listResponse = await _client.GetAsync($"/api/documents/{documentId}/extractions");
        var listBody = await listResponse.Content.ReadAsStringAsync();

        listResponse.StatusCode.Should().Be(
            HttpStatusCode.OK,
            $"response body was: {listBody}");

        var list = await listResponse.Content.ReadFromJsonAsync<List<ExtractResponseDto>>();

        list.Should().NotBeNull();
        list.Should().ContainSingle(x => x.Id == extraction.Id);

        var listedExtraction = list!.Single(x => x.Id == extraction.Id);

        listedExtraction.DocumentId.Should().Be(documentId);
        listedExtraction.ExtractionType.Should().Be("invoice");
        listedExtraction.JsonResult.Should().Contain("\"extractionType\":\"invoice\"");
        listedExtraction.JsonResult.Should().Contain("\"language\":\"en\"");
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

    private sealed class ExtractResponseDto
    {
        public Guid Id { get; set; }
        public Guid DocumentId { get; set; }
        public string ExtractionType { get; set; } = default!;
        public string JsonResult { get; set; } = default!;
        public DateTime CreatedAtUtc { get; set; }
    }
}
