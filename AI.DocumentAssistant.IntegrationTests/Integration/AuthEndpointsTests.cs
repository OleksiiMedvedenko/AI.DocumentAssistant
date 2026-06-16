using System.Net;
using System.Net.Http.Json;
using AI.DocumentAssistant.IntegrationTests.Infrastructure;
using FluentAssertions;
using Xunit;

namespace AI.DocumentAssistant.IntegrationTests.Integration;

public sealed class AuthEndpointsTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public AuthEndpointsTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Register_And_Login_Should_Return_AccessToken_And_RefreshToken()
    {
        var email = TestAuthHelper.CreateUniqueEmail("auth");
        var password = "P@ssword123!";

        await TestAuthHelper.RegisterAsync(_client, email, password);
        await TestAuthHelper.ConfirmUserEmailAsync(_factory, email);

        var loginResponse = await _client.PostAsJsonAsync("/api/auth/login", new
        {
            Email = email,
            Password = password
        });

        var loginBody = await loginResponse.Content.ReadAsStringAsync();

        loginResponse.StatusCode.Should().Be(
            HttpStatusCode.OK,
            $"login response body was: {loginBody}");

        var body = await loginResponse.Content.ReadFromJsonAsync<AuthResponseDto>();

        body.Should().NotBeNull();
        body!.AccessToken.Should().NotBeNullOrWhiteSpace();
        body.RefreshToken.Should().NotBeNullOrWhiteSpace();
        body.ExpiresIn.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Me_Should_Return_Current_User_When_Authorized()
    {
        var token = await TestAuthHelper.RegisterAndLoginAsync(_factory, _client);
        TestAuthHelper.SetBearerToken(_client, token);

        var response = await _client.GetAsync("/api/auth/me");
        var responseBody = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(
            HttpStatusCode.OK,
            $"response body was: {responseBody}");

        var body = await response.Content.ReadFromJsonAsync<CurrentUserDto>();

        body.Should().NotBeNull();
        body!.Email.Should().Contain("@test.local");
        body.Id.Should().NotBeEmpty();
    }


    [Fact]
    public async Task Preferred_Language_Should_Be_Persisted_Normalized_And_Updated()
    {
        var email = TestAuthHelper.CreateUniqueEmail("preferred-language");
        var password = "P@ssword123!";

        var registerResponse = await _client.PostAsJsonAsync("/api/auth/register", new
        {
            Email = email,
            Password = password,
            ConfirmationUrl = "http://localhost:5173/confirm-email",
            Language = "uk"
        });
        var registerBody = await registerResponse.Content.ReadAsStringAsync();
        registerResponse.StatusCode.Should().Be(HttpStatusCode.OK, $"register response body was: {registerBody}");

        await TestAuthHelper.ConfirmUserEmailAsync(_factory, email);

        var loginResponse = await _client.PostAsJsonAsync("/api/auth/login", new
        {
            Email = email,
            Password = password
        });
        var auth = await loginResponse.Content.ReadFromJsonAsync<AuthResponseDto>();
        auth.Should().NotBeNull();
        TestAuthHelper.SetBearerToken(_client, auth!.AccessToken);

        var meResponse = await _client.GetAsync("/api/auth/me");
        var me = await meResponse.Content.ReadFromJsonAsync<CurrentUserDto>();
        me.Should().NotBeNull();
        me!.PreferredLanguage.Should().Be("ua");

        var updateResponse = await _client.PatchAsJsonAsync("/api/auth/me/language", new
        {
            Language = "pl"
        });
        var updateBody = await updateResponse.Content.ReadAsStringAsync();
        updateResponse.StatusCode.Should().Be(HttpStatusCode.OK, $"update response body was: {updateBody}");
        var updated = await updateResponse.Content.ReadFromJsonAsync<CurrentUserDto>();
        updated.Should().NotBeNull();
        updated!.PreferredLanguage.Should().Be("pl");
    }

    private sealed class AuthResponseDto
    {
        public string AccessToken { get; set; } = default!;
        public string RefreshToken { get; set; } = default!;
        public int ExpiresIn { get; set; }
    }

    private sealed class CurrentUserDto
    {
        public Guid Id { get; set; }
        public string Email { get; set; } = default!;
        public string? DisplayName { get; set; }
        public string PreferredLanguage { get; set; } = "en";
        public string? Role { get; set; }
        public bool IsActive { get; set; }
        public string? AuthProvider { get; set; }
        public DateTime CreatedAtUtc { get; set; }
    }
}