using AI.DocumentAssistant.Application.Abstractions.Documents;
using CsvHelper;
using System.Globalization;
using System.Text;

namespace AI.DocumentAssistant.Application.Services.DocumentProcessing;

public sealed class CsvDocumentTextExtractor : IDocumentTextExtractor
{
    public bool CanHandle(string fileName, string? contentType)
    {
        var extension = Path.GetExtension(fileName);
        return string.Equals(extension, ".csv", StringComparison.OrdinalIgnoreCase)
               || string.Equals(contentType, "text/csv", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<string> ExtractTextAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, true, leaveOpen: false);
        using var csv = new CsvReader(reader, CultureInfo.InvariantCulture);

        var sb = new StringBuilder();

        await foreach (var record in csv.GetRecordsAsync<dynamic>(cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (record is IDictionary<string, object> dict)
            {
                foreach (var pair in dict)
                {
                    sb.Append(pair.Key);
                    sb.Append(": ");
                    sb.Append(pair.Value?.ToString());
                    sb.Append(" | ");
                }

                sb.AppendLine();
            }
            else
            {
                sb.AppendLine(record?.ToString());
            }
        }

        return sb.ToString();
    }
}