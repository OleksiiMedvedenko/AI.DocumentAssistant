namespace AI.DocumentAssistant.Application.Abstractions.Common;

public interface ISystemClock
{
    DateTime UtcNow { get; }
}