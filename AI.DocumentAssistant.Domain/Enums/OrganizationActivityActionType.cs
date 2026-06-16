namespace AI.DocumentAssistant.Domain.Enums;

public enum OrganizationActivityActionType
{
    OrganizationCreated = 0,
    OrganizationUpdated = 1,
    OrganizationDeactivated = 2,
    OrganizationReactivated = 3,
    OrganizationInvitationCreated = 10,
    OrganizationInvitationAccepted = 11,
    OrganizationInvitationRevoked = 12,
    OrganizationMemberJoined = 20,
    OrganizationMemberRemoved = 21,
    OrganizationSettingsUpdated = 30
}
