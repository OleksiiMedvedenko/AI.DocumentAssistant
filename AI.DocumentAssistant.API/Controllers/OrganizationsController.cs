using AI.DocumentAssistant.API.Contracts.Organizations;
using AI.DocumentAssistant.Application.Abstractions.Organizations;
using AI.DocumentAssistant.Application.Organizations.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AI.DocumentAssistant.API.Controllers;

[ApiController]
[Authorize]
[Route("api/organizations")]
public sealed class OrganizationsController : ControllerBase
{
    private readonly IOrganizationService _organizationService;
    private readonly IOrganizationInvitationService _organizationInvitationService;

    public OrganizationsController(
        IOrganizationService organizationService,
        IOrganizationInvitationService organizationInvitationService)
    {
        _organizationService = organizationService;
        _organizationInvitationService = organizationInvitationService;
    }

    [HttpGet]
    public async Task<IActionResult> GetOrganizations(CancellationToken cancellationToken)
    {
        var result = await _organizationService.GetMyOrganizationsAsync(cancellationToken);
        return Ok(result.Select(Map));
    }

    [HttpGet("my")]
    public async Task<IActionResult> GetMyOrganizations(CancellationToken cancellationToken)
    {
        var result = await _organizationService.GetMyOrganizationsAsync(cancellationToken);
        return Ok(result.Select(Map));
    }

    [HttpGet("{organizationId:guid}")]
    public async Task<IActionResult> GetById(
        Guid organizationId,
        CancellationToken cancellationToken)
    {
        var result = await _organizationService.GetByIdAsync(organizationId, cancellationToken);
        return Ok(Map(result));
    }

    [Authorize(Roles = "Admin,Manager")]
    [HttpPost]
    public async Task<IActionResult> Create(
        CreateOrganizationRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _organizationService.CreateAsync(new CreateOrganizationDto
        {
            Name = request.Name
        }, cancellationToken);

        return CreatedAtAction(nameof(GetById), new { organizationId = result.Id }, Map(result));
    }

    [HttpPut("{organizationId:guid}")]
    public async Task<IActionResult> Update(
        Guid organizationId,
        UpdateOrganizationRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _organizationService.UpdateAsync(organizationId, new UpdateOrganizationDto
        {
            Name = request.Name
        }, cancellationToken);

        return Ok(Map(result));
    }

    [HttpDelete("{organizationId:guid}")]
    public async Task<IActionResult> Delete(
        Guid organizationId,
        CancellationToken cancellationToken)
    {
        await _organizationService.DeleteAsync(organizationId, cancellationToken);
        return NoContent();
    }


    [HttpPatch("{organizationId:guid}/reactivate")]
    public async Task<IActionResult> Reactivate(
        Guid organizationId,
        CancellationToken cancellationToken)
    {
        var result = await _organizationService.ReactivateAsync(organizationId, cancellationToken);
        return Ok(Map(result));
    }

    [HttpGet("{organizationId:guid}/members")]
    public async Task<IActionResult> GetMembers(
        Guid organizationId,
        CancellationToken cancellationToken)
    {
        var result = await _organizationService.GetMembersAsync(organizationId, cancellationToken);
        return Ok(result.Select(Map));
    }

    [HttpPost("{organizationId:guid}/members")]
    public async Task<IActionResult> AddMember(
        Guid organizationId,
        AddOrganizationMemberRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _organizationService.AddMemberAsync(organizationId, new AddOrganizationMemberDto
        {
            UserId = request.UserId,
            Email = request.Email
        }, cancellationToken);

        return Ok(Map(result));
    }

    [HttpDelete("{organizationId:guid}/members/{userId:guid}")]
    public async Task<IActionResult> RemoveMember(
        Guid organizationId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        await _organizationService.RemoveMemberAsync(organizationId, userId, cancellationToken);
        return NoContent();
    }


    [HttpGet("{organizationId:guid}/invitations")]
    public async Task<IActionResult> GetInvitations(
        Guid organizationId,
        [FromQuery] bool includeHistory,
        CancellationToken cancellationToken)
    {
        var result = await _organizationInvitationService.GetOrganizationInvitationsAsync(organizationId, includeHistory, cancellationToken);
        return Ok(result.Select(Map));
    }

    [HttpPost("{organizationId:guid}/invitations")]
    public async Task<IActionResult> CreateInvitation(
        Guid organizationId,
        CreateOrganizationInvitationRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _organizationInvitationService.CreateInvitationAsync(organizationId, new CreateOrganizationInvitationDto
        {
            Email = request.Email
        }, cancellationToken);

        return Ok(Map(result));
    }

    [HttpDelete("{organizationId:guid}/invitations/{invitationId:guid}")]
    public async Task<IActionResult> RevokeInvitation(
        Guid organizationId,
        Guid invitationId,
        CancellationToken cancellationToken)
    {
        await _organizationInvitationService.RevokeInvitationAsync(organizationId, invitationId, cancellationToken);
        return NoContent();
    }

    [HttpPost("invitations/{invitationId:guid}/accept")]
    public async Task<IActionResult> AcceptInvitation(
        Guid invitationId,
        CancellationToken cancellationToken)
    {
        var result = await _organizationInvitationService.AcceptInvitationAsync(invitationId, cancellationToken);
        return Ok(Map(result));
    }

    [HttpPost("invitations/accept-by-code")]
    public async Task<IActionResult> AcceptInvitationByCode(
        AcceptOrganizationInvitationByCodeRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _organizationInvitationService.AcceptInvitationByCodeAsync(request.Code, cancellationToken);
        return Ok(Map(result));
    }

    [HttpGet("{organizationId:guid}/settings")]
    public async Task<IActionResult> GetSettings(
        Guid organizationId,
        CancellationToken cancellationToken)
    {
        var result = await _organizationInvitationService.GetSettingsAsync(organizationId, cancellationToken);
        return Ok(Map(result));
    }

    [HttpPatch("{organizationId:guid}/settings")]
    public async Task<IActionResult> UpdateSettings(
        Guid organizationId,
        UpdateOrganizationSettingsRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _organizationInvitationService.UpdateSettingsAsync(organizationId, new UpdateOrganizationSettingsDto
        {
            InvitationLifetimeDays = request.InvitationLifetimeDays
        }, cancellationToken);

        return Ok(Map(result));
    }

    [HttpGet("{organizationId:guid}/activity-logs")]
    public async Task<IActionResult> GetActivityLogs(
        Guid organizationId,
        CancellationToken cancellationToken)
    {
        var result = await _organizationInvitationService.GetActivityLogsAsync(organizationId, cancellationToken);
        return Ok(result.Select(Map));
    }

    private static OrganizationResponse Map(OrganizationDto dto)
    {
        return new OrganizationResponse
        {
            Id = dto.Id,
            Name = dto.Name,
            CreatedByUserId = dto.CreatedByUserId,
            CreatedByEmail = dto.CreatedByEmail,
            CreatedAtUtc = dto.CreatedAtUtc,
            UpdatedAtUtc = dto.UpdatedAtUtc,
            IsActive = dto.IsActive,
            ActiveMembersCount = dto.ActiveMembersCount,
            CanManage = dto.CanManage
        };
    }

    private static OrganizationInvitationResponse Map(OrganizationInvitationDto dto)
    {
        return new OrganizationInvitationResponse
        {
            Id = dto.Id,
            OrganizationId = dto.OrganizationId,
            OrganizationName = dto.OrganizationName,
            Email = dto.Email,
            InvitedUserId = dto.InvitedUserId,
            InvitedUserEmail = dto.InvitedUserEmail,
            InvitedByUserId = dto.InvitedByUserId,
            InvitedByEmail = dto.InvitedByEmail,
            Status = dto.Status,
            CreatedAtUtc = dto.CreatedAtUtc,
            ExpiresAtUtc = dto.ExpiresAtUtc,
            AcceptedAtUtc = dto.AcceptedAtUtc,
            RevokedAtUtc = dto.RevokedAtUtc
        };
    }

    private static OrganizationInvitationAcceptResponse Map(OrganizationInvitationAcceptResultDto dto)
    {
        return new OrganizationInvitationAcceptResponse
        {
            OrganizationId = dto.OrganizationId,
            OrganizationName = dto.OrganizationName,
            JoinedAtUtc = dto.JoinedAtUtc
        };
    }

    private static OrganizationSettingsResponse Map(OrganizationSettingsDto dto)
    {
        return new OrganizationSettingsResponse
        {
            OrganizationId = dto.OrganizationId,
            InvitationLifetimeDays = dto.InvitationLifetimeDays
        };
    }

    private static OrganizationActivityLogResponse Map(OrganizationActivityLogDto dto)
    {
        return new OrganizationActivityLogResponse
        {
            Id = dto.Id,
            OrganizationId = dto.OrganizationId,
            ActorUserId = dto.ActorUserId,
            ActorEmail = dto.ActorEmail,
            ActionType = dto.ActionType,
            PayloadJson = dto.PayloadJson,
            CreatedAtUtc = dto.CreatedAtUtc
        };
    }

    private static OrganizationMemberResponse Map(OrganizationMemberDto dto)
    {
        return new OrganizationMemberResponse
        {
            Id = dto.Id,
            OrganizationId = dto.OrganizationId,
            UserId = dto.UserId,
            Email = dto.Email,
            DisplayName = dto.DisplayName,
            UserRole = dto.UserRole,
            AddedByUserId = dto.AddedByUserId,
            JoinedAtUtc = dto.JoinedAtUtc,
            RemovedAtUtc = dto.RemovedAtUtc,
            IsActive = dto.IsActive
        };
    }
}
