using System.Text;
using AI.DocumentAssistant.Application.Abstractions.Documents;

namespace AI.DocumentAssistant.Application.Services.DocumentProcessing;

public sealed class PlainTextDocumentTextExtractor : IDocumentTextExtractor
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt",
        ".md",
        ".markdown",
        ".log",
        ".json",
        ".xml"
    };

    public bool CanHandle(string fileName, string? contentType)
    {
        var extension = Path.GetExtension(fileName);
        return SupportedExtensions.Contains(extension)
               || string.Equals(contentType, "text/plain", StringComparison.OrdinalIgnoreCase)
               || string.Equals(contentType, "text/markdown", StringComparison.OrdinalIgnoreCase)
               || string.Equals(contentType, "application/json", StringComparison.OrdinalIgnoreCase)
               || string.Equals(contentType, "application/xml", StringComparison.OrdinalIgnoreCase)
               || string.Equals(contentType, "text/xml", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<string> ExtractTextAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, true, leaveOpen: false);
        return await reader.ReadToEndAsync(cancellationToken);
    }
}