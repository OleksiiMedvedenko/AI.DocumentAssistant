namespace AI.DocumentAssistant.Application.Common.Exceptions;

public sealed class QuotaExceededException : Exception
{
    public QuotaExceededException(string message) : base(message)
    {
    }
}