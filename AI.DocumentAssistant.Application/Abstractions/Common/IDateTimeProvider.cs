namespace AI.DocumentAssistant.Application.Abstractions.Common
{
    public interface IDateTimeProvider
    {
        DateTime UtcNow { get; }
    }
}
