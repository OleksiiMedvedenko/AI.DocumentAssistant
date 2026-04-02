using AI.DocumentAssistant.Application.Abstractions.Common;

namespace AI.DocumentAssistant.Application.Services.Time;

public sealed class SystemClock : ISystemClock
{
    public DateTime UtcNow => DateTime.UtcNow;
}