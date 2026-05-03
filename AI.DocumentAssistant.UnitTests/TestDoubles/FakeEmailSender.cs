using AI.DocumentAssistant.Application.Abstractions.Communication;

namespace AI.DocumentAssistant.UnitTests.TestDoubles;

public sealed class FakeEmailSender : IEmailSender
{
    private readonly List<SentEmail> _sentEmails = new();

    public IReadOnlyList<SentEmail> SentEmails
    {
        get
        {
            lock (_sentEmails)
            {
                return _sentEmails.ToList();
            }
        }
    }

    public Task SendAsync(
        string toEmail,
        string subject,
        string htmlBody,
        CancellationToken cancellationToken)
    {
        lock (_sentEmails)
        {
            _sentEmails.Add(new SentEmail(
                toEmail,
                subject,
                htmlBody,
                DateTime.UtcNow));
        }

        return Task.CompletedTask;
    }

    public void Clear()
    {
        lock (_sentEmails)
        {
            _sentEmails.Clear();
        }
    }

    public sealed record SentEmail(
        string ToEmail,
        string Subject,
        string HtmlBody,
        DateTime SentAtUtc);
}
