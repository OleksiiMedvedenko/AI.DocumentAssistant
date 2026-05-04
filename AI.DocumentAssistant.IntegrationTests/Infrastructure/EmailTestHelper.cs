using AI.DocumentAssistant.IntegrationTests.TestDoubles;
using System.Net;
using System.Text.RegularExpressions;

namespace AI.DocumentAssistant.IntegrationTests.Infrastructure;

public static class EmailTestHelper
{
    public static string ExtractConfirmationToken(FakeEmailSender.SentEmail email)
    {
        var html = WebUtility.HtmlDecode(email.HtmlBody);
        var match = Regex.Match(html, "[?&]token=([^&\"'\\s<>]+)", RegexOptions.IgnoreCase);

        if (!match.Success)
        {
            throw new InvalidOperationException($"Confirmation token was not found in email body: {email.HtmlBody}");
        }

        return WebUtility.UrlDecode(match.Groups[1].Value);
    }
}
