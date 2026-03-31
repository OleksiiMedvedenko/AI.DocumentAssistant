namespace AI.DocumentAssistant.Abstraction.Abstractions.Common
{
    public interface ICurrentUserService
    {
        Guid GetUserId();
        bool IsAuthenticated();
    }
}
