using System.Net;
using System.Net.Http.Json;
using AI.DocumentAssistant.Infrastructure.Persistence;
using AI.DocumentAssistant.UnitTests.Infrastructure;
using AI.DocumentAssistant.UnitTests.TestDoubles;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AI.DocumentAssistant.UnitTests.Integration;

public sealed class AuthEmailConfirmationEndpointsTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;
    private readonly FakeEmailSender _emailSender;

    public AuthEmailConfirmationEndpointsTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
        _emailSender = factory.Services.GetRequiredService<FakeEmailSender>();
        _emailSender.Clear();
    }

    [Fact]
    public async Task Register_Should_Create_Unconfirmed_User_Send_Email_And_Block_Login_Until_Confirmed()
    {
        var email = TestAuthHelper.CreateUniqueEmail("confirm-required");
        var password = "P@ssword123!";

        await TestAuthHelper.RegisterAsync(_client, email, password);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var user = db.Users.Single(x => x.Email == email);

            user.EmailConfirmed.Should().BeFalse();
            user.EmailConfirmationTokenHash.Should().NotBeNullOrWhiteSpace();
            user.EmailConfirmationTokenExpiresAtUtc.Should().BeAfter(DateTime.UtcNow);
        }

        _emailSender.SentEmails.Should().ContainSingle(x => x.ToEmail == email)
            .Which.Subject.Should().NotBeNullOrWhiteSpace();

        var loginResponse = await _client.PostAsJsonAsync("/api/auth/login", new
        {
            Email = email,
            Password = password
        });
        var loginBody = await loginResponse.Content.ReadAsStringAsync();

        loginResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        loginBody.Should().Contain("AUTH_EMAIL_NOT_CONFIRMED");
    }

    [Fact]
    public async Task ConfirmEmail_Should_Activate_User_And_Allow_Login()
    {
        var email = TestAuthHelper.CreateUniqueEmail("confirm-flow");
        var password = "P@ssword123!";

        await TestAuthHelper.RegisterAsync(_client, email, password);

        var sentEmail = _emailSender.SentEmails.Should().ContainSingle(x => x.ToEmail == email).Which;
        var token = EmailTestHelper.ExtractConfirmationToken(sentEmail);

        var confirmResponse = await _client.GetAsync(
            $"/api/auth/confirm-email?email={Uri.EscapeDataString(email)}&token={Uri.EscapeDataString(token)}");
        var confirmBody = await confirmResponse.Content.ReadAsStringAsync();

        confirmResponse.StatusCode.Should().Be(HttpStatusCode.OK, $"response body was: {confirmBody}");

        var loginResponse = await _client.PostAsJsonAsync("/api/auth/login", new
        {
            Email = email,
            Password = password
        });
        var loginBody = await loginResponse.Content.ReadAsStringAsync();

        loginResponse.StatusCode.Should().Be(HttpStatusCode.OK, $"login response body was: {loginBody}");
    }

    [Fact]
    public async Task Register_Should_Reject_Duplicate_Email()
    {
        var email = TestAuthHelper.CreateUniqueEmail("duplicate");

        await TestAuthHelper.RegisterAsync(_client, email);

        var response = await _client.PostAsJsonAsync("/api/auth/register", new
        {
            Email = email.ToUpperInvariant(),
            Password = "P@ssword123!",
            ConfirmationUrl = "http://localhost:5173/confirm-email",
            Language = "en"
        });
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        body.Should().Contain("AUTH_EMAIL_ALREADY_EXISTS");
    }

    [Fact]
    public async Task ResendConfirmationEmail_Should_Send_New_Email_For_Unconfirmed_User()
    {
        var email = TestAuthHelper.CreateUniqueEmail("resend");

        await TestAuthHelper.RegisterAsync(_client, email);
        _emailSender.Clear();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var user = db.Users.Single(x => x.Email == email);
            user.EmailConfirmationSentAtUtc = DateTime.UtcNow.AddMinutes(-5);
            await db.SaveChangesAsync();
        }

        var response = await _client.PostAsJsonAsync("/api/auth/resend-confirmation-email", new
        {
            Email = email,
            ConfirmationUrl = "http://localhost:5173/confirm-email",
            Language = "pl"
        });
        var responseBody = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK, $"response body was: {responseBody}");
        _emailSender.SentEmails.Should().ContainSingle(x => x.ToEmail == email)
            .Which.Subject.Should().Contain("Potwierdź");
    }
}
