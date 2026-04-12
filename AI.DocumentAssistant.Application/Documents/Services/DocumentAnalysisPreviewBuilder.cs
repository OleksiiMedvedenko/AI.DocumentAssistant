using System.Text;

namespace AI.DocumentAssistant.Application.Documents.Services;

public static class DocumentAnalysisPreviewBuilder
{
    public static string Build(string? extractedText, int maxChars = 3500)
    {
        if (string.IsNullOrWhiteSpace(extractedText))
        {
            return string.Empty;
        }

        var text = extractedText.Trim();
        if (text.Length <= maxChars)
        {
            return text;
        }

        var firstLength = Math.Min(1500, text.Length);
        var lastLength = Math.Min(900, Math.Max(0, text.Length - firstLength));
        var remaining = Math.Max(0, maxChars - firstLength - lastLength - 32);

        var middleStart = Math.Max(0, (text.Length / 2) - (remaining / 2));
        var middleLength = Math.Max(0, Math.Min(remaining, text.Length - middleStart));

        var builder = new StringBuilder(maxChars + 128);
        builder.AppendLine(text[..firstLength]);

        if (middleLength > 0)
        {
            builder.AppendLine();
            builder.AppendLine("[...]");
            builder.AppendLine(text.Substring(middleStart, middleLength));
        }

        if (lastLength > 0)
        {
            builder.AppendLine();
            builder.AppendLine("[...]");
            builder.AppendLine(text[^lastLength..]);
        }

        return builder.ToString().Trim();
    }
}