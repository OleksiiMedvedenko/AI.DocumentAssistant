using AI.DocumentAssistant.Application.Abstractions.Common;
using AI.DocumentAssistant.Application.Common.Exceptions;
using AI.DocumentAssistant.Application.Organizations.Dtos;
using AI.DocumentAssistant.Application.Organizations.Services;
using AI.DocumentAssistant.Domain.Entities;
using AI.DocumentAssistant.Domain.Enums;
using AI.DocumentAssistant.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AI.DocumentAssistant.UnitTests.Unit.Organizations;

public sealed class OrganizationServiceTests
{
    [Fact]
    public async Task CreateAsync_Should_Reject_Regular_User()
    {
        await using var fixture = await OrganizationUnitTestFixture.CreateAsync();
        var user = fixture.AddUser(UserRole.User);
        await fixture.SaveChangesAsync();
        fixture.CurrentUser.UserId = user.Id;

        var sut = new OrganizationService(fixture.DbContext, fixture.CurrentUser);

        var act = () => sut.CreateAsync(new CreateOrganizationDto { Name = "Regular user organization" });

        await act.Should().ThrowAsync<ForbiddenException>()
            .WithMessage("ORGANIZATION_CREATE_REQUIRES_ADMIN_OR_MANAGER");
    }

    [Theory]
    [InlineData(UserRole.Admin)]
    [InlineData(UserRole.Manager)]
    public async Task CreateAsync_Should_Create_Organization_Settings_Owner_Membership_And_Activity_Log_For_Privileged_User(UserRole role)
    {
        await using var fixture = await OrganizationUnitTestFixture.CreateAsync();
        var user = fixture.AddUser(role, email: $"{role.ToString().ToLowerInvariant()}@test.local");
        await fixture.SaveChangesAsync();
        fixture.CurrentUser.UserId = user.Id;

        var sut = new OrganizationService(fixture.DbContext, fixture.CurrentUser);

        var result = await sut.CreateAsync(new CreateOrganizationDto { Name = "  Test Organization  " });

        result.Name.Should().Be("Test Organization");
        result.CanManage.Should().BeTrue();

        var membership = await fixture.DbContext.OrganizationMembers.SingleAsync(x => x.OrganizationId == result.Id);
        membership.UserId.Should().Be(user.Id);
        membership.AddedByUserId.Should().Be(user.Id);
        membership.IsActive.Should().BeTrue();

        var settings = await fixture.DbContext.OrganizationSettings.SingleAsync(x => x.OrganizationId == result.Id);
        settings.InvitationLifetimeDays.Should().Be(3);

        var log = await fixture.DbContext.OrganizationActivityLogs.SingleAsync(x => x.OrganizationId == result.Id);
        log.ActionType.Should().Be(OrganizationActivityActionType.OrganizationCreated);
    }

    [Fact]
    public async Task RemoveMemberAsync_Should_Reject_Removing_Current_User()
    {
        await using var fixture = await OrganizationUnitTestFixture.CreateAsync();
        var manager = fixture.AddUser(UserRole.Manager, email: "manager@test.local");
        var organization = fixture.AddOrganization(manager);
        fixture.AddMembership(organization, manager, manager);
        await fixture.SaveChangesAsync();
        fixture.CurrentUser.UserId = manager.Id;

        var sut = new OrganizationService(fixture.DbContext, fixture.CurrentUser);

        var act = () => sut.RemoveMemberAsync(organization.Id, manager.Id);

        await act.Should().ThrowAsync<BadRequestException>()
            .WithMessage("ORGANIZATION_CANNOT_REMOVE_YOURSELF");
    }

    [Fact]
    public async Task RemoveMemberAsync_Should_Reject_Removing_Last_Active_Member()
    {
        await using var fixture = await OrganizationUnitTestFixture.CreateAsync();
        var admin = fixture.AddUser(UserRole.Admin, email: "admin@test.local");
        var owner = fixture.AddUser(UserRole.Manager, email: "owner@test.local");
        var organization = fixture.AddOrganization(owner);
        fixture.AddMembership(organization, owner, owner);
        await fixture.SaveChangesAsync();
        fixture.CurrentUser.UserId = admin.Id;

        var sut = new OrganizationService(fixture.DbContext, fixture.CurrentUser);

        var act = () => sut.RemoveMemberAsync(organization.Id, owner.Id);

        await act.Should().ThrowAsync<BadRequestException>()
            .WithMessage("ORGANIZATION_CANNOT_REMOVE_LAST_ACTIVE_MEMBER");
    }

    [Fact]
    public async Task GetMyOrganizationsAsync_Should_Filter_By_Role_And_Organization_Status()
    {
        await using var fixture = await OrganizationUnitTestFixture.CreateAsync();
        var admin = fixture.AddUser(UserRole.Admin, email: "admin@test.local");
        var manager = fixture.AddUser(UserRole.Manager, email: "manager@test.local");
        var regularUser = fixture.AddUser(UserRole.User, email: "member@test.local");
        var activeOrganization = fixture.AddOrganization(manager, name: "Active org", isActive: true);
        var inactiveOrganization = fixture.AddOrganization(manager, name: "Inactive org", isActive: false);
        fixture.AddMembership(activeOrganization, regularUser, manager);
        fixture.AddMembership(inactiveOrganization, regularUser, manager);
        fixture.AddMembership(activeOrganization, manager, manager);
        fixture.AddMembership(inactiveOrganization, manager, manager);
        await fixture.SaveChangesAsync();

        var sut = new OrganizationService(fixture.DbContext, fixture.CurrentUser);

        fixture.CurrentUser.UserId = regularUser.Id;
        var regularUserOrganizations = await sut.GetMyOrganizationsAsync();
        regularUserOrganizations.Should().ContainSingle().Which.Name.Should().Be("Active org");

        fixture.CurrentUser.UserId = manager.Id;
        var managerOrganizations = await sut.GetMyOrganizationsAsync();
        managerOrganizations.Should().HaveCount(2);

        fixture.CurrentUser.UserId = admin.Id;
        var adminOrganizations = await sut.GetMyOrganizationsAsync();
        adminOrganizations.Should().HaveCount(2);
    }
}

internal sealed class OrganizationUnitTestFixture : IAsyncDisposable
{
    private readonly SqliteConnection _connection;

    private OrganizationUnitTestFixture(SqliteConnection connection, AppDbContext dbContext)
    {
        _connection = connection;
        DbContext = dbContext;
        CurrentUser = new MutableCurrentUserService();
    }

    public AppDbContext DbContext { get; }
    public MutableCurrentUserService CurrentUser { get; }

    public static async Task<OrganizationUnitTestFixture> CreateAsync()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;

        var dbContext = new AppDbContext(options);
        await dbContext.Database.EnsureCreatedAsync();

        return new OrganizationUnitTestFixture(connection, dbContext);
    }

    public User AddUser(UserRole role, string? email = null, bool isActive = true, string preferredLanguage = "en")
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = email ?? $"user-{Guid.NewGuid():N}@test.local",
            PasswordHash = "hashed-password",
            CreatedAtUtc = DateTime.UtcNow,
            Role = role,
            IsActive = isActive,
            EmailConfirmed = true,
            AuthProvider = AuthProvider.Local,
            PreferredLanguage = preferredLanguage
        };

        DbContext.Users.Add(user);
        return user;
    }

    public Organization AddOrganization(User createdBy, string name = "Organization", bool isActive = true)
    {
        var organization = new Organization
        {
            Id = Guid.NewGuid(),
            Name = name,
            CreatedByUserId = createdBy.Id,
            CreatedByUser = createdBy,
            CreatedAtUtc = DateTime.UtcNow,
            IsActive = isActive
        };

        DbContext.Organizations.Add(organization);
        DbContext.OrganizationSettings.Add(new OrganizationSettings
        {
            Id = Guid.NewGuid(),
            OrganizationId = organization.Id,
            Organization = organization,
            InvitationLifetimeDays = 3,
            CreatedAtUtc = DateTime.UtcNow
        });
        return organization;
    }

    public OrganizationMember AddMembership(Organization organization, User user, User addedBy, bool isActive = true)
    {
        var membership = new OrganizationMember
        {
            Id = Guid.NewGuid(),
            OrganizationId = organization.Id,
            Organization = organization,
            UserId = user.Id,
            User = user,
            AddedByUserId = addedBy.Id,
            AddedByUser = addedBy,
            JoinedAtUtc = DateTime.UtcNow,
            IsActive = isActive
        };

        DbContext.OrganizationMembers.Add(membership);
        return membership;
    }

    public Task<int> SaveChangesAsync() => DbContext.SaveChangesAsync();

    public async ValueTask DisposeAsync()
    {
        await DbContext.DisposeAsync();
        await _connection.DisposeAsync();
    }
}

internal sealed class MutableCurrentUserService : ICurrentUserService
{
    public Guid UserId { get; set; }

    public Guid GetUserId() => UserId;

    public bool IsAuthenticated() => UserId != Guid.Empty;
}
