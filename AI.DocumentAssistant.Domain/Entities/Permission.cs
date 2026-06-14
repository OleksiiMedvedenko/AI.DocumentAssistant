namespace AI.DocumentAssistant.Domain.Entities;

public sealed class Permission
{
    public Guid Id { get; set; }
    public string Key { get; set; } = default!;
    public string Description { get; set; } = default!;
    public ICollection<RolePermission> Roles { get; set; } = new List<RolePermission>();
}
