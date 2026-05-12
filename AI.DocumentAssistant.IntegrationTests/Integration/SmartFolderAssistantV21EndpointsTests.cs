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

public sealed class SmartFolderAssistantV21EndpointsTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public SmartFolderAssistantV21EndpointsTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Intelligence_And_Regenerate_Should_Return_Formal_Document_Model_And_Suggestions()
    {
        var email = TestAuthHelper.CreateUniqueEmail("smart-v21");
        var token = await TestAuthHelper.RegisterAndLoginAsync(_factory, _client, email);
        TestAuthHelper.SetBearerToken(_client, token);

        Guid documentId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var (userId, seededDocumentId) = await TestDataSeeder.SeedReadyDocumentAsync(
                db,
                email,
                "Invoice FV/2026/05 Total: 123.45 PLN VAT seller buyer payment due 2026-05-30 KSeF.",
                "invoice-ksef-2026.txt");

            documentId = seededDocumentId;
            db.DocumentFolders.Add(new DocumentFolder
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Key = "finance-invoices",
                Name = "Finance Invoices",
                NamePl = "Faktury finansowe",
                NameEn = "Finance Invoices",
                NameUa = "Фінансові рахунки",
                CreatedAtUtc = DateTime.UtcNow
            });

            await db.SaveChangesAsync();
        }

        var intelligenceResponse = await _client.GetAsync($"/api/documents/{documentId}/intelligence");
        var intelligenceBody = await intelligenceResponse.Content.ReadAsStringAsync();

        intelligenceResponse.StatusCode.Should().Be(HttpStatusCode.OK, $"response body was: {intelligenceBody}");
        intelligenceBody.Should().Contain("invoice");
        intelligenceBody.Should().Contain("finance");
        intelligenceBody.Should().Contain("ksef");

        var regenerateResponse = await _client.PostAsync($"/api/documents/{documentId}/folder-suggestions/regenerate", null);
        var regenerateBody = await regenerateResponse.Content.ReadAsStringAsync();

        regenerateResponse.StatusCode.Should().Be(HttpStatusCode.OK, $"response body was: {regenerateBody}");
        regenerateBody.Should().Contain("suggestions");
        regenerateBody.Should().Contain("finalScore");
        regenerateBody.Should().MatchRegex("Finance Invoices|Faktury|Invoices|finance-invoices|faktury");
    }

    [Fact]
    public async Task Merge_Duplicate_Folders_Should_Move_Documents_And_Delete_Source()
    {
        var email = TestAuthHelper.CreateUniqueEmail("merge-folders");
        var token = await TestAuthHelper.RegisterAndLoginAsync(_factory, _client, email);
        TestAuthHelper.SetBearerToken(_client, token);

        Guid sourceFolderId;
        Guid targetFolderId;
        Guid documentId;

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var (userId, _) = await TestDataSeeder.SeedReadyDocumentAsync(db, email, "bootstrap", "bootstrap.txt");

            var source = new DocumentFolder
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Key = "invoice",
                Name = "Invoice",
                NamePl = "Faktura",
                NameEn = "Invoice",
                NameUa = "Рахунок",
                CreatedAtUtc = DateTime.UtcNow
            };

            var target = new DocumentFolder
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Key = "invoices",
                Name = "Invoices",
                NamePl = "Faktury",
                NameEn = "Invoices",
                NameUa = "Рахунки",
                CreatedAtUtc = DateTime.UtcNow
            };

            db.DocumentFolders.AddRange(source, target);
            await db.SaveChangesAsync();

            sourceFolderId = source.Id;
            targetFolderId = target.Id;
            (_, documentId) = await TestDataSeeder.SeedReadyDocumentAsync(
                db,
                email,
                "Invoice document assigned to duplicate folder.",
                "invoice-doc.txt",
                sourceFolderId);
        }

        var mergeResponse = await _client.PostAsJsonAsync($"/api/document-folders/{sourceFolderId}/merge", new
        {
            TargetFolderId = targetFolderId,
            DeleteSourceFolder = true
        });
        var mergeBody = await mergeResponse.Content.ReadAsStringAsync();

        mergeResponse.StatusCode.Should().Be(HttpStatusCode.OK, $"response body was: {mergeBody}");
        mergeBody.Should().Contain("movedDocuments");

        var documentResponse = await _client.GetAsync($"/api/documents/{documentId}");
        var documentBody = await documentResponse.Content.ReadAsStringAsync();

        documentResponse.StatusCode.Should().Be(HttpStatusCode.OK, $"response body was: {documentBody}");
        documentBody.Should().Contain(targetFolderId.ToString());

        var treeResponse = await _client.GetAsync("/api/document-folders/tree");
        var treeBody = await treeResponse.Content.ReadAsStringAsync();

        treeResponse.StatusCode.Should().Be(HttpStatusCode.OK, $"response body was: {treeBody}");
        treeBody.Should().NotContain(sourceFolderId.ToString());
        treeBody.Should().Contain(targetFolderId.ToString());
    }
}
