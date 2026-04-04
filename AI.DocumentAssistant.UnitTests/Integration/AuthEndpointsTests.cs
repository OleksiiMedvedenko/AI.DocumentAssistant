using AI.DocumentAssistant.UnitTests.Infrastructure;
using FluentAssertions;
using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace AI.DocumentAssistant.UnitTests.Integration;

public sealed class AuthEndpointsTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client;

    public AuthEndpointsTests(CustomWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Register_And_Login_Should_Return_AccessToken()
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

        var json = await loginResponse.Content.ReadAsStringAsync();
        json.Should().Contain("accessToken");
    }

    [Fact]
    public async Task Me_Should_Return_Current_User_When_Authorized()
    {
        var token = await TestAuthHelper.RegisterAndLoginAsync(_client);
        TestAuthHelper.SetBearerToken(_client, token);

        var response = await _client.GetAsync("/api/auth/me");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("email");
    }
}