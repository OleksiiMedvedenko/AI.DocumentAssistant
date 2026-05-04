using System.Net;
using System.Net.Http.Json;
using AI.DocumentAssistant.Domain.Enums;
using AI.DocumentAssistant.Infrastructure.Persistence;
using AI.DocumentAssistant.IntegrationTests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AI.DocumentAssistant.IntegrationTests.Integration;

public sealed class AdminUsersEndpointsTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public AdminUsersEndpointsTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Admin_Endpoints_Should_Reject_Regular_Users()
    {
        var token = await TestAuthHelper.RegisterAndLoginAsync(_factory, _client);
        TestAuthHelper.SetBearerToken(_client, token);

        var response = await _client.GetAsync("/api/admin/users");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Admin_Should_List_Users_And_Update_Role_Active_Status_And_Limits()
    {
        var targetEmail = TestAuthHelper.CreateUniqueEmail("managed-user");
        await TestAuthHelper.RegisterAsync(_client, targetEmail);
        await TestAuthHelper.ConfirmUserEmailAsync(_factory, targetEmail);

        var targetUserId = await GetUserIdAsync(targetEmail);
        var adminToken = await RegisterAdminAndLoginAsync();
        TestAuthHelper.SetBearerToken(_client, adminToken);

        var listResponse = await _client.GetAsync("/api/admin/users");
        var listBody = await listResponse.Content.ReadAsStringAsync();

        listResponse.StatusCode.Should().Be(HttpStatusCode.OK, $"response body was: {listBody}");
        listBody.Should().Contain(targetEmail);

        var roleResponse = await _client.PatchAsJsonAsync($"/api/admin/users/{targetUserId}/role", new
        {
            Role = "Admin"
        });
        var roleBody = await roleResponse.Content.ReadAsStringAsync();

        roleResponse.StatusCode.Should().Be(HttpStatusCode.OK, $"response body was: {roleBody}");

        var activeResponse = await _client.PatchAsJsonAsync($"/api/admin/users/{targetUserId}/active-status", new
        {
            IsActive = false
        });
        var activeBody = await activeResponse.Content.ReadAsStringAsync();

        activeResponse.StatusCode.Should().Be(HttpStatusCode.OK, $"response body was: {activeBody}");

        var limitsResponse = await _client.PatchAsJsonAsync($"/api/admin/users/{targetUserId}/limits", new
        {
            HasUnlimitedAiUsage = true,
            MonthlyChatMessageLimit = 3,
            MonthlyDocumentUploadLimit = 4,
            MonthlySummarizationLimit = 5,
            MonthlyExtractionLimit = 6,
            MonthlyComparisonLimit = 7,
            Reason = "integration-test"
        });
        var limitsBody = await limitsResponse.Content.ReadAsStringAsync();

        limitsResponse.StatusCode.Should().Be(HttpStatusCode.OK, $"response body was: {limitsBody}");

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var user = await db.Users.SingleAsync(x => x.Id == targetUserId);
            var activeOverride = await db.UserQuotaOverrides.SingleAsync(x => x.UserId == targetUserId && x.ValidToUtc == null);

            user.Role.Should().Be(UserRole.Admin);
            user.IsActive.Should().BeFalse();
            activeOverride.HasUnlimitedAiUsageOverride.Should().BeTrue();
            activeOverride.MonthlyChatMessageLimitOverride.Should().Be(3);
            activeOverride.MonthlyComparisonLimitOverride.Should().Be(7);
            activeOverride.Reason.Should().Be("integration-test");
        }

        var removeLimitsResponse = await _client.DeleteAsync($"/api/admin/users/{targetUserId}/limits");
        var removeLimitsBody = await removeLimitsResponse.Content.ReadAsStringAsync();

        removeLimitsResponse.StatusCode.Should().Be(HttpStatusCode.OK, $"response body was: {removeLimitsBody}");

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var activeOverrides = await db.UserQuotaOverrides
                .CountAsync(x => x.UserId == targetUserId && x.ValidToUtc == null);

            activeOverrides.Should().Be(0);
        }
    }

    private async Task<string> RegisterAdminAndLoginAsync()
    {
        var email = TestAuthHelper.CreateUniqueEmail("admin");
        var password = "P@ssword123!";

        await TestAuthHelper.RegisterAsync(_client, email, password);
        await TestAuthHelper.ConfirmUserEmailAsync(_factory, email);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var user = await db.Users.SingleAsync(x => x.Email == email);
            user.Role = UserRole.Admin;
            await db.SaveChangesAsync();
        }

        var loginResponse = await _client.PostAsJsonAsync("/api/auth/login", new
        {
            Email = email,
            Password = password
        });
        var loginBody = await loginResponse.Content.ReadAsStringAsync();

        loginResponse.StatusCode.Should().Be(HttpStatusCode.OK, $"login response body was: {loginBody}");

        var auth = await loginResponse.Content.ReadFromJsonAsync<AuthResponseDto>();
        auth.Should().NotBeNull();
        auth!.AccessToken.Should().NotBeNullOrWhiteSpace();

        return auth.AccessToken;
    }

    private async Task<Guid> GetUserIdAsync(string email)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Users
            .Where(x => x.Email == email.Trim().ToLowerInvariant())
            .Select(x => x.Id)
            .SingleAsync();
    }

    private sealed class AuthResponseDto
    {
        public string AccessToken { get; set; } = default!;
        public string RefreshToken { get; set; } = default!;
        public int ExpiresIn { get; set; }
    }
}
