namespace AI.DocumentAssistant.Application.Common.Exceptions
{
    public sealed class NotFoundException : Exception
    {
        public NotFoundException(string message) : base(message)
        {
        }
    }
}
