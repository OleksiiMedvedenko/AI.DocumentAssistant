using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Xunit;

namespace AI.DocumentAssistant.UnitTests.Integration;

public sealed class AuthEndpointsTests : IClassFixture<Infrastructure.CustomWebApplicationFactory>
{
    private readonly HttpClient _client;

    public AuthEndpointsTests(Infrastructure.CustomWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Register_And_Login_Should_Return_AccessToken_And_RefreshToken()
    {
        var email = $"auth_{Guid.NewGuid():N}@test.local";
        var password = "P@ssword123!";

        var registerResponse = await _client.PostAsJsonAsync("/api/auth/register", new
        {
            Email = email,
            Password = password
        });

        registerResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var loginResponse = await _client.PostAsJsonAsync("/api/auth/login", new
        {
            Email = email,
            Password = password
        });

        loginResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await loginResponse.Content.ReadFromJsonAsync<AuthResponseDto>();
        body.Should().NotBeNull();
        body!.AccessToken.Should().NotBeNullOrWhiteSpace();
        body.RefreshToken.Should().NotBeNullOrWhiteSpace();
        body.ExpiresIn.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Me_Should_Return_Current_User_When_Authorized()
    {
        var token = await Infrastructure.TestAuthHelper.RegisterAndLoginAsync(_client);
        Infrastructure.TestAuthHelper.SetBearerToken(_client, token);

        var response = await _client.GetAsync("/api/auth/me");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<CurrentUserDto>();
        body.Should().NotBeNull();
        body!.Email.Should().Contain("@test.local");
        body.Id.Should().NotBeEmpty();
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
        public DateTime CreatedAtUtc { get; set; }
    }
}