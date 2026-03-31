namespace AI.DocumentAssistant.API.Contracts.Common
{
    public sealed class ApiErrorResponse
    {
        public int StatusCode { get; set; }
        public string Message { get; set; } = default!;
        public string? Details { get; set; }
    }
}
