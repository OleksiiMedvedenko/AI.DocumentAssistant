using System.Net;
using System.Net.Http.Json;
using AI.DocumentAssistant.IntegrationTests.Infrastructure;
using FluentAssertions;
using Xunit;

namespace AI.DocumentAssistant.IntegrationTests.Integration;

public sealed class AuthRefreshEndpointsTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public AuthRefreshEndpointsTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Refresh_Should_Return_New_Token_Pair_And_Revoke_Previous_Refresh_Token()
    {
        var email = TestAuthHelper.CreateUniqueEmail("refresh");
        var password = "P@ssword123!";

        await TestAuthHelper.RegisterAsync(_client, email, password);
        await TestAuthHelper.ConfirmUserEmailAsync(_factory, email);

        var loginResponse = await _client.PostAsJsonAsync("/api/auth/login", new
        {
            Email = email,
            Password = password
        });
        var loginBody = await loginResponse.Content.ReadAsStringAsync();

        loginResponse.StatusCode.Should().Be(HttpStatusCode.OK, $"login response body was: {loginBody}");

        var login = await loginResponse.Content.ReadFromJsonAsync<AuthResponseDto>();
        login.Should().NotBeNull();
        login!.AccessToken.Should().NotBeNullOrWhiteSpace();
        login.RefreshToken.Should().NotBeNullOrWhiteSpace();

        var refreshResponse = await _client.PostAsJsonAsync("/api/auth/refresh", new
        {
            RefreshToken = login.RefreshToken
        });
        var refreshBody = await refreshResponse.Content.ReadAsStringAsync();

        refreshResponse.StatusCode.Should().Be(HttpStatusCode.OK, $"refresh response body was: {refreshBody}");

        var refreshed = await refreshResponse.Content.ReadFromJsonAsync<AuthResponseDto>();
        refreshed.Should().NotBeNull();
        refreshed!.AccessToken.Should().NotBeNullOrWhiteSpace();
        refreshed.RefreshToken.Should().NotBeNullOrWhiteSpace();
        refreshed.RefreshToken.Should().NotBe(login.RefreshToken);

        var reusedResponse = await _client.PostAsJsonAsync("/api/auth/refresh", new
        {
            RefreshToken = login.RefreshToken
        });
        var reusedBody = await reusedResponse.Content.ReadAsStringAsync();

        reusedResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        reusedBody.Should().Contain("AUTH_INVALID_REFRESH_TOKEN");
    }

    [Fact]
    public async Task Refresh_Should_Reject_Missing_Or_Unknown_Refresh_Token()
    {
        var missingResponse = await _client.PostAsJsonAsync("/api/auth/refresh", new
        {
            RefreshToken = string.Empty
        });
        var missingBody = await missingResponse.Content.ReadAsStringAsync();

        missingResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        missingBody.Should().Contain("AUTH_INVALID_REFRESH_TOKEN");

        var unknownResponse = await _client.PostAsJsonAsync("/api/auth/refresh", new
        {
            RefreshToken = $"missing-{Guid.NewGuid():N}"
        });
        var unknownBody = await unknownResponse.Content.ReadAsStringAsync();

        unknownResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        unknownBody.Should().Contain("AUTH_INVALID_REFRESH_TOKEN");
    }

    private sealed class AuthResponseDto
    {
        public string AccessToken { get; set; } = default!;
        public string RefreshToken { get; set; } = default!;
        public int ExpiresIn { get; set; }
    }
}
