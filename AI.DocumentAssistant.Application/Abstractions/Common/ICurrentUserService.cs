namespace AI.DocumentAssistant.Application.Abstractions.Common
{
    public interface ICurrentUserService
    {
        Guid GetUserId();
        bool IsAuthenticated();
    }
}
