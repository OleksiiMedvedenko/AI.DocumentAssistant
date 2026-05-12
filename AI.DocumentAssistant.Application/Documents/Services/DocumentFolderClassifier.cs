using AI.DocumentAssistant.Application.Abstractions.AI;
using AI.DocumentAssistant.Application.Abstractions.Documents;
using AI.DocumentAssistant.Application.Documents.Dtos;
using AI.DocumentAssistant.Domain.Entities;
using AI.DocumentAssistant.Domain.Enums;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AI.DocumentAssistant.Application.Documents.Services;

public sealed class DocumentFolderClassifier : IDocumentFolderClassifier
{
    private const int MaxFolderRowsForPrompt = 120;
    private const int MaxPathDepth = 4;

    private readonly IOpenAiService _openAiService;

    public DocumentFolderClassifier(IOpenAiService openAiService)
    {
        _openAiService = openAiService;
    }

    public async Task<DocumentFolderAnalysisResultDto> AnalyzeAsync(
        Document document,
        IReadOnlyCollection<DocumentFolder> existingFolders,
        CancellationToken cancellationToken)
    {
        var sourcePreview = BuildDocumentPreview(document);
        var local = BuildLocalSafetyNet(document, existingFolders, sourcePreview);

        var ai = await TryAnalyzeWithAiAsync(document, existingFolders, sourcePreview, cancellationToken);
        if (ai is null)
        {
            return local;
        }

        return MergeAiWithLocalSafetyNet(ai, local, existingFolders);
    }

    private async Task<DocumentFolderAnalysisResultDto?> TryAnalyzeWithAiAsync(
        Document document,
        IReadOnlyCollection<DocumentFolder> existingFolders,
        string sourcePreview,
        CancellationToken cancellationToken)
    {
        var developerPrompt = """
You are the Smart Folders brain for a document assistant.

Your job is to decide where a document belongs in a user's hierarchical folder tree.
This is a serious production feature: be precise, use the whole folder tree, and avoid flat/generic decisions.

Decision model:
- use_existing: choose the deepest existing folder when it already fits the document well.
- create_path: propose a clean folder path when the right folder does not exist yet.
- needs_review: use when confidence is not enough for an automatic assignment.
- uncategorized: use only when the document has no useful organizational signal.

Rules:
1. Read the document content first. File name is a useful signal, but never the only signal.
2. Use the full folder path, not only folder names. Check parent/child relationships from top to bottom.
3. Prefer specific paths over generic parents. Example: a recruiter CV should not stop at CV if CV / HR is more appropriate.
4. You may create a path with at most 4 levels total. Prefer 2-3 levels, but use the 4th level when it makes the structure clearly better.
5. Do not create duplicate folders. If a suitable folder exists anywhere in the tree, use it or extend it with a child.
6. If the right parent exists but the specialization child does not, propose parent + child. If a top-level document-type folder already exists, extend it instead of creating a competing root.
7. Keep names short and user friendly. Use Polish names in namePl. Provide English in nameEn and Ukrainian in nameUa.
8. Do not use candidate names, people names, company names, dates, or file names as folder names unless the existing structure already clearly uses that convention.
9. Do not propose generic folders such as Documents, Files, Other, Misc, Inne, Różne.
10. Distinguish document type from topic. A CV about accounting is still a CV, likely CV / Finanse or HR / CV / Finanse depending on the user's tree. Technical documentation about invoices is Documentation / KSeF or Documentation / API, not Faktury.
11. If an existing folder is semantically wrong, do not choose it just because embeddings or words overlap.
12. Do not return human-facing translated text. Return a stable machine-readable reasonCode only. The frontend translates reasonCode to PL/EN/UA.
13. Recommended reasonCode values: smart_folder.existing_path_selected, smart_folder.new_path_proposed, smart_folder.needs_review, smart_folder.no_confident_match, smart_folder.no_useful_signal.

Examples:
- CV księgowej -> CV / Finanse, unless the user already has a deeper CV/accounting branch.
- CV rekrutera or HR specialist -> CV / HR when a top-level CV folder exists; otherwise HR / CV is acceptable only if that is already the user's established tree.
- CV programisty -> CV / IT.
- CV psychologa -> CV / Psychologia.
- Faktura VAT -> Rachunki / Faktury or Finanse / Faktury.
- PIT-37/PIT-11 -> Rachunki / PIT or Podatki / PIT.
- KSeF API manual -> Dokumentacja / KSeF or Dokumentacja / API.
- Umowa najmu -> Umowy / Najem.
""";

        var folderTree = BuildFolderTreeForPrompt(existingFolders);
        var userPrompt = $$"""
Current folder tree:
{{folderTree}}

Document metadata:
- File name: {{document.OriginalFileName}}
- Content type: {{document.ContentType}}
- Processing profile: {{document.ProcessingProfile}}

Document excerpt:
{{sourcePreview}}

Return the smart folder decision as JSON.
""";

        try
        {
            var raw = await _openAiService.AnalyzeFolderTreeAsync(
                developerPrompt,
                userPrompt,
                cancellationToken);

            var parsed = JsonSerializer.Deserialize<AiSmartFolderDecision>(
                raw,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (parsed is null)
            {
                return null;
            }

            return MapAiDecision(parsed, existingFolders);
        }
        catch
        {
            return null;
        }
    }

    private static DocumentFolderAnalysisResultDto MapAiDecision(
        AiSmartFolderDecision parsed,
        IReadOnlyCollection<DocumentFolder> existingFolders)
    {
        var existingFolderId = ValidateExistingFolderId(parsed.ExistingFolderId, existingFolders);
        var path = SanitizePath(parsed.ProposedPath);
        var confidence = Math.Clamp(parsed.Confidence, 0m, 1m);
        var reasonCode = NormalizeReasonCode(parsed.ReasonCode, existingFolderId, path, confidence);
        var decision = NormalizeDecision(parsed.Decision, existingFolderId, path, confidence);

        var result = new DocumentFolderAnalysisResultDto
        {
            Category = NormalizeKey(parsed.DocumentKind, "document"),
            Topic = NormalizeKey(parsed.Topic, "general"),
            Decision = decision,
            Confidence = confidence,
            Reason = reasonCode,
            ReasonCode = reasonCode,
            SuggestedExistingFolderId = existingFolderId,
            ProposedPath = path,
            ProposedFolder = path.LastOrDefault(),
            ExistingFolderCandidates = new List<DocumentFolderCandidateDto>()
        };

        if (existingFolderId is Guid id)
        {
            var folder = existingFolders.First(x => x.Id == id);
            result.ExistingFolderCandidates.Add(new DocumentFolderCandidateDto
            {
                FolderId = folder.Id,
                FolderKey = folder.Key,
                FolderName = folder.Name,
                Score = confidence,
                RuleScore = confidence,
                FinalScore = confidence,
                Reason = reasonCode
            });
        }

        foreach (var alternative in parsed.Alternatives ?? new List<AiSmartFolderAlternative>())
        {
            var altId = ValidateExistingFolderId(alternative.ExistingFolderId, existingFolders);
            if (altId is null)
            {
                continue;
            }

            if (result.ExistingFolderCandidates.Any(x => x.FolderId == altId.Value))
            {
                continue;
            }

            var folder = existingFolders.First(x => x.Id == altId.Value);
            var altScore = Math.Clamp(alternative.Confidence, 0m, 1m);
            result.ExistingFolderCandidates.Add(new DocumentFolderCandidateDto
            {
                FolderId = folder.Id,
                FolderKey = folder.Key,
                FolderName = folder.Name,
                Score = altScore,
                RuleScore = altScore,
                FinalScore = altScore,
                Reason = NormalizeReasonCode(alternative.ReasonCode, altId, SanitizePath(alternative.ProposedPath), altScore)
            });
        }

        return result;
    }

    private static DocumentFolderAnalysisResultDto MergeAiWithLocalSafetyNet(
        DocumentFolderAnalysisResultDto ai,
        DocumentFolderAnalysisResultDto local,
        IReadOnlyCollection<DocumentFolder> existingFolders)
    {
        // AI is the source of truth. The local model is only a safety net when AI is uncertain or empty.
        if (ai.Confidence < 0.55m && local.Confidence > ai.Confidence)
        {
            return local;
        }

        if (ShouldPreferLocalSpecificPath(ai, local, existingFolders))
        {
            ai.Decision = "create_path";
            ai.ProposedPath = local.ProposedPath.Take(MaxPathDepth).ToList();
            ai.ProposedFolder = ai.ProposedPath.LastOrDefault();
            ai.SuggestedExistingFolderId = null;
            ai.Confidence = Math.Max(ai.Confidence, Math.Min(0.86m, local.Confidence + 0.08m));
            ai.Reason = "smart_folder.specific_child_path_preferred";
            ai.ReasonCode = "smart_folder.specific_child_path_preferred";
        }
        else if (ai.ProposedPath.Count == 0 && local.ProposedPath.Count > 0 && ai.Decision is "create_path" or "needs_review")
        {
            ai.ProposedPath = local.ProposedPath.Take(MaxPathDepth).ToList();
            ai.ProposedFolder = ai.ProposedPath.LastOrDefault();
        }

        foreach (var candidate in local.ExistingFolderCandidates)
        {
            if (ai.ExistingFolderCandidates.All(x => x.FolderId != candidate.FolderId))
            {
                ai.ExistingFolderCandidates.Add(candidate);
            }
        }

        ai.ExistingFolderCandidates = ai.ExistingFolderCandidates
            .GroupBy(x => x.FolderId)
            .Select(x => x.OrderByDescending(y => y.Score).First())
            .OrderByDescending(x => x.Score)
            .Take(5)
            .ToList();

        if (ai.SuggestedExistingFolderId is null && ai.ExistingFolderCandidates.FirstOrDefault() is { } best)
        {
            ai.SuggestedExistingFolderId = best.FolderId;
        }

        return ai;
    }

    private static bool ShouldPreferLocalSpecificPath(
        DocumentFolderAnalysisResultDto ai,
        DocumentFolderAnalysisResultDto local,
        IReadOnlyCollection<DocumentFolder> existingFolders)
    {
        if (local.ProposedPath.Count <= 1)
        {
            return false;
        }

        if (!string.Equals(ai.Category, local.Category, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (string.Equals(local.Topic, "general", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (ai.ProposedPath.Count > 1)
        {
            return false;
        }

        if (ai.Decision == "use_existing" && ai.SuggestedExistingFolderId is Guid existingId)
        {
            var folder = existingFolders.FirstOrDefault(x => x.Id == existingId);
            if (folder is null)
            {
                return false;
            }

            var firstLocal = local.ProposedPath[0];
            var existingKey = NormalizeKey(folder.Key, "folder");
            var firstLocalKey = NormalizeKey(firstLocal.Key, "folder");
            var existingMatchesFirstLocal = existingKey == firstLocalKey ||
                                            NormalizeKey(folder.Name, "folder") == firstLocalKey ||
                                            NormalizeKey(folder.NamePl, "folder") == firstLocalKey ||
                                            NormalizeKey(folder.NameEn, "folder") == firstLocalKey;

            if (existingMatchesFirstLocal)
            {
                return true;
            }

            // If the user already has a top-level document-type root, prefer extending it
            // over accepting a competing domain root like HR / CV when CV already exists.
            if (string.Equals(local.Category, "cv", StringComparison.OrdinalIgnoreCase))
            {
                return existingFolders.Any(x => x.ParentFolderId is null &&
                    (NormalizeKey(x.Key, "folder") == firstLocalKey ||
                     NormalizeKey(x.Name, "folder") == firstLocalKey ||
                     NormalizeKey(x.NamePl, "folder") == firstLocalKey ||
                     NormalizeKey(x.NameEn, "folder") == firstLocalKey));
            }

            return false;
        }

        return ai.ProposedPath.Count == 0 && (ai.Decision is "create_path" or "needs_review");
    }

    private static DocumentFolderAnalysisResultDto BuildLocalSafetyNet(
        Document document,
        IReadOnlyCollection<DocumentFolder> existingFolders,
        string sourcePreview)
    {
        var source = ($"{document.OriginalFileName}\n{sourcePreview}").ToLowerInvariant();
        var kind = GuessKind(source);
        var topic = GuessTopic(source, kind);
        var path = BuildGenericPath(kind, topic);
        var confidence = kind == "document" ? 0.35m : 0.72m;
        var existingCandidate = FindBestExistingByPath(existingFolders, path);

        var result = new DocumentFolderAnalysisResultDto
        {
            Category = kind,
            Topic = topic,
            Decision = existingCandidate is null ? "create_path" : "use_existing",
            Confidence = confidence,
            Reason = "smart_folder.fallback_local",
            ReasonCode = "smart_folder.fallback_local",
            SuggestedExistingFolderId = existingCandidate?.Id,
            ProposedPath = path,
            ProposedFolder = path.LastOrDefault()
        };

        if (existingCandidate is not null)
        {
            result.ExistingFolderCandidates.Add(new DocumentFolderCandidateDto
            {
                FolderId = existingCandidate.Id,
                FolderKey = existingCandidate.Key,
                FolderName = existingCandidate.Name,
                Score = confidence,
                RuleScore = confidence,
                FinalScore = confidence,
                Reason = result.Reason
            });
        }

        return result;
    }

    private static string BuildDocumentPreview(Document document)
    {
        var limit = document.ProcessingProfile == DocumentProcessingProfile.HighAccuracyCv ? 9000 : 6500;
        var preview = DocumentAnalysisPreviewBuilder.Build(document.ExtractedText, limit);
        var builder = new StringBuilder();
        builder.AppendLine(document.OriginalFileName);
        builder.AppendLine(document.ContentType);
        if (!string.IsNullOrWhiteSpace(document.QuickSummary)) builder.AppendLine(document.QuickSummary);
        if (!string.IsNullOrWhiteSpace(document.Summary)) builder.AppendLine(document.Summary);
        builder.AppendLine(preview);
        return builder.ToString();
    }

    private static string BuildFolderTreeForPrompt(IReadOnlyCollection<DocumentFolder> folders)
    {
        if (folders.Count == 0)
        {
            return "No folders exist yet.";
        }

        return string.Join(
            "\n",
            folders
                .OrderBy(x => BuildFolderPath(folders, x))
                .Take(MaxFolderRowsForPrompt)
                .Select(x =>
                    $"- id: {x.Id}; parentId: {x.ParentFolderId?.ToString() ?? "null"}; key: {x.Key}; path: {BuildFolderPath(folders, x)}; names: PL='{x.NamePl}', EN='{x.NameEn}', UA='{x.NameUa}', default='{x.Name}'"));
    }

    private static string BuildFolderPath(IReadOnlyCollection<DocumentFolder> folders, DocumentFolder leaf)
    {
        var names = new Stack<string>();
        var current = leaf;
        var guard = 0;

        while (guard++ < 8)
        {
            names.Push(current.Name);
            if (current.ParentFolderId is not Guid parentId)
            {
                break;
            }

            var parent = folders.FirstOrDefault(x => x.Id == parentId);
            if (parent is null)
            {
                break;
            }

            current = parent;
        }

        return string.Join(" / ", names);
    }

    private static Guid? ValidateExistingFolderId(string? value, IReadOnlyCollection<DocumentFolder> existingFolders)
    {
        if (!Guid.TryParse(value, out var id))
        {
            return null;
        }

        return existingFolders.Any(x => x.Id == id) ? id : null;
    }

    private static List<DocumentFolderProposalDto> SanitizePath(IEnumerable<AiFolderPathSegment>? path)
    {
        if (path is null)
        {
            return new List<DocumentFolderProposalDto>();
        }

        return path
            .Select(x =>
            {
                var fallbackName = FirstNonEmpty(x.NamePl, x.Name, x.NameEn, x.Key, "Folder");
                var key = NormalizeKey(FirstNonEmpty(x.Key, fallbackName), "folder");
                return new DocumentFolderProposalDto
                {
                    Key = key,
                    Name = Truncate(NormalizeText(x.Name, fallbackName), 60),
                    NamePl = Truncate(NormalizeText(x.NamePl, fallbackName), 60),
                    NameEn = Truncate(NormalizeText(x.NameEn, fallbackName), 60),
                    NameUa = Truncate(NormalizeText(x.NameUa, fallbackName), 60),
                    ParentFolderId = null
                };
            })
            .Where(x => !IsBadFolderName(x.Name) && !IsBadFolderName(x.NamePl))
            .GroupBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.First())
            .Take(MaxPathDepth)
            .ToList();
    }

    private static string NormalizeReasonCode(string? reasonCode, Guid? existingFolderId, IReadOnlyCollection<DocumentFolderProposalDto> path, decimal confidence)
    {
        if (!string.IsNullOrWhiteSpace(reasonCode))
        {
            var cleaned = reasonCode.Trim().ToLowerInvariant().Replace('-', '_').Replace(' ', '_');
            cleaned = Regex.Replace(cleaned, @"[^a-z0-9_\.]+", "_");
            cleaned = Regex.Replace(cleaned, @"_{2,}", "_").Trim('_');

            if (!string.IsNullOrWhiteSpace(cleaned))
            {
                return cleaned.StartsWith("smart_folder.", StringComparison.OrdinalIgnoreCase)
                    ? cleaned
                    : $"smart_folder.{cleaned}";
            }
        }

        if (existingFolderId is not null)
        {
            return "smart_folder.existing_path_selected";
        }

        if (path.Count > 0)
        {
            return "smart_folder.new_path_proposed";
        }

        return confidence < 0.45m
            ? "smart_folder.no_confident_match"
            : "smart_folder.needs_review";
    }

    private static string NormalizeDecision(string? decision, Guid? existingFolderId, IReadOnlyCollection<DocumentFolderProposalDto> path, decimal confidence)
    {
        var normalized = NormalizeKey(decision, "needs_review").Replace('-', '_');
        if (normalized is "use_existing" or "create_path" or "needs_review" or "uncategorized")
        {
            if (normalized == "use_existing" && existingFolderId is null)
            {
                return path.Count > 0 ? "create_path" : "needs_review";
            }

            if (normalized == "create_path" && path.Count == 0)
            {
                return existingFolderId is not null ? "use_existing" : "needs_review";
            }

            return normalized;
        }

        if (existingFolderId is not null && confidence >= 0.60m)
        {
            return "use_existing";
        }

        return path.Count > 0 ? "create_path" : "needs_review";
    }

    private static string GuessKind(string source)
    {
        if (Regex.IsMatch(source, @"\bcv\b|resume|curriculum vitae|doświadczenie zawodowe|doswiadczenie zawodowe|wykształcenie|wyksztalcenie|umiejętności|umiejetnosci", RegexOptions.IgnoreCase)) return "cv";
        if (Regex.IsMatch(source, @"\bpit([ -]?(11|28|36|37|38|39|40|8c))?\b|zeznanie podatkowe|deklaracja podatkowa", RegexOptions.IgnoreCase)) return "tax-return";
        if (Regex.IsMatch(source, @"faktura|invoice|sprzedawca|nabywca|kwota netto|kwota brutto", RegexOptions.IgnoreCase)) return "invoice";
        if (Regex.IsMatch(source, @"umowa|contract|agreement|najem|wynajem", RegexOptions.IgnoreCase)) return "contract";
        if (Regex.IsMatch(source, @"documentation|dokumentacja|instrukcja|manual|specyfikacja|openapi|swagger|api", RegexOptions.IgnoreCase)) return "documentation";
        return "document";
    }

    private static string GuessTopic(string source, string kind)
    {
        if (kind == "cv")
        {
            if (Regex.IsMatch(source, @"księg|ksieg|accountant|accounting|rachunkowość|rachunkowosc|vat|cit|jpk", RegexOptions.IgnoreCase)) return "finance";
            if (Regex.IsMatch(source, @"rekruter|rekrutacja|hr|kadry|płace|place|payroll|human resources", RegexOptions.IgnoreCase)) return "hr";
            if (Regex.IsMatch(source, @"programista|developer|software|frontend|backend|\.net|java|typescript|react|python", RegexOptions.IgnoreCase)) return "it";
            if (Regex.IsMatch(source, @"psycholog|psychologia|psychotherapy|psychoterapia|terapia", RegexOptions.IgnoreCase)) return "psychology";
            if (Regex.IsMatch(source, @"handlow|sales|sprzedaż|sprzedaz|account manager", RegexOptions.IgnoreCase)) return "sales";
        }

        if (kind == "tax-return") return "pit";
        if (kind == "invoice") return "invoices";
        if (kind == "documentation" && Regex.IsMatch(source, @"ksef|e-faktur", RegexOptions.IgnoreCase)) return "ksef";
        if (kind == "documentation" && Regex.IsMatch(source, @"api|swagger|openapi|endpoint", RegexOptions.IgnoreCase)) return "api";
        if (kind == "contract" && Regex.IsMatch(source, @"najem|wynajem|lease|rental", RegexOptions.IgnoreCase)) return "rent";
        return "general";
    }

    private static List<DocumentFolderProposalDto> BuildGenericPath(string kind, string topic)
    {
        if (kind == "cv")
        {
            return new[] { NewSegment("cv", "CV", "CV", "CV", "Резюме"), TopicSegment(topic) }
                .Where(x => x.Key != "general")
                .ToList();
        }

        if (kind == "invoice") return new List<DocumentFolderProposalDto> { NewSegment("rachunki", "Rachunki", "Rachunki", "Bills", "Рахунки"), NewSegment("faktury", "Faktury", "Faktury", "Invoices", "Рахунки") };
        if (kind == "tax-return") return new List<DocumentFolderProposalDto> { NewSegment("rachunki", "Rachunki", "Rachunki", "Bills", "Рахунки"), NewSegment("pit", "PIT", "PIT", "PIT", "PIT") };
        if (kind == "documentation") return new List<DocumentFolderProposalDto> { NewSegment("dokumentacja", "Dokumentacja", "Dokumentacja", "Documentation", "Документація"), TopicSegment(topic) }.Where(x => x.Key != "general").ToList();
        if (kind == "contract") return new List<DocumentFolderProposalDto> { NewSegment("umowy", "Umowy", "Umowy", "Contracts", "Договори"), TopicSegment(topic) }.Where(x => x.Key != "general").ToList();
        return new List<DocumentFolderProposalDto>();
    }

    private static DocumentFolder? FindBestExistingByPath(IReadOnlyCollection<DocumentFolder> folders, IReadOnlyList<DocumentFolderProposalDto> path)
    {
        if (path.Count == 0)
        {
            return null;
        }

        return folders
            .Select(folder => new { Folder = folder, Score = ScoreExistingPath(folders, folder, path) })
            .Where(x => x.Score > 0m)
            .OrderByDescending(x => x.Score)
            .Select(x => x.Folder)
            .FirstOrDefault();
    }

    private static decimal ScoreExistingPath(IReadOnlyCollection<DocumentFolder> folders, DocumentFolder folder, IReadOnlyList<DocumentFolderProposalDto> proposed)
    {
        var folderPath = BuildFolderPath(folders, folder).Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(x => NormalizeKey(x, "folder"))
            .ToList();
        var proposedKeys = proposed.Select(x => NormalizeKey(x.Key, "folder")).ToList();
        var matches = proposedKeys.Count(x => folderPath.Contains(x, StringComparer.OrdinalIgnoreCase));
        return matches == 0 ? 0m : matches / (decimal)Math.Max(proposedKeys.Count, folderPath.Count);
    }

    private static DocumentFolderProposalDto TopicSegment(string topic)
    {
        return topic switch
        {
            "finance" => NewSegment("finanse", "Finanse", "Finanse", "Finance", "Фінанси"),
            "hr" => NewSegment("hr", "HR", "HR", "HR", "HR"),
            "it" => NewSegment("it", "IT", "IT", "IT", "IT"),
            "psychology" => NewSegment("psychologia", "Psychologia", "Psychologia", "Psychology", "Психологія"),
            "sales" => NewSegment("sprzedaz", "Sprzedaż", "Sprzedaż", "Sales", "Продажі"),
            "invoices" => NewSegment("faktury", "Faktury", "Faktury", "Invoices", "Рахунки"),
            "pit" => NewSegment("pit", "PIT", "PIT", "PIT", "PIT"),
            "ksef" => NewSegment("ksef", "KSeF", "KSeF", "KSeF", "KSeF"),
            "api" => NewSegment("api", "API", "API", "API", "API"),
            "rent" => NewSegment("najem", "Najem", "Najem", "Rent", "Оренда"),
            _ => NewSegment("general", "Ogólne", "Ogólne", "General", "Загальні")
        };
    }

    private static DocumentFolderProposalDto NewSegment(string key, string name, string namePl, string nameEn, string nameUa)
    {
        return new DocumentFolderProposalDto
        {
            Key = NormalizeKey(key, "folder"),
            Name = name,
            NamePl = namePl,
            NameEn = nameEn,
            NameUa = nameUa
        };
    }

    private static bool IsBadFolderName(string? name)
    {
        var normalized = NormalizeKey(name, "");
        return string.IsNullOrWhiteSpace(normalized) || normalized is "document" or "documents" or "dokument" or "dokumenty" or "file" or "files" or "plik" or "pliki" or "other" or "inne" or "misc" or "rozne";
    }

    private static string FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x))!.Trim();

    private static string NormalizeText(string? value, string fallback)
        => string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    private static string Truncate(string value, int max)
        => value.Length <= max ? value : value[..max];

    private static string NormalizeKey(string? value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value)) return fallback;
        var normalized = RemoveDiacritics(value.Trim().ToLowerInvariant());
        normalized = Regex.Replace(normalized, @"[^a-z0-9]+", "-");
        normalized = Regex.Replace(normalized, @"-{2,}", "-").Trim('-');
        return string.IsNullOrWhiteSpace(normalized) ? fallback : normalized;
    }

    private static string RemoveDiacritics(string value)
    {
        var normalized = value.Normalize(System.Text.NormalizationForm.FormD);
        var chars = normalized
            .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            .ToArray();
        return new string(chars).Normalize(System.Text.NormalizationForm.FormC);
    }

    private sealed class AiSmartFolderDecision
    {
        public string? DocumentKind { get; set; }
        public string? Topic { get; set; }
        public string? Decision { get; set; }
        public decimal Confidence { get; set; }
        public string? ExistingFolderId { get; set; }
        public List<AiFolderPathSegment>? ProposedPath { get; set; }
        public List<AiSmartFolderAlternative>? Alternatives { get; set; }
        public string? ReasonCode { get; set; }
    }

    private sealed class AiSmartFolderAlternative
    {
        public string? ExistingFolderId { get; set; }
        public List<AiFolderPathSegment>? ProposedPath { get; set; }
        public decimal Confidence { get; set; }
        public string? ReasonCode { get; set; }
    }

    private sealed class AiFolderPathSegment
    {
        public string? Key { get; set; }
        public string? Name { get; set; }
        public string? NamePl { get; set; }
        public string? NameEn { get; set; }
        public string? NameUa { get; set; }
    }
}
