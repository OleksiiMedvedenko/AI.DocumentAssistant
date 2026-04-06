namespace AI.DocumentAssistant.API.Contracts.Admin;

public sealed class UpdateUserRoleRequest
{
    public string Role { get; set; } = default!;
}