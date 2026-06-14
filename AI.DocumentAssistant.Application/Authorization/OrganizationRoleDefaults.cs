namespace AI.DocumentAssistant.Application.Authorization;

public static class OrganizationRoleDefaults
{
    public const string Owner = "Owner";
    public const string Admin = "OrgAdmin";
    public const string Manager = "Manager";
    public const string Approver = "Approver";
    public const string Member = "Member";
    public const string Viewer = "Viewer";

    public static IReadOnlyDictionary<string, string[]> PermissionsByRole { get; } = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
    {
        [Owner] = PermissionKeys.All.Keys.ToArray(),
        [Admin] = new[]
        {
            PermissionKeys.DocumentsView, PermissionKeys.DocumentsUpload, PermissionKeys.DocumentsEdit,
            PermissionKeys.DocumentsDelete, PermissionKeys.DocumentsShare, PermissionKeys.DocumentsExport,
            PermissionKeys.FoldersView, PermissionKeys.FoldersCreate, PermissionKeys.FoldersEdit,
            PermissionKeys.FoldersDelete, PermissionKeys.FoldersManageAccess,
            PermissionKeys.OrganizationViewMembers, PermissionKeys.OrganizationInviteMembers,
            PermissionKeys.OrganizationRemoveMembers, PermissionKeys.OrganizationManageRoles,
            PermissionKeys.OrganizationManageSettings,
            PermissionKeys.AiActionsRun, PermissionKeys.AiActionsCreateTemplate,
            PermissionKeys.AiActionsManageOrgTemplates,
            PermissionKeys.WorkflowsView, PermissionKeys.WorkflowsCreate,
            PermissionKeys.WorkflowsEdit, PermissionKeys.WorkflowsDelete,
            PermissionKeys.WorkflowsAssignApprover
        },
        [Manager] = new[]
        {
            PermissionKeys.DocumentsView, PermissionKeys.DocumentsUpload, PermissionKeys.DocumentsEdit,
            PermissionKeys.DocumentsShare, PermissionKeys.DocumentsExport,
            PermissionKeys.FoldersView, PermissionKeys.FoldersCreate, PermissionKeys.FoldersEdit,
            PermissionKeys.OrganizationViewMembers,
            PermissionKeys.AiActionsRun, PermissionKeys.AiActionsCreateTemplate,
            PermissionKeys.WorkflowsView, PermissionKeys.WorkflowsAssignApprover,
            PermissionKeys.DocumentsApprove, PermissionKeys.DocumentsReject
        },
        [Approver] = new[]
        {
            PermissionKeys.DocumentsView, PermissionKeys.DocumentsExport,
            PermissionKeys.DocumentsApprove, PermissionKeys.DocumentsReject,
            PermissionKeys.FoldersView, PermissionKeys.AiActionsRun, PermissionKeys.WorkflowsView
        },
        [Member] = new[]
        {
            PermissionKeys.DocumentsView, PermissionKeys.DocumentsUpload, PermissionKeys.DocumentsEdit,
            PermissionKeys.FoldersView, PermissionKeys.FoldersCreate,
            PermissionKeys.AiActionsRun, PermissionKeys.AiActionsCreateTemplate,
            PermissionKeys.WorkflowsView
        },
        [Viewer] = new[]
        {
            PermissionKeys.DocumentsView, PermissionKeys.FoldersView, PermissionKeys.WorkflowsView
        }
    };
}
