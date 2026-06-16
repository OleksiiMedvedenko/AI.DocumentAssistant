using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using AI.DocumentAssistant.Domain.Enums;
using AI.DocumentAssistant.Infrastructure.Persistence;
using AI.DocumentAssistant.IntegrationTests.Infrastructure;
using AI.DocumentAssistant.IntegrationTests.TestDoubles;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AI.DocumentAssistant.IntegrationTests.Integration;

public sealed class OrganizationInvitationEndpointsTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;
    private readonly FakeEmailSender _emailSender;

    public OrganizationInvitationEndpointsTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
        _emailSender = factory.Services.GetRequiredService<FakeEmailSender>();
        _emailSender.Clear();
    }

    [Fact]
    public async Task Invitation_Flow_Should_Create_Notification_Send_Email_Accept_And_Add_Member()
    {
        var adminEmail = TestAuthHelper.CreateUniqueEmail("org-admin");
        var invitedEmail = TestAuthHelper.CreateUniqueEmail("org-invited");
        await TestAuthHelper.RegisterAsync(_client, invitedEmail);
        await TestAuthHelper.ConfirmUserEmailAsync(_factory, invitedEmail);

        var adminToken = await TestAuthHelper.RegisterAndLoginWithRoleAsync(_factory, _client, UserRole.Admin, adminEmail);
        TestAuthHelper.SetBearerToken(_client, adminToken);

        var organization = await CreateOrganizationAsync("Integration Org");
        _emailSender.Clear();

        var inviteResponse = await _client.PostAsJsonAsync($"/api/organizations/{organization.Id}/invitations", new
        {
            Email = invitedEmail.ToUpperInvariant()
        });
        var inviteBody = await inviteResponse.Content.ReadAsStringAsync();

        inviteResponse.StatusCode.Should().Be(HttpStatusCode.OK, $"response body was: {inviteBody}");
        var invitation = await inviteResponse.Content.ReadFromJsonAsync<OrganizationInvitationResponse>();
        invitation.Should().NotBeNull();
        invitation!.Email.Should().Be(invitedEmail);
        invitation.Status.Should().Be("Pending");

        _emailSender.SentEmails.Should().ContainSingle(x => x.ToEmail == invitedEmail)
            .Which.HtmlBody.Should().Contain("fallback option");

        var invitedToken = await LoginAsync(invitedEmail);
        TestAuthHelper.SetBearerToken(_client, invitedToken);

        var notificationsResponse = await _client.GetAsync("/api/notifications");
        var notificationsBody = await notificationsResponse.Content.ReadAsStringAsync();
        notificationsResponse.StatusCode.Should().Be(HttpStatusCode.OK, $"response body was: {notificationsBody}");
        var notifications = await notificationsResponse.Content.ReadFromJsonAsync<List<UserNotificationResponse>>();
        notifications.Should().ContainSingle(x => x.RelatedInvitationId == invitation.Id);

        var acceptResponse = await _client.PostAsync($"/api/organizations/invitations/{invitation.Id}/accept", null);
        var acceptBody = await acceptResponse.Content.ReadAsStringAsync();
        acceptResponse.StatusCode.Should().Be(HttpStatusCode.OK, $"response body was: {acceptBody}");

        var unreadCountResponse = await _client.GetAsync("/api/notifications/unread-count");
        var unreadCount = await unreadCountResponse.Content.ReadFromJsonAsync<UnreadCountResponse>();
        unreadCount.Should().NotBeNull();
        unreadCount!.Count.Should().Be(0);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var invitedUserId = await db.Users
            .Where(x => x.Email == invitedEmail)
            .Select(x => x.Id)
            .SingleAsync();

        var memberExists = await db.OrganizationMembers.AnyAsync(x =>
            x.OrganizationId == organization.Id && x.UserId == invitedUserId && x.IsActive);
        memberExists.Should().BeTrue();

        var acceptedInvitation = await db.OrganizationInvitations.SingleAsync(x => x.Id == invitation.Id);
        acceptedInvitation.Status.Should().Be(OrganizationInvitationStatus.Accepted);

        var actionTypes = await db.OrganizationActivityLogs
            .Where(x => x.OrganizationId == organization.Id)
            .Select(x => x.ActionType)
            .ToListAsync();
        actionTypes.Should().Contain(OrganizationActivityActionType.OrganizationInvitationCreated);
        actionTypes.Should().Contain(OrganizationActivityActionType.OrganizationInvitationAccepted);
        actionTypes.Should().Contain(OrganizationActivityActionType.OrganizationMemberJoined);
    }

    [Fact]
    public async Task CreateInvitation_Should_Block_Duplicate_Active_Invitation()
    {
        var adminEmail = TestAuthHelper.CreateUniqueEmail("org-admin");
        var invitedEmail = TestAuthHelper.CreateUniqueEmail("org-invited");
        await TestAuthHelper.RegisterAsync(_client, invitedEmail);
        await TestAuthHelper.ConfirmUserEmailAsync(_factory, invitedEmail);

        var adminToken = await TestAuthHelper.RegisterAndLoginWithRoleAsync(_factory, _client, UserRole.Admin, adminEmail);
        TestAuthHelper.SetBearerToken(_client, adminToken);
        var organization = await CreateOrganizationAsync("Duplicate Invite Org");
        _emailSender.Clear();

        var firstResponse = await _client.PostAsJsonAsync($"/api/organizations/{organization.Id}/invitations", new
        {
            Email = invitedEmail
        });
        firstResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var duplicateResponse = await _client.PostAsJsonAsync($"/api/organizations/{organization.Id}/invitations", new
        {
            Email = invitedEmail
        });
        var duplicateBody = await duplicateResponse.Content.ReadAsStringAsync();

        duplicateResponse.StatusCode.Should().Be(HttpStatusCode.Conflict);
        duplicateBody.Should().Contain("ORGANIZATION_INVITATION_ALREADY_PENDING");
    }

    [Fact]
    public async Task Revoked_Invitation_Should_Disappear_From_User_Notifications_And_Cannot_Be_Accepted()
    {
        var adminEmail = TestAuthHelper.CreateUniqueEmail("org-admin");
        var invitedEmail = TestAuthHelper.CreateUniqueEmail("org-invited");
        await TestAuthHelper.RegisterAsync(_client, invitedEmail);
        await TestAuthHelper.ConfirmUserEmailAsync(_factory, invitedEmail);

        var adminToken = await TestAuthHelper.RegisterAndLoginWithRoleAsync(_factory, _client, UserRole.Admin, adminEmail);
        TestAuthHelper.SetBearerToken(_client, adminToken);
        var organization = await CreateOrganizationAsync("Revoked Invite Org");
        _emailSender.Clear();

        var inviteResponse = await _client.PostAsJsonAsync($"/api/organizations/{organization.Id}/invitations", new
        {
            Email = invitedEmail
        });
        var invitation = await inviteResponse.Content.ReadFromJsonAsync<OrganizationInvitationResponse>();
        invitation.Should().NotBeNull();

        var revokeResponse = await _client.DeleteAsync($"/api/organizations/{organization.Id}/invitations/{invitation!.Id}");
        revokeResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var invitedToken = await LoginAsync(invitedEmail);
        TestAuthHelper.SetBearerToken(_client, invitedToken);

        var notifications = await _client.GetFromJsonAsync<List<UserNotificationResponse>>("/api/notifications?includeRead=true");
        notifications.Should().NotBeNull();
        notifications.Should().NotContain(x => x.RelatedInvitationId == invitation.Id);

        var acceptResponse = await _client.PostAsync($"/api/organizations/invitations/{invitation.Id}/accept", null);
        var acceptBody = await acceptResponse.Content.ReadAsStringAsync();

        acceptResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        acceptBody.Should().Contain("ORGANIZATION_INVITATION_REVOKED");
    }

    [Fact]
    public async Task AcceptInvitationByCode_Should_Reject_Different_Authenticated_Email()
    {
        var adminEmail = TestAuthHelper.CreateUniqueEmail("org-admin");
        var invitedEmail = TestAuthHelper.CreateUniqueEmail("org-invited");
        var otherEmail = TestAuthHelper.CreateUniqueEmail("org-other");
        await TestAuthHelper.RegisterAsync(_client, invitedEmail);
        await TestAuthHelper.ConfirmUserEmailAsync(_factory, invitedEmail);
        await TestAuthHelper.RegisterAsync(_client, otherEmail);
        await TestAuthHelper.ConfirmUserEmailAsync(_factory, otherEmail);

        var adminToken = await TestAuthHelper.RegisterAndLoginWithRoleAsync(_factory, _client, UserRole.Admin, adminEmail);
        TestAuthHelper.SetBearerToken(_client, adminToken);
        var organization = await CreateOrganizationAsync("Code Security Org");
        _emailSender.Clear();

        var inviteResponse = await _client.PostAsJsonAsync($"/api/organizations/{organization.Id}/invitations", new
        {
            Email = invitedEmail
        });
        inviteResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var code = ExtractInvitationCode(_emailSender.SentEmails.Last(x => x.ToEmail == invitedEmail).HtmlBody);

        var otherToken = await LoginAsync(otherEmail);
        TestAuthHelper.SetBearerToken(_client, otherToken);

        var acceptByCodeResponse = await _client.PostAsJsonAsync("/api/organizations/invitations/accept-by-code", new
        {
            Code = code
        });
        var acceptBody = await acceptByCodeResponse.Content.ReadAsStringAsync();

        acceptByCodeResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        acceptBody.Should().Contain("ORGANIZATION_INVITATION_EMAIL_MISMATCH");
    }

    [Fact]
    public async Task Organization_Settings_Should_Log_Only_Real_Changes_With_Change_Payload()
    {
        var adminToken = await TestAuthHelper.RegisterAndLoginWithRoleAsync(_factory, _client, UserRole.Admin);
        TestAuthHelper.SetBearerToken(_client, adminToken);
        var organization = await CreateOrganizationAsync("Settings Audit Org");

        var sameValueResponse = await _client.PatchAsJsonAsync($"/api/organizations/{organization.Id}/settings", new
        {
            InvitationLifetimeDays = 3
        });
        sameValueResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var changedResponse = await _client.PatchAsJsonAsync($"/api/organizations/{organization.Id}/settings", new
        {
            InvitationLifetimeDays = 7
        });
        var changedBody = await changedResponse.Content.ReadAsStringAsync();
        changedResponse.StatusCode.Should().Be(HttpStatusCode.OK, $"response body was: {changedBody}");

        var logsResponse = await _client.GetAsync($"/api/organizations/{organization.Id}/activity-logs");
        var logsBody = await logsResponse.Content.ReadAsStringAsync();
        logsResponse.StatusCode.Should().Be(HttpStatusCode.OK, $"response body was: {logsBody}");
        var logs = await logsResponse.Content.ReadFromJsonAsync<List<OrganizationActivityLogResponse>>();

        logs.Should().NotBeNull();
        logs!.Where(x => x.ActionType == "OrganizationSettingsUpdated").Should().ContainSingle()
            .Which.PayloadJson.Should().Contain("previousInvitationLifetimeDays").And.Contain("newInvitationLifetimeDays");
    }

    private async Task<OrganizationResponse> CreateOrganizationAsync(string name)
    {
        var response = await _client.PostAsJsonAsync("/api/organizations", new
        {
            Name = name
        });
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.Created, $"response body was: {body}");
        var organization = await response.Content.ReadFromJsonAsync<OrganizationResponse>();
        organization.Should().NotBeNull();
        return organization!;
    }

    private async Task<string> LoginAsync(string email)
    {
        var response = await _client.PostAsJsonAsync("/api/auth/login", new
        {
            Email = email,
            Password = "P@ssword123!"
        });
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK, $"login response body was: {body}");
        var auth = await response.Content.ReadFromJsonAsync<AuthResponse>();
        auth.Should().NotBeNull();
        return auth!.AccessToken;
    }

    private static string ExtractInvitationCode(string htmlBody)
    {
        var match = Regex.Match(htmlBody, "[A-Z2-9]{4}-[A-Z2-9]{4}-[A-Z2-9]{4}");
        match.Success.Should().BeTrue($"HTML body should contain an invitation code. Body: {htmlBody}");
        return match.Value;
    }

    private sealed class AuthResponse
    {
        public string AccessToken { get; set; } = default!;
        public string RefreshToken { get; set; } = default!;
        public int ExpiresIn { get; set; }
    }

    private sealed class OrganizationResponse
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = default!;
        public Guid CreatedByUserId { get; set; }
        public string CreatedByEmail { get; set; } = default!;
        public DateTime CreatedAtUtc { get; set; }
        public DateTime? UpdatedAtUtc { get; set; }
        public bool IsActive { get; set; }
        public int ActiveMembersCount { get; set; }
        public bool CanManage { get; set; }
    }

    private sealed class OrganizationInvitationResponse
    {
        public Guid Id { get; set; }
        public Guid OrganizationId { get; set; }
        public string OrganizationName { get; set; } = default!;
        public string Email { get; set; } = default!;
        public Guid InvitedUserId { get; set; }
        public string InvitedUserEmail { get; set; } = default!;
        public Guid InvitedByUserId { get; set; }
        public string InvitedByEmail { get; set; } = default!;
        public string Status { get; set; } = default!;
        public DateTime CreatedAtUtc { get; set; }
        public DateTime ExpiresAtUtc { get; set; }
        public DateTime? AcceptedAtUtc { get; set; }
        public DateTime? RevokedAtUtc { get; set; }
    }

    private sealed class UserNotificationResponse
    {
        public Guid Id { get; set; }
        public string Type { get; set; } = default!;
        public string TitleKey { get; set; } = default!;
        public string MessageKey { get; set; } = default!;
        public string PayloadJson { get; set; } = "{}";
        public DateTime CreatedAtUtc { get; set; }
        public DateTime? ReadAtUtc { get; set; }
        public Guid? RelatedOrganizationId { get; set; }
        public Guid? RelatedInvitationId { get; set; }
    }

    private sealed class UnreadCountResponse
    {
        public int Count { get; set; }
    }

    private sealed class OrganizationActivityLogResponse
    {
        public Guid Id { get; set; }
        public Guid OrganizationId { get; set; }
        public Guid? ActorUserId { get; set; }
        public string? ActorEmail { get; set; }
        public string ActionType { get; set; } = default!;
        public string PayloadJson { get; set; } = "{}";
        public DateTime CreatedAtUtc { get; set; }
    }
}
