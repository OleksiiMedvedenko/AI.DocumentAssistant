namespace AI.DocumentAssistant.API.Contracts.Admin;

public sealed class UpdateUserActiveStatusRequest
{
    public bool IsActive { get; set; }
}