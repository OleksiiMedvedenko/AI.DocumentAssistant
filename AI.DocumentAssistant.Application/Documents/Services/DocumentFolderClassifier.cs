using AI.DocumentAssistant.Application.Abstractions.AI;
using AI.DocumentAssistant.Application.Abstractions.Documents;
using AI.DocumentAssistant.Application.Documents.Dtos;
using AI.DocumentAssistant.Domain.Entities;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AI.DocumentAssistant.Application.Documents.Services
{
    public sealed class DocumentFolderClassifier : IDocumentFolderClassifier
    {
        private readonly IOpenAiService _openAiService;

        public DocumentFolderClassifier(IOpenAiService openAiService)
        {
            _openAiService = openAiService;
        }

        public async Task<DocumentFolderSuggestionDto?> SuggestAsync(
            Document document,
            IReadOnlyCollection<DocumentFolder> existingFolders,
            CancellationToken cancellationToken)
        {
            var heuristic = TryHeuristic(document, existingFolders);
            if (heuristic is not null)
            {
                return heuristic;
            }

            var foldersText = string.Join(
                "\n",
                existingFolders.Select(x =>
                    $"- id: {x.Id}, key: {x.Key}, parentId: {x.ParentFolderId}, names: [{x.NamePl} | {x.NameEn} | {x.NameUa}]"));

            var sampleText = document.ExtractedText is null
                ? string.Empty
                : document.ExtractedText[..Math.Min(document.ExtractedText.Length, 5000)];

            var prompt = $$"""
                You classify a document into an existing folder or propose a new one.

                Return ONLY valid JSON with this exact shape:
                {
    
                        "existingFolderId": "guid-or-null",
                    "proposedKey": "string",
                    "proposedName": "string",
                    "proposedNamePl": "string",
                    "proposedNameEn": "string",
                    "proposedNameUa": "string",
                    "confidence": 0.0,
                    "reason": "string"
                }

                Rules:
                - Prefer an existing folder when it clearly matches.
                - If nothing matches, propose a short, clean business folder name.
                - Avoid duplicates and plural noise.
                - Use confidence between 0.00 and 1.00.
                - Keep names concise and natural.

                File name: {{document.OriginalFileName}}
                Content type: {{document.ContentType}}

                Existing folders:
                {{foldersText}}

                Document excerpt:
                {{sampleText}}
                """;

            var raw = await _openAiService.ExtractStructuredDataAsync(
                prompt,
                "document-folder-classification",
                null,
                cancellationToken);

            try
            {
                var result = JsonSerializer.Deserialize<DocumentFolderClassifierResult>(raw, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (result is null)
                {
                    return null;
                }

                return new DocumentFolderSuggestionDto
                {
                    ExistingFolderId = result.ExistingFolderId,
                    ProposedKey = NormalizeKey(result.ProposedKey ?? result.ProposedNameEn ?? result.ProposedName ?? "folder"),
                    ProposedName = NormalizeText(result.ProposedName ?? result.ProposedNameEn ?? "Folder"),
                    ProposedNamePl = NormalizeText(result.ProposedNamePl ?? result.ProposedName ?? "Folder"),
                    ProposedNameEn = NormalizeText(result.ProposedNameEn ?? result.ProposedName ?? "Folder"),
                    ProposedNameUa = NormalizeText(result.ProposedNameUa ?? result.ProposedName ?? "Folder"),
                    Confidence = Math.Clamp(result.Confidence, 0m, 1m),
                    Reason = NormalizeText(result.Reason ?? "AI classification.")
                };
            }
            catch
            {
                return null;
            }
        }

        private static DocumentFolderSuggestionDto? TryHeuristic(
            Document document,
            IReadOnlyCollection<DocumentFolder> existingFolders)
        {
            var source = $"{document.OriginalFileName}\n{document.ExtractedText}".ToLowerInvariant();

            var aliases = new Dictionary<string, string[]>
            {
                ["cv"] = ["cv", "resume", "curriculum vitae", "candidate", "experience", "skills"],
                ["invoices"] = ["invoice", "faktura", "rachunek", "vat", "payment due"],
                ["contracts"] = ["contract", "agreement", "umowa", "договір", "terms and conditions"],
                ["reports"] = ["report", "raport", "summary", "analysis", "звіт"]
            };

            foreach (var pair in aliases)
            {
                if (!pair.Value.Any(source.Contains))
                {
                    continue;
                }

                var existing = existingFolders.FirstOrDefault(x =>
                    x.Key.Equals(pair.Key, StringComparison.OrdinalIgnoreCase) ||
                    x.NameEn.Equals(pair.Key, StringComparison.OrdinalIgnoreCase));

                if (existing is not null)
                {
                    return new DocumentFolderSuggestionDto
                    {
                        ExistingFolderId = existing.Id,
                        ProposedKey = existing.Key,
                        ProposedName = existing.Name,
                        ProposedNamePl = existing.NamePl,
                        ProposedNameEn = existing.NameEn,
                        ProposedNameUa = existing.NameUa,
                        Confidence = 0.96m,
                        Reason = "Matched by document keywords."
                    };
                }

                return pair.Key switch
                {
                    "cv" => NewFolder("cv", "CV", "CV", "CV", "Резюме", 0.92m, "Detected resume-like content."),
                    "invoices" => NewFolder("invoices", "Invoices", "Faktury", "Invoices", "Рахунки", 0.92m, "Detected invoice-like content."),
                    "contracts" => NewFolder("contracts", "Contracts", "Umowy", "Contracts", "Договори", 0.90m, "Detected contract-like content."),
                    "reports" => NewFolder("reports", "Reports", "Raporty", "Reports", "Звіти", 0.88m, "Detected report-like content."),
                    _ => null
                };
            }

            return null;
        }

        private static DocumentFolderSuggestionDto NewFolder(
            string key,
            string name,
            string namePl,
            string nameEn,
            string nameUa,
            decimal confidence,
            string reason)
        {
            return new DocumentFolderSuggestionDto
            {
                ProposedKey = key,
                ProposedName = name,
                ProposedNamePl = namePl,
                ProposedNameEn = nameEn,
                ProposedNameUa = nameUa,
                Confidence = confidence,
                Reason = reason
            };
        }

        private static string NormalizeText(string value) => value.Trim();

        private static string NormalizeKey(string value)
        {
            var lower = value.Trim().ToLowerInvariant();
            var compact = Regex.Replace(lower, @"\s+", "-");
            var safe = Regex.Replace(compact, @"[^a-z0-9\-]", "");
            safe = Regex.Replace(safe, @"\-{2,}", "-").Trim('-');
            return string.IsNullOrWhiteSpace(safe) ? "folder" : safe;
        }

        private sealed class DocumentFolderClassifierResult
        {
            public Guid? ExistingFolderId { get; set; }
            public string? ProposedKey { get; set; }
            public string? ProposedName { get; set; }
            public string? ProposedNamePl { get; set; }
            public string? ProposedNameEn { get; set; }
            public string? ProposedNameUa { get; set; }
            public decimal Confidence { get; set; }
            public string? Reason { get; set; }
        }
    }
}
