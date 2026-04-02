using System.Text;
using AI.DocumentAssistant.Application.Abstractions.Documents;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace AI.DocumentAssistant.Application.Services.DocumentProcessing;

public sealed class DocxDocumentTextExtractor : IDocumentTextExtractor
{
    public bool CanHandle(string fileName, string? contentType)
    {
        var extension = Path.GetExtension(fileName);

        return string.Equals(extension, ".docx", StringComparison.OrdinalIgnoreCase)
               || string.Equals(
                   contentType,
                   "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                   StringComparison.OrdinalIgnoreCase);
    }

    public Task<string> ExtractTextAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var memoryStream = new MemoryStream();
        stream.CopyTo(memoryStream);
        memoryStream.Position = 0;

        using var document = WordprocessingDocument.Open(memoryStream, false);
        var body = document.MainDocumentPart?.Document?.Body;

        if (body is null)
        {
            return Task.FromResult(string.Empty);
        }

        var sb = new StringBuilder();

        foreach (var paragraph in body.Descendants<Paragraph>())
        {
            cancellationToken.ThrowIfCancellationRequested();

            var text = paragraph.InnerText?.Trim();
            if (!string.IsNullOrWhiteSpace(text))
            {
                sb.AppendLine(text);
            }
        }

        return Task.FromResult(sb.ToString());
    }
}