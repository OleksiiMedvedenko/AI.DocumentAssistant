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

    public OrganizationsController(IOrganizationService organizationService)
    {
        _organizationService = organizationService;
    }

    [HttpGet]
    public async Task<IActionResult> GetMine(CancellationToken cancellationToken)
    {
        return Ok(await _organizationService.GetMineAsync(cancellationToken));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateOrganizationRequest request, CancellationToken cancellationToken)
    {
        return Ok(await _organizationService.CreateAsync(new CreateOrganizationRequestDto { Name = request.Name }, cancellationToken));
    }

    [HttpGet("{organizationId:guid}")]
    public async Task<IActionResult> GetById(Guid organizationId, CancellationToken cancellationToken)
    {
        return Ok(await _organizationService.GetByIdAsync(organizationId, cancellationToken));
    }

    [HttpPatch("{organizationId:guid}/settings")]
    public async Task<IActionResult> UpdateSettings(Guid organizationId, [FromBody] UpdateOrganizationSettingsRequest request, CancellationToken cancellationToken)
    {
        return Ok(await _organizationService.UpdateSettingsAsync(
            organizationId,
            new UpdateOrganizationSettingsRequestDto
            {
                AllowPrivateDocuments = request.AllowPrivateDocuments,
                AllowPublicShareLinks = request.AllowPublicShareLinks
            },
            cancellationToken));
    }

    [HttpGet("{organizationId:guid}/members")]
    public async Task<IActionResult> GetMembers(Guid organizationId, CancellationToken cancellationToken)
    {
        return Ok(await _organizationService.GetMembersAsync(organizationId, cancellationToken));
    }

    [HttpPost("{organizationId:guid}/invitations")]
    public async Task<IActionResult> InviteMember(Guid organizationId, [FromBody] InviteOrganizationMemberRequest request, CancellationToken cancellationToken)
    {
        return Ok(await _organizationService.InviteMemberAsync(
            organizationId,
            new InviteOrganizationMemberRequestDto { Email = request.Email, Roles = request.Roles },
            cancellationToken));
    }

    [HttpPost("invitations/accept")]
    public async Task<IActionResult> AcceptInvitation([FromBody] AcceptOrganizationInvitationRequest request, CancellationToken cancellationToken)
    {
        return Ok(await _organizationService.AcceptInvitationAsync(
            new AcceptOrganizationInvitationRequestDto { Code = request.Code },
            cancellationToken));
    }

    [HttpPatch("{organizationId:guid}/members/{memberId:guid}/roles")]
    public async Task<IActionResult> UpdateMemberRoles(Guid organizationId, Guid memberId, [FromBody] UpdateOrganizationMemberRolesRequest request, CancellationToken cancellationToken)
    {
        return Ok(await _organizationService.UpdateMemberRolesAsync(
            organizationId,
            memberId,
            new UpdateOrganizationMemberRolesRequestDto { Roles = request.Roles },
            cancellationToken));
    }

    [HttpDelete("{organizationId:guid}/members/{memberId:guid}")]
    public async Task<IActionResult> RemoveMember(Guid organizationId, Guid memberId, CancellationToken cancellationToken)
    {
        await _organizationService.RemoveMemberAsync(organizationId, memberId, cancellationToken);
        return NoContent();
    }

    [HttpGet("{organizationId:guid}/roles")]
    public async Task<IActionResult> GetRoles(Guid organizationId, CancellationToken cancellationToken)
    {
        return Ok(await _organizationService.GetRolesAsync(organizationId, cancellationToken));
    }

    [HttpGet("{organizationId:guid}/teams")]
    public async Task<IActionResult> GetTeams(Guid organizationId, CancellationToken cancellationToken)
    {
        return Ok(await _organizationService.GetTeamsAsync(organizationId, cancellationToken));
    }

    [HttpPost("{organizationId:guid}/teams")]
    public async Task<IActionResult> CreateTeam(Guid organizationId, [FromBody] CreateTeamRequest request, CancellationToken cancellationToken)
    {
        return Ok(await _organizationService.CreateTeamAsync(organizationId, new CreateTeamRequestDto { Name = request.Name }, cancellationToken));
    }

    [HttpPost("{organizationId:guid}/teams/{teamId:guid}/members")]
    public async Task<IActionResult> AddTeamMember(Guid organizationId, Guid teamId, [FromBody] AddTeamMemberRequest request, CancellationToken cancellationToken)
    {
        await _organizationService.AddTeamMemberAsync(
            organizationId,
            teamId,
            new AddTeamMemberRequestDto { OrganizationMemberId = request.OrganizationMemberId },
            cancellationToken);
        return NoContent();
    }

    [HttpDelete("{organizationId:guid}/teams/{teamId:guid}/members/{memberId:guid}")]
    public async Task<IActionResult> RemoveTeamMember(Guid organizationId, Guid teamId, Guid memberId, CancellationToken cancellationToken)
    {
        await _organizationService.RemoveTeamMemberAsync(organizationId, teamId, memberId, cancellationToken);
        return NoContent();
    }

    [HttpGet("{organizationId:guid}/access-grants")]
    public async Task<IActionResult> GetAccessGrants(Guid organizationId, [FromQuery] string resourceType, [FromQuery] Guid resourceId, CancellationToken cancellationToken)
    {
        return Ok(await _organizationService.GetAccessGrantsAsync(organizationId, resourceType, resourceId, cancellationToken));
    }

    [HttpPost("{organizationId:guid}/access-grants")]
    public async Task<IActionResult> GrantAccess(Guid organizationId, [FromBody] GrantAccessRequest request, CancellationToken cancellationToken)
    {
        return Ok(await _organizationService.GrantAccessAsync(
            organizationId,
            new GrantAccessRequestDto
            {
                UserId = request.UserId,
                TeamId = request.TeamId,
                Role = request.Role,
                ResourceType = request.ResourceType,
                ResourceId = request.ResourceId,
                PermissionKey = request.PermissionKey
            },
            cancellationToken));
    }

    [HttpDelete("{organizationId:guid}/access-grants/{grantId:guid}")]
    public async Task<IActionResult> RevokeAccess(Guid organizationId, Guid grantId, CancellationToken cancellationToken)
    {
        await _organizationService.RevokeAccessAsync(organizationId, grantId, cancellationToken);
        return NoContent();
    }

    [HttpGet("{organizationId:guid}/audit-logs")]
    public async Task<IActionResult> GetAuditLogs(Guid organizationId, CancellationToken cancellationToken)
    {
        return Ok(await _organizationService.GetAuditLogsAsync(organizationId, cancellationToken));
    }
}
