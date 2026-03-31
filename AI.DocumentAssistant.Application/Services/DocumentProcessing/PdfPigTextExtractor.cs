using AI.DocumentAssistant.Application.Abstractions.Documents;
using System.Text;
using UglyToad.PdfPig;

namespace AI.DocumentAssistant.Application.Services.DocumentProcessing
{
    public sealed class PdfPigTextExtractor : IPdfTextExtractor
    {
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
}
