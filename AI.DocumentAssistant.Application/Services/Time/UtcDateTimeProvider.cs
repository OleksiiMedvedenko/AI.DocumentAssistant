using AI.DocumentAssistant.Application.Abstractions.Common;

namespace AI.DocumentAssistant.Application.Services.Time;

public sealed class UtcDateTimeProvider : IDateTimeProvider
{
    public DateTime UtcNow => DateTime.UtcNow;
}