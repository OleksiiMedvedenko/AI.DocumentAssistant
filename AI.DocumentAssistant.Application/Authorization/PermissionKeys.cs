namespace AI.DocumentAssistant.Application.Authorization;

public static class PermissionKeys
{
    public const string DocumentsView = "documents.view";
    public const string DocumentsUpload = "documents.upload";
    public const string DocumentsEdit = "documents.edit";
    public const string DocumentsDelete = "documents.delete";
    public const string DocumentsShare = "documents.share";
    public const string DocumentsExport = "documents.export";
    public const string DocumentsApprove = "documents.approve";
    public const string DocumentsReject = "documents.reject";

    public const string FoldersView = "folders.view";
    public const string FoldersCreate = "folders.create";
    public const string FoldersEdit = "folders.edit";
    public const string FoldersDelete = "folders.delete";
    public const string FoldersManageAccess = "folders.manageAccess";

    public const string OrganizationViewMembers = "organization.viewMembers";
    public const string OrganizationInviteMembers = "organization.inviteMembers";
    public const string OrganizationRemoveMembers = "organization.removeMembers";
    public const string OrganizationManageRoles = "organization.manageRoles";
    public const string OrganizationManageSettings = "organization.manageSettings";

    public const string AiActionsRun = "aiActions.run";
    public const string AiActionsCreateTemplate = "aiActions.createTemplate";
    public const string AiActionsManageOrgTemplates = "aiActions.manageOrgTemplates";
    public const string AiActionsManageGlobalTemplates = "aiActions.manageGlobalTemplates";

    public const string WorkflowsView = "workflows.view";
    public const string WorkflowsCreate = "workflows.create";
    public const string WorkflowsEdit = "workflows.edit";
    public const string WorkflowsDelete = "workflows.delete";
    public const string WorkflowsAssignApprover = "workflows.assignApprover";

    public const string AdminViewOrganizations = "admin.viewOrganizations";
    public const string AdminManageUsers = "admin.manageUsers";
    public const string AdminManagePlans = "admin.managePlans";
    public const string AdminManageSystemSettings = "admin.manageSystemSettings";
    public const string AdminViewAuditLogs = "admin.viewAuditLogs";

    public static IReadOnlyDictionary<string, string> All { get; } = new Dictionary<string, string>
    {
        [DocumentsView] = "View documents visible to the member.",
        [DocumentsUpload] = "Upload documents.",
        [DocumentsEdit] = "Edit document metadata and classification.",
        [DocumentsDelete] = "Delete documents.",
        [DocumentsShare] = "Share documents with other members or teams.",
        [DocumentsExport] = "Download or export documents.",
        [DocumentsApprove] = "Approve documents in workflows.",
        [DocumentsReject] = "Reject documents in workflows.",
        [FoldersView] = "View folders.",
        [FoldersCreate] = "Create folders.",
        [FoldersEdit] = "Edit folders.",
        [FoldersDelete] = "Delete folders.",
        [FoldersManageAccess] = "Manage folder access grants.",
        [OrganizationViewMembers] = "View organization members.",
        [OrganizationInviteMembers] = "Invite organization members.",
        [OrganizationRemoveMembers] = "Remove organization members.",
        [OrganizationManageRoles] = "Manage organization roles and permissions.",
        [OrganizationManageSettings] = "Manage organization settings.",
        [AiActionsRun] = "Run AI actions.",
        [AiActionsCreateTemplate] = "Create private AI action templates.",
        [AiActionsManageOrgTemplates] = "Manage organization AI action templates.",
        [AiActionsManageGlobalTemplates] = "Manage global AI action templates.",
        [WorkflowsView] = "View workflows.",
        [WorkflowsCreate] = "Create workflows.",
        [WorkflowsEdit] = "Edit workflows.",
        [WorkflowsDelete] = "Delete workflows.",
        [WorkflowsAssignApprover] = "Assign workflow approvers.",
        [AdminViewOrganizations] = "View organizations in the system admin panel.",
        [AdminManageUsers] = "Manage users in the system admin panel.",
        [AdminManagePlans] = "Manage plans and quotas.",
        [AdminManageSystemSettings] = "Manage system settings.",
        [AdminViewAuditLogs] = "View audit logs."
    };
}
