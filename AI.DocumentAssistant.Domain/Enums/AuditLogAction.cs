namespace AI.DocumentAssistant.Domain.Enums;

public enum AuditLogAction
{
    OrganizationCreated = 0,
    MemberInvited = 1,
    InvitationAccepted = 2,
    MemberRemoved = 3,
    MemberRoleChanged = 4,
    TeamCreated = 5,
    TeamMemberAdded = 6,
    TeamMemberRemoved = 7,
    AccessGranted = 8,
    AccessRevoked = 9,
    DocumentCreated = 10,
    DocumentViewed = 11,
    DocumentDeleted = 12,
    FolderCreated = 13,
    FolderUpdated = 14,
    FolderDeleted = 15
}
