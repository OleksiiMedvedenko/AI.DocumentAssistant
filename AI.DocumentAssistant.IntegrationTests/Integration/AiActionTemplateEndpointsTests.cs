using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AI.DocumentAssistant.IntegrationTests.Infrastructure;
using FluentAssertions;
using Xunit;

namespace AI.DocumentAssistant.IntegrationTests.Integration;

public sealed class AiActionTemplateEndpointsTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public AiActionTemplateEndpointsTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Ai_Action_Templates_Should_Be_Created_Listed_Updated_And_Deleted()
    {
        var token = await TestAuthHelper.RegisterAndLoginAsync(_factory, _client);
        TestAuthHelper.SetBearerToken(_client, token);

        var createResponse = await _client.PostAsJsonAsync("/api/ai-action-templates", new
        {
            Name = "Invoice extraction",
            DocumentType = "invoice",
            Prompt = "Extract invoice number, seller, buyer, total and payment due date.",
            OutputFormat = "json"
        });
        var createBody = await createResponse.Content.ReadAsStringAsync();

        createResponse.StatusCode.Should().Be(HttpStatusCode.OK, $"response body was: {createBody}");

        using var createJson = JsonDocument.Parse(createBody);
        var templateId = createJson.RootElement.GetProperty("id").GetGuid();

        var listResponse = await _client.GetAsync("/api/ai-action-templates?documentType=invoice");
        var listBody = await listResponse.Content.ReadAsStringAsync();
        listResponse.StatusCode.Should().Be(HttpStatusCode.OK, $"response body was: {listBody}");
        listBody.Should().Contain("Invoice extraction");

        var updateResponse = await _client.PutAsJsonAsync($"/api/ai-action-templates/{templateId}", new
        {
            Name = "Invoice audit",
            DocumentType = "invoice",
            Prompt = "Extract invoice data and highlight risks.",
            OutputFormat = "json"
        });
        var updateBody = await updateResponse.Content.ReadAsStringAsync();
        updateResponse.StatusCode.Should().Be(HttpStatusCode.OK, $"response body was: {updateBody}");
        updateBody.Should().Contain("Invoice audit");

        var deleteResponse = await _client.DeleteAsync($"/api/ai-action-templates/{templateId}");
        var deleteBody = await deleteResponse.Content.ReadAsStringAsync();
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent, $"response body was: {deleteBody}");
    }
}
