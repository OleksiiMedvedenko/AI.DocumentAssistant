using System.Security.Cryptography;
using System.Text;
using AI.DocumentAssistant.Application.Abstractions.Communication;
using AI.DocumentAssistant.Application.Common.Exceptions;
using AI.DocumentAssistant.Application.Organizations.Dtos;
using AI.DocumentAssistant.Application.Organizations.Services;
using AI.DocumentAssistant.Application.Services.Communication;
using AI.DocumentAssistant.Domain.Entities;
using AI.DocumentAssistant.Domain.Enums;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AI.DocumentAssistant.UnitTests.Unit.Organizations;

public sealed class OrganizationInvitationServiceTests
{
    [Fact]
    public async Task CreateInvitationAsync_Should_Reject_Unknown_User_Email()
    {
        await using var fixture = await OrganizationUnitTestFixture.CreateAsync();
        var manager = fixture.AddUser(UserRole.Manager, email: "manager@test.local");
        var organization = fixture.AddOrganization(manager);
        fixture.AddMembership(organization, manager, manager);
        await fixture.SaveChangesAsync();
        fixture.CurrentUser.UserId = manager.Id;

        var sut = CreateService(fixture, new RecordingEmailSender());

        var act = () => sut.CreateInvitationAsync(organization.Id, new CreateOrganizationInvitationDto
        {
            Email = "missing@test.local"
        });

        await act.Should().ThrowAsync<NotFoundException>()
            .WithMessage("ORGANIZATION_INVITATION_USER_ACCOUNT_REQUIRED");
    }

    [Fact]
    public async Task CreateInvitationAsync_Should_Block_Duplicate_Active_Invitation()
    {
        await using var fixture = await OrganizationUnitTestFixture.CreateAsync();
        var manager = fixture.AddUser(UserRole.Manager, email: "manager@test.local");
        var invitedUser = fixture.AddUser(UserRole.User, email: "invited@test.local");
        var organization = fixture.AddOrganization(manager);
        fixture.AddMembership(organization, manager, manager);
        fixture.DbContext.OrganizationInvitations.Add(CreateInvitation(organization, invitedUser, manager, "ABC1-ABC2-ABC3"));
        await fixture.SaveChangesAsync();
        fixture.CurrentUser.UserId = manager.Id;

        var emailSender = new RecordingEmailSender();
        var sut = CreateService(fixture, emailSender);

        var act = () => sut.CreateInvitationAsync(organization.Id, new CreateOrganizationInvitationDto
        {
            Email = invitedUser.Email
        });

        await act.Should().ThrowAsync<ConflictException>()
            .WithMessage("ORGANIZATION_INVITATION_ALREADY_PENDING");
        emailSender.SentEmails.Should().BeEmpty();
    }

    [Fact]
    public async Task CreateInvitationAsync_Should_Create_Pending_Invitation_Notification_Activity_Log_And_Localized_Email()
    {
        await using var fixture = await OrganizationUnitTestFixture.CreateAsync();
        var manager = fixture.AddUser(UserRole.Manager, email: "manager@test.local");
        var invitedUser = fixture.AddUser(UserRole.User, email: "invited@test.local", preferredLanguage: "pl");
        var organization = fixture.AddOrganization(manager, name: "Acme Org");
        fixture.AddMembership(organization, manager, manager);
        await fixture.SaveChangesAsync();
        fixture.CurrentUser.UserId = manager.Id;

        var emailSender = new RecordingEmailSender();
        var sut = CreateService(fixture, emailSender);

        var result = await sut.CreateInvitationAsync(organization.Id, new CreateOrganizationInvitationDto
        {
            Email = " INVITED@test.local "
        });

        result.Email.Should().Be("invited@test.local");
        result.Status.Should().Be(OrganizationInvitationStatus.Pending.ToString());
        result.ExpiresAtUtc.Should().BeAfter(DateTime.UtcNow.AddDays(2));

        var notification = await fixture.DbContext.UserNotifications.SingleAsync(x => x.UserId == invitedUser.Id);
        notification.RelatedInvitationId.Should().Be(result.Id);
        notification.RelatedOrganizationId.Should().Be(organization.Id);
        notification.DismissedAtUtc.Should().BeNull();

        var activityLog = await fixture.DbContext.OrganizationActivityLogs
            .SingleAsync(x => x.ActionType == OrganizationActivityActionType.OrganizationInvitationCreated);
        activityLog.PayloadJson.Should().Contain("invited@test.local");

        var sentEmail = emailSender.SentEmails.Should().ContainSingle().Which;
        sentEmail.ToEmail.Should().Be("invited@test.local");
        sentEmail.Subject.Should().Contain("Zaproszenie");
        sentEmail.HtmlBody.Should().Contain("Acme Org");
        sentEmail.HtmlBody.Should().Contain("Kod poniżej");
    }

    [Fact]
    public async Task AcceptInvitationByCodeAsync_Should_Reject_User_With_Different_Email()
    {
        await using var fixture = await OrganizationUnitTestFixture.CreateAsync();
        var manager = fixture.AddUser(UserRole.Manager, email: "manager@test.local");
        var invitedUser = fixture.AddUser(UserRole.User, email: "invited@test.local");
        var otherUser = fixture.AddUser(UserRole.User, email: "other@test.local");
        var organization = fixture.AddOrganization(manager, name: "Secure Org");
        fixture.AddMembership(organization, manager, manager);
        fixture.DbContext.OrganizationInvitations.Add(CreateInvitation(organization, invitedUser, manager, "SAFE-CODE-1234"));
        await fixture.SaveChangesAsync();
        fixture.CurrentUser.UserId = otherUser.Id;

        var sut = CreateService(fixture, new RecordingEmailSender());

        var act = () => sut.AcceptInvitationByCodeAsync("safe-code-1234");

        await act.Should().ThrowAsync<ForbiddenException>()
            .WithMessage("ORGANIZATION_INVITATION_EMAIL_MISMATCH");
    }

    [Fact]
    public async Task AcceptInvitationByCodeAsync_Should_Join_User_Dismiss_Notification_And_Write_Activity_Logs()
    {
        await using var fixture = await OrganizationUnitTestFixture.CreateAsync();
        var manager = fixture.AddUser(UserRole.Manager, email: "manager@test.local");
        var invitedUser = fixture.AddUser(UserRole.User, email: "invited@test.local");
        var organization = fixture.AddOrganization(manager, name: "Accepted Org");
        fixture.AddMembership(organization, manager, manager);
        var invitation = CreateInvitation(organization, invitedUser, manager, "JOIN-CODE-1234");
        fixture.DbContext.OrganizationInvitations.Add(invitation);
        fixture.DbContext.UserNotifications.Add(new UserNotification
        {
            Id = Guid.NewGuid(),
            UserId = invitedUser.Id,
            Type = UserNotificationType.OrganizationInvitation,
            TitleKey = "notifications.organizationInvitation.title",
            MessageKey = "notifications.organizationInvitation.message",
            PayloadJson = "{}",
            CreatedAtUtc = DateTime.UtcNow,
            RelatedOrganizationId = organization.Id,
            RelatedInvitationId = invitation.Id
        });
        await fixture.SaveChangesAsync();
        fixture.CurrentUser.UserId = invitedUser.Id;

        var sut = CreateService(fixture, new RecordingEmailSender());

        var result = await sut.AcceptInvitationByCodeAsync("join-code-1234");

        result.OrganizationId.Should().Be(organization.Id);
        result.OrganizationName.Should().Be("Accepted Org");

        var membership = await fixture.DbContext.OrganizationMembers
            .SingleAsync(x => x.OrganizationId == organization.Id && x.UserId == invitedUser.Id);
        membership.IsActive.Should().BeTrue();
        membership.AddedByUserId.Should().Be(manager.Id);

        var updatedInvitation = await fixture.DbContext.OrganizationInvitations.SingleAsync(x => x.Id == invitation.Id);
        updatedInvitation.Status.Should().Be(OrganizationInvitationStatus.Accepted);
        updatedInvitation.AcceptedAtUtc.Should().NotBeNull();

        var notification = await fixture.DbContext.UserNotifications.SingleAsync(x => x.RelatedInvitationId == invitation.Id);
        notification.DismissedAtUtc.Should().NotBeNull();
        notification.ReadAtUtc.Should().NotBeNull();

        var actionTypes = await fixture.DbContext.OrganizationActivityLogs
            .Where(x => x.OrganizationId == organization.Id)
            .Select(x => x.ActionType)
            .ToListAsync();
        actionTypes.Should().Contain(OrganizationActivityActionType.OrganizationInvitationAccepted);
        actionTypes.Should().Contain(OrganizationActivityActionType.OrganizationMemberJoined);
    }

    [Theory]
    [InlineData(OrganizationInvitationStatus.Revoked, "ORGANIZATION_INVITATION_REVOKED")]
    [InlineData(OrganizationInvitationStatus.Accepted, "ORGANIZATION_INVITATION_ALREADY_ACCEPTED")]
    [InlineData(OrganizationInvitationStatus.Expired, "ORGANIZATION_INVITATION_EXPIRED")]
    public async Task AcceptInvitationAsync_Should_Return_Stable_Error_For_Inactive_Invitation_Statuses(
        OrganizationInvitationStatus status,
        string expectedErrorCode)
    {
        await using var fixture = await OrganizationUnitTestFixture.CreateAsync();
        var manager = fixture.AddUser(UserRole.Manager, email: "manager@test.local");
        var invitedUser = fixture.AddUser(UserRole.User, email: "invited@test.local");
        var organization = fixture.AddOrganization(manager);
        fixture.AddMembership(organization, manager, manager);
        var invitation = CreateInvitation(organization, invitedUser, manager, "FAIL-CODE-1234", status);
        fixture.DbContext.OrganizationInvitations.Add(invitation);
        await fixture.SaveChangesAsync();
        fixture.CurrentUser.UserId = invitedUser.Id;

        var sut = CreateService(fixture, new RecordingEmailSender());

        var act = () => sut.AcceptInvitationAsync(invitation.Id);

        await act.Should().ThrowAsync<BadRequestException>()
            .WithMessage(expectedErrorCode);
    }

    [Fact]
    public async Task UpdateSettingsAsync_Should_Write_Change_Log_Only_When_Value_Changes()
    {
        await using var fixture = await OrganizationUnitTestFixture.CreateAsync();
        var manager = fixture.AddUser(UserRole.Manager, email: "manager@test.local");
        var organization = fixture.AddOrganization(manager);
        fixture.AddMembership(organization, manager, manager);
        await fixture.SaveChangesAsync();
        fixture.CurrentUser.UserId = manager.Id;

        var sut = CreateService(fixture, new RecordingEmailSender());

        await sut.UpdateSettingsAsync(organization.Id, new UpdateOrganizationSettingsDto { InvitationLifetimeDays = 3 });
        fixture.DbContext.OrganizationActivityLogs.Should().BeEmpty();

        await sut.UpdateSettingsAsync(organization.Id, new UpdateOrganizationSettingsDto { InvitationLifetimeDays = 7 });

        var log = await fixture.DbContext.OrganizationActivityLogs.SingleAsync();
        log.ActionType.Should().Be(OrganizationActivityActionType.OrganizationSettingsUpdated);
        log.PayloadJson.Should().Contain("previousInvitationLifetimeDays");
        log.PayloadJson.Should().Contain("newInvitationLifetimeDays");
        log.PayloadJson.Should().Contain("7");
    }

    private static OrganizationInvitationService CreateService(
        OrganizationUnitTestFixture fixture,
        IEmailSender emailSender)
    {
        return new OrganizationInvitationService(
            fixture.DbContext,
            fixture.CurrentUser,
            emailSender,
            new OrganizationInvitationEmailTemplateService());
    }

    private static OrganizationInvitation CreateInvitation(
        Organization organization,
        User invitedUser,
        User invitedByUser,
        string rawCode,
        OrganizationInvitationStatus status = OrganizationInvitationStatus.Pending,
        DateTime? expiresAtUtc = null)
    {
        return new OrganizationInvitation
        {
            Id = Guid.NewGuid(),
            OrganizationId = organization.Id,
            Organization = organization,
            Email = invitedUser.Email,
            InvitedUserId = invitedUser.Id,
            InvitedUser = invitedUser,
            InvitedByUserId = invitedByUser.Id,
            InvitedByUser = invitedByUser,
            CodeHash = ComputeSha256(NormalizeInvitationCode(rawCode)),
            Status = status,
            CreatedAtUtc = DateTime.UtcNow,
            ExpiresAtUtc = expiresAtUtc ?? DateTime.UtcNow.AddDays(3),
            AcceptedAtUtc = status == OrganizationInvitationStatus.Accepted ? DateTime.UtcNow : null,
            RevokedAtUtc = status == OrganizationInvitationStatus.Revoked ? DateTime.UtcNow : null,
            RevokedByUserId = status == OrganizationInvitationStatus.Revoked ? invitedByUser.Id : null
        };
    }

    private static string NormalizeInvitationCode(string code)
    {
        return code.Trim().ToUpperInvariant().Replace(" ", string.Empty).Replace("-", string.Empty);
    }

    private static string ComputeSha256(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes);
    }

    private sealed class RecordingEmailSender : IEmailSender
    {
        private readonly List<SentEmail> _sentEmails = new();

        public IReadOnlyList<SentEmail> SentEmails => _sentEmails;

        public Task SendAsync(string toEmail, string subject, string htmlBody, CancellationToken cancellationToken)
        {
            _sentEmails.Add(new SentEmail(toEmail, subject, htmlBody));
            return Task.CompletedTask;
        }
    }

    private sealed record SentEmail(string ToEmail, string Subject, string HtmlBody);
}
