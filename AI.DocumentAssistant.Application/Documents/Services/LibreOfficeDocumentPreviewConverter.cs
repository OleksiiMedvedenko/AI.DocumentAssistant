using AI.DocumentAssistant.Application.Abstractions.Documents;
using DocumentFormat.OpenXml.Drawing.Charts;
using DocumentFormat.OpenXml.Spreadsheet;
using DocumentFormat.OpenXml.Wordprocessing;
using System.IO.Compression;
using System.Text;
using System.Xml.Linq;

namespace AI.DocumentAssistant.Application.Documents.Services
{
    public sealed class LibreOfficeDocumentPreviewConverter : IDocumentPreviewConverter
    {
        private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

        public async Task<(Stream Stream, string ContentType, string FileName)> ConvertToPreviewAsync(
            string sourcePath,
            string originalFileName,
            string contentType,
            CancellationToken cancellationToken)
        {
            var extension = Path.GetExtension(originalFileName).ToLowerInvariant();

            if (string.Equals(contentType, "application/pdf", StringComparison.OrdinalIgnoreCase) ||
                extension == ".pdf")
            {
                var pdfStream = File.OpenRead(sourcePath);
                return (pdfStream, "application/pdf", originalFileName);
            }

            if (extension is ".txt" or ".md" or ".json" or ".xml" or ".csv" or ".log")
            {
                var textStream = File.OpenRead(sourcePath);
                return (textStream, "text/plain; charset=utf-8", originalFileName);
            }

            if (extension == ".docx")
            {
                var html = await ConvertDocxToHtmlAsync(sourcePath, originalFileName, cancellationToken);
                var bytes = Encoding.UTF8.GetBytes(html);
                var stream = new MemoryStream(bytes);
                return (stream, "text/html; charset=utf-8", Path.GetFileNameWithoutExtension(originalFileName) + ".html");
            }

            var originalStream = File.OpenRead(sourcePath);
            return (
                originalStream,
                string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType,
                originalFileName
            );
        }

        private static async Task<string> ConvertDocxToHtmlAsync(
            string sourcePath,
            string originalFileName,
            CancellationToken cancellationToken)
        {
            await using var fileStream = File.OpenRead(sourcePath);
            using var archive = new ZipArchive(fileStream, ZipArchiveMode.Read, leaveOpen: false);

            var documentEntry = archive.GetEntry("word/document.xml");
            if (documentEntry is null)
            {
                return BuildHtmlDocument(
                    title: originalFileName,
                    bodyHtml: "<p>Nie udało się odczytać zawartości dokumentu DOCX.</p>");
            }

            using var documentStream = documentEntry.Open();
            var xDocument = await XDocument.LoadAsync(documentStream, LoadOptions.None, cancellationToken);

            var body = xDocument.Root?.Element(W + "body");
            if (body is null)
            {
                return BuildHtmlDocument(
                    title: originalFileName,
                    bodyHtml: "<p>Dokument DOCX nie zawiera czytelnej treści.</p>");
            }

            var html = new StringBuilder();

            foreach (var element in body.Elements())
            {
                if (element.Name == W + "p")
                {
                    html.Append(RenderParagraph(element));
                }
                else if (element.Name == W + "tbl")
                {
                    html.Append(RenderTable(element));
                }
            }

            if (html.Length == 0)
            {
                html.Append("<p>Brak treści do podglądu.</p>");
            }

            return BuildHtmlDocument(originalFileName, html.ToString());
        }

        private static string RenderParagraph(XElement paragraph)
        {
            var paragraphProperties = paragraph.Element(W + "pPr");
            var styleId = paragraphProperties?
                .Element(W + "pStyle")?
                .Attribute(W + "val")?
                .Value;

            var isListItem = paragraphProperties?.Element(W + "numPr") is not null;

            var text = new StringBuilder();

            foreach (var node in paragraph.Elements())
            {
                if (node.Name == W + "r")
                {
                    text.Append(RenderRun(node));
                }
                else if (node.Name == W + "hyperlink")
                {
                    foreach (var run in node.Elements(W + "r"))
                    {
                        text.Append(RenderRun(run));
                    }
                }
            }

            var content = text.ToString();

            if (string.IsNullOrWhiteSpace(content))
            {
                return "<div class=\"docx-spacer\"></div>";
            }

            if (IsHeadingStyle(styleId, out var headingLevel))
            {
                return $"<h{headingLevel}>{content}</h{headingLevel}>";
            }

            if (isListItem)
            {
                return $"<li>{content}</li>";
            }

            return $"<p>{content}</p>";
        }

        private static string RenderRun(XElement run)
        {
            var runProperties = run.Element(W + "rPr");

            var isBold = runProperties?.Element(W + "b") is not null;
            var isItalic = runProperties?.Element(W + "i") is not null;
            var isUnderline = runProperties?.Element(W + "u") is not null;

            var content = new StringBuilder();

            foreach (var child in run.Elements())
            {
                if (child.Name == W + "t")
                {
                    content.Append(HtmlEncode(child.Value));
                }
                else if (child.Name == W + "tab")
                {
                    content.Append("&emsp;");
                }
                else if (child.Name == W + "br")
                {
                    content.Append("<br />");
                }
            }

            var value = content.ToString();

            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            if (isBold) value = $"<strong>{value}</strong>";
            if (isItalic) value = $"<em>{value}</em>";
            if (isUnderline) value = $"<u>{value}</u>";

            return value;
        }

        private static string RenderTable(XElement table)
        {
            var html = new StringBuilder();
            html.Append("<table><tbody>");

            foreach (var row in table.Elements(W + "tr"))
            {
                html.Append("<tr>");

                foreach (var cell in row.Elements(W + "tc"))
                {
                    html.Append("<td>");

                    foreach (var cellElement in cell.Elements())
                    {
                        if (cellElement.Name == W + "p")
                        {
                            html.Append(RenderParagraph(cellElement));
                        }
                        else if (cellElement.Name == W + "tbl")
                        {
                            html.Append(RenderTable(cellElement));
                        }
                    }

                    html.Append("</td>");
                }

                html.Append("</tr>");
            }

            html.Append("</tbody></table>");
            return html.ToString();
        }

        private static bool IsHeadingStyle(string? styleId, out int level)
        {
            level = 0;

            if (string.IsNullOrWhiteSpace(styleId))
            {
                return false;
            }

            if (styleId.StartsWith("Heading", StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(styleId["Heading".Length..], out var parsed))
            {
                level = Math.Clamp(parsed, 1, 6);
                return true;
            }

            return false;
        }

        private static string BuildHtmlDocument(string title, string bodyHtml)
        {
            var normalizedBody = NormalizeLists(bodyHtml);

            return $$"""
<!doctype html>
<html lang="pl">
<head>
  <meta charset="utf-8" />
  <meta name="viewport" content="width=device-width,initial-scale=1" />
  <title>{{HtmlEncode(title)}}</title>
  <style>
    body {
      margin: 0;
      padding: 32px;
      font-family: Arial, Helvetica, sans-serif;
      color: #1f2937;
      background: #ffffff;
      line-height: 1.6;
    }

    .docx-root {
      max-width: 960px;
      margin: 0 auto;
    }

    h1, h2, h3, h4, h5, h6 {
      margin: 1.2em 0 0.5em;
      line-height: 1.25;
      color: #111827;
    }

    p {
      margin: 0 0 0.85rem;
      white-space: pre-wrap;
      word-break: break-word;
    }

    .docx-spacer {
      height: 0.8rem;
    }

    ul {
      margin: 0 0 1rem 1.25rem;
      padding: 0;
    }

    li {
      margin: 0.2rem 0;
    }

    table {
      width: 100%;
      border-collapse: collapse;
      margin: 1rem 0;
      table-layout: fixed;
    }

    td, th {
      border: 1px solid #d1d5db;
      padding: 0.65rem;
      vertical-align: top;
      text-align: left;
    }
  </style>
</head>
<body>
  <div class="docx-root">
    {{normalizedBody}}
  </div>
</body>
</html>
""";
        }

        private static string NormalizeLists(string html)
        {
            var lines = html.Split('\n', StringSplitOptions.None);
            var result = new StringBuilder();
            var insideList = false;

            foreach (var rawLine in lines)
            {
                var line = rawLine.Trim();

                if (line.StartsWith("<li>", StringComparison.Ordinal))
                {
                    if (!insideList)
                    {
                        result.Append("<ul>");
                        insideList = true;
                    }

                    result.Append(line);
                }
                else
                {
                    if (insideList)
                    {
                        result.Append("</ul>");
                        insideList = false;
                    }

                    result.Append(line);
                }
            }

            if (insideList)
            {
                result.Append("</ul>");
            }

            return result.ToString();
        }

        private static string HtmlEncode(string value)
        {
            return System.Net.WebUtility.HtmlEncode(value);
        }
    }
}