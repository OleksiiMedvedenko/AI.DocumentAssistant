using System.Text;
using AI.DocumentAssistant.Application.Abstractions.Documents;
using UglyToad.PdfPig;

namespace AI.DocumentAssistant.Application.Services.DocumentProcessing;

public sealed class PdfPigTextExtractor : IPdfTextExtractor, IDocumentTextExtractor
{
    public bool CanHandle(string fileName, string? contentType)
    {
        var extension = Path.GetExtension(fileName);
        return string.Equals(extension, ".pdf", StringComparison.OrdinalIgnoreCase)
               || string.Equals(contentType, "application/pdf", StringComparison.OrdinalIgnoreCase);
    }

    public Task<string> ExtractTextAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var document = PdfDocument.Open(stream);
        var sb = new StringBuilder();

        foreach (var page in document.GetPages())
        {
            cancellationToken.ThrowIfCancellationRequested();
            sb.AppendLine(page.Text);
        }

        return Task.FromResult(sb.ToString());
    }
}