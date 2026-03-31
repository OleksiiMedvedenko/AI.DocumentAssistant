namespace AI.DocumentAssistant.Abstraction.Abstractions.Common
{
    public interface IDateTimeProvider
    {
        DateTime UtcNow { get; }
    }
}
