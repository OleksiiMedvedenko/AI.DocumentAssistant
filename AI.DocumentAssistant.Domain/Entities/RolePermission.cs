namespace AI.DocumentAssistant.Domain.Entities;

public sealed class RolePermission
{
    public Guid RoleId { get; set; }
    public Guid PermissionId { get; set; }

    public Role Role { get; set; } = default!;
    public Permission Permission { get; set; } = default!;
}
