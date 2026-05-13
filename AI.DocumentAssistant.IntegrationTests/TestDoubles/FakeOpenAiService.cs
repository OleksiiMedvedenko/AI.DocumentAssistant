using AI.DocumentAssistant.Application.Abstractions.AI;

namespace AI.DocumentAssistant.IntegrationTests.TestDoubles;

public sealed class FakeOpenAiService : IOpenAiService
{
    public Task<string> GenerateSummaryAsync(
        string text,
        string? language,
        CancellationToken cancellationToken)
        => Task.FromResult($"SUMMARY::{Lang(language)}::{Trim(text)}");

    public Task<string> AnswerQuestionAsync(
        string documentContext,
        string question,
        string? language,
        CancellationToken cancellationToken)
        => Task.FromResult($"ANSWER::{Lang(language)}::{question}::CTX::{Trim(documentContext)}");

    public Task<string> ExtractStructuredDataAsync(
        string documentContext,
        string extractionType,
        string? language,
        CancellationToken cancellationToken)
        => Task.FromResult(
            $$"""
            {"extractionType":"{{extractionType}}","language":"{{Lang(language)}}","value":"fake","preview":"{{Escape(Trim(documentContext))}}"}
            """);


    public Task<string> AnalyzeFolderTreeAsync(
        string developerPrompt,
        string userPrompt,
        CancellationToken cancellationToken)
    {
        var source = (userPrompt ?? string.Empty).ToLowerInvariant();
        var isCv = source.Contains(" cv") || source.Contains("cv_") || source.Contains("resume") || source.Contains("doświadczenie zawodowe") || source.Contains("doswiadczenie zawodowe");
        var isInvoice = source.Contains("faktura") || source.Contains("invoice");
        var kind = isCv ? "cv" : isInvoice ? "invoice" : "document";
        var topic = source.Contains("księg") || source.Contains("ksieg") || source.Contains("account") ? "finance" :
            source.Contains("program") || source.Contains("developer") || source.Contains("it") ? "it" : "general";

        if (kind == "invoice")
        {
            var existingInvoiceFolderId = ExtractFolderIdByKeyOrName(userPrompt, "finance-invoices", "Finance Invoices")
                ?? ExtractFolderIdByKeyOrName(userPrompt, "invoices", "Invoices");
            if (existingInvoiceFolderId is not null)
            {
                var existingJson = "{\"documentKind\":\"invoice\",\"topic\":\"finance\",\"decision\":\"use_existing\",\"confidence\":0.91,\"existingFolderId\":\"" + existingInvoiceFolderId + "\",\"proposedPath\":[],\"alternatives\":[],\"reasonCode\":\"smart_folder.existing_path_selected\"}";
                return Task.FromResult(existingJson);
            }
        }

        string path;
        if (kind == "cv")
        {
            var topicKey = topic == "finance" ? "finanse" : topic;
            var topicName = topic == "finance" ? "Finanse" : ToTitle(topic);
            path = "[" + JsonFolder("cv", "CV") + "," + JsonFolder(topicKey, topicName) + "]";
        }
        else if (kind == "invoice")
        {
            path = "[" + JsonFolder("rachunki", "Rachunki") + "," + JsonFolder("faktury", "Faktury") + "]";
        }
        else
        {
            path = "[]";
        }

        var decision = path == "[]" ? "needs_review" : "create_path";
        var json = "{\"documentKind\":\"" + kind + "\",\"topic\":\"" + topic + "\",\"decision\":\"" + decision + "\",\"confidence\":0.91,\"existingFolderId\":null,\"proposedPath\":" + path + ",\"alternatives\":[],\"reasonCode\":\"smart_folder.fake_decision\"}";
        return Task.FromResult(json);
    }

    public Task<string> RunDocumentActionAsync(
        string documentContext,
        string actionType,
        string outputFormat,
        string prompt,
        string? language,
        CancellationToken cancellationToken)
    {
        var format = string.IsNullOrWhiteSpace(outputFormat) ? "markdown" : outputFormat.Trim().ToLowerInvariant();
        if (format == "json")
        {
            return Task.FromResult($"{{\"actionType\":\"{actionType}\",\"language\":\"{Lang(language)}\",\"value\":\"fake action result\",\"preview\":\"{Escape(Trim(documentContext))}\"}}");
        }

        return Task.FromResult($"AI_ACTION::{format}::{Lang(language)}::{actionType}::{prompt}::CTX::{Trim(documentContext)}");
    }

    public Task<string> CompareDocumentsAsync(
        string firstDocumentText,
        string secondDocumentText,
        string? comparisonPrompt,
        string? language,
        CancellationToken cancellationToken)
        => Task.FromResult(
            $"COMPARE::{Lang(language)}::{comparisonPrompt ?? "default"}::A::{Trim(firstDocumentText)}::B::{Trim(secondDocumentText)}");


    private static string? ExtractFolderIdByKeyOrName(string prompt, string key, string name)
    {
        foreach (var line in (prompt ?? string.Empty).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!line.Contains($"key: {key}", StringComparison.OrdinalIgnoreCase) &&
                !line.Contains(name, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var marker = "id:";
            var start = line.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (start < 0)
            {
                continue;
            }

            start += marker.Length;
            var end = line.IndexOf(';', start);
            var value = end > start ? line[start..end].Trim() : line[start..].Trim();
            return Guid.TryParse(value, out var id) ? id.ToString() : null;
        }

        return null;
    }

    private static string JsonFolder(string key, string name)
        => $"{{\"key\":\"{key}\",\"name\":\"{name}\",\"namePl\":\"{name}\",\"nameEn\":\"{name}\",\"nameUa\":\"{name}\"}}";

    private static string ToTitle(string value)
        => string.IsNullOrWhiteSpace(value) || value == "general" ? "Ogólne" : char.ToUpperInvariant(value[0]) + value[1..];

    private static string Lang(string? language)
        => string.IsNullOrWhiteSpace(language) ? "default" : language.Trim();

    private static string Trim(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var normalized = text.Trim().Replace("\r", " ").Replace("\n", " ");
        return normalized.Length <= 120 ? normalized : normalized[..120];
    }

    private static string Escape(string value)
        => value.Replace("\\", "\\\\").Replace("\"", "\\\"");
}