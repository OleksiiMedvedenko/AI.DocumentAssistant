namespace AI.DocumentAssistant.Application.Services.Communication;

public sealed class SmtpOptions
{
    public const string SectionName = "Smtp";

    public string Host { get; set; } = "smtp.gmail.com";
    public int Port { get; set; } = 587;
    public string UserName { get; set; } = default!;
    public string Password { get; set; } = default!;
    public string FromEmail { get; set; } = default!;
    public string FromName { get; set; } = "AI Document Assistant";
    public bool UseStartTls { get; set; } = true;
    public int ConnectTimeoutMilliseconds { get; set; } = 10000;
    public int OperationTimeoutMilliseconds { get; set; } = 10000;
    public int MaxRetryAttempts { get; set; } = 3;
    public int RetryBaseDelayMilliseconds { get; set; } = 800;
}