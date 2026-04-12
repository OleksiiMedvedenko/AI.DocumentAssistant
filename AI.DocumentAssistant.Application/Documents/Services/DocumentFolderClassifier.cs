using AI.DocumentAssistant.Application.Abstractions.AI;
using AI.DocumentAssistant.Application.Abstractions.Documents;
using AI.DocumentAssistant.Application.Documents.Dtos;
using AI.DocumentAssistant.Domain.Entities;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AI.DocumentAssistant.Application.Documents.Services
{
    public sealed class DocumentFolderClassifier : IDocumentFolderClassifier
    {
        private static readonly IReadOnlyDictionary<string, string[]> CategoryAliases =
            new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
            {
                ["documentation"] =
                [
                    "documentation", "docs", "readme", "manual", "user guide", "guide",
                    "technical documentation", "instruction", "installation",
                    "dokumentacja", "instrukcja", "podręcznik", "specyfikacja",
                    "dokumentacja techniczna", "instrukcja obsługi",
                    "документація", "інструкція", "посібник"
                ],
                ["architecture"] =
                [
                    "architecture", "solution architecture", "system design", "uml", "diagram",
                    "architektura", "projekt architektury", "diagram architektury",
                    "архітектура", "системний дизайн"
                ],
                ["api-docs"] =
                [
                    "api", "swagger", "openapi", "endpoint", "rest", "graphql",
                    "specification api", "api reference", "integration api",
                    "specyfikacja api", "dokumentacja api"
                ],
                ["cv"] =
                [
                    "cv", "resume", "curriculum vitae", "candidate", "experience", "skills",
                    "work experience", "employment", "education", "projects",
                    "życiorys", "doświadczenie", "umiejętności", "wykształcenie",
                    "резюме", "досвід", "навички", "освіта"
                ],
                ["invoices"] =
                [
                    "invoice", "invoice number", "invoice no", "faktura", "faktura zaliczkowa",
                    "numer faktury", "sprzedawca", "nabywca",
                    "vat", "stawka podatku", "kwota netto", "kwota brutto",
                    "payment due", "seller", "buyer", "tax"
                ],
                ["contracts"] =
                [
                    "contract", "agreement", "umowa", "договір", "terms and conditions",
                    "parties", "signature", "effective date"
                ],
                ["reports"] =
                [
                    "report", "raport", "summary", "analysis", "звіт", "podsumowanie"
                ],
                ["policies"] =
                [
                    "policy", "polityka", "procedure", "procedura", "regulation",
                    "compliance", "privacy policy", "rodo", "gdpr"
                ]
            };

        private static readonly IReadOnlyDictionary<string, string[]> TopicAliases =
            new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
            {
                ["it"] =
                [
                    "developer", "software", "programming", "backend", "frontend", "fullstack",
                    "dotnet", ".net", "c#", "java", "javascript", "typescript", "react",
                    "angular", "node", "python", "sql", "api", "microservices",
                    "programista", "informatyk", "deweloper", "backend developer", "frontend developer"
                ],
                ["ksef"] =
                [
                    "ksef", "krajowy system e-faktur", "e-faktur", "e-faktury"
                ],
                ["api"] =
                [
                    "api", "swagger", "openapi", "endpoint", "rest", "graphql"
                ],
                ["finance"] =
                [
                    "invoice", "faktura", "vat", "tax", "payment", "płatność", "księgowość", "accounting"
                ],
                ["hr"] =
                [
                    "cv", "resume", "candidate", "recruitment", "rekrutacja", "hiring"
                ],
                ["legal"] =
                [
                    "contract", "agreement", "umowa", "terms", "legal", "compliance"
                ]
            };

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
            var source = BuildSource(document);

            var highPrecision = TryHighPrecisionClassification(document, existingFolders, source);
            if (highPrecision is not null)
            {
                return highPrecision;
            }

            var local = AnalyzeLocally(document, existingFolders, source);

            var bestLocal = local.ExistingFolderCandidates
                .OrderByDescending(x => x.Score)
                .FirstOrDefault();

            if (bestLocal is not null && bestLocal.Score >= 0.88m)
            {
                local.SuggestedExistingFolderId = bestLocal.FolderId;
                local.Confidence = Math.Max(local.Confidence, bestLocal.Score);
                local.Reason = bestLocal.Reason;
                return local;
            }

            if (local.Confidence >= 0.93m)
            {
                return local;
            }

            var ai = await TryAnalyzeWithAiAsync(document, existingFolders, cancellationToken);
            if (ai is null)
            {
                return local;
            }

            MergeLocalCandidates(local, ai, existingFolders);
            return local;
        }

        private static DocumentFolderAnalysisResultDto? TryHighPrecisionClassification(
            Document document,
            IReadOnlyCollection<DocumentFolder> existingFolders,
            string source)
        {
            var documentType = DetectDocumentType(source);
            var topic = DetectTopic(source);

            // Dokumentacja o KSeF / API / fakturach nie może wpadać do faktur tylko dlatego,
            // że zawiera słowo "faktura".
            if (LooksLikeTechnicalDocumentation(source))
            {
                return BuildHierarchicalResult(
                    documentType: "documentation",
                    topic: topic,
                    existingFolders: existingFolders,
                    confidence: topic == "ksef" ? 0.97m : 0.95m,
                    reason: topic == "ksef"
                        ? "Technical documentation about KSeF detected."
                        : "Technical documentation detected.");
            }

            if (LooksLikeInvoice(source))
            {
                return BuildHierarchicalResult(
                    documentType: "invoices",
                    topic: topic,
                    existingFolders: existingFolders,
                    confidence: 0.98m,
                    reason: "Strong invoice indicators detected.");
            }

            if (LooksLikeCv(source))
            {
                return BuildHierarchicalResult(
                    documentType: "cv",
                    topic: topic,
                    existingFolders: existingFolders,
                    confidence: 0.97m,
                    reason: topic == "it"
                        ? "Strong IT CV/resume indicators detected."
                        : "Strong CV/resume indicators detected.");
            }

            if (LooksLikeContract(source))
            {
                return BuildHierarchicalResult(
                    documentType: "contracts",
                    topic: topic,
                    existingFolders: existingFolders,
                    confidence: 0.96m,
                    reason: "Strong contract/agreement indicators detected.");
            }

            return null;
        }

        private static DocumentFolderAnalysisResultDto AnalyzeLocally(
            Document document,
            IReadOnlyCollection<DocumentFolder> existingFolders,
            string source)
        {
            var documentType = DetectDocumentType(source);
            var topic = DetectTopic(source);

            var typeConfidence = EstimateCategoryConfidence(source, documentType);
            var topicConfidence = EstimateTopicConfidence(source, topic);

            var candidates = new List<DocumentFolderCandidateDto>();

            foreach (var folder in existingFolders)
            {
                var score = ScoreFolder(folder, document, source, documentType, topic, existingFolders, out var reason);
                if (score <= 0m)
                {
                    continue;
                }

                candidates.Add(new DocumentFolderCandidateDto
                {
                    FolderId = folder.Id,
                    FolderKey = folder.Key,
                    FolderName = folder.Name,
                    Score = score,
                    Reason = reason
                });
            }

            var ordered = candidates
                .OrderByDescending(x => x.Score)
                .ThenBy(x => x.FolderName)
                .Take(7)
                .ToList();

            var bestExisting = ordered.FirstOrDefault();
            var finalConfidence = new[] { typeConfidence, topicConfidence, bestExisting?.Score ?? 0m }.Max();

            return new DocumentFolderAnalysisResultDto
            {
                Category = documentType,
                Confidence = finalConfidence <= 0m ? 0.35m : finalConfidence,
                Reason = bestExisting?.Reason
                    ?? (!string.Equals(documentType, "unknown", StringComparison.OrdinalIgnoreCase)
                        ? $"Detected type '{documentType}' with topic '{topic}'."
                        : "Category is unknown."),
                SuggestedExistingFolderId = bestExisting?.FolderId,
                ExistingFolderCandidates = ordered,
                ProposedFolder = BuildDefaultProposal(documentType, topic, existingFolders)
            };
        }

        private async Task<DocumentFolderAnalysisResultDto?> TryAnalyzeWithAiAsync(
            Document document,
            IReadOnlyCollection<DocumentFolder> existingFolders,
            CancellationToken cancellationToken)
        {
            var foldersText = string.Join(
                "\n",
                existingFolders.Select(x =>
                    $"- id: {x.Id}, parentId: {x.ParentFolderId}, key: {x.Key}, names: [{x.NamePl} | {x.NameEn} | {x.NameUa} | {x.Name}]"));

            var sampleText = document.ExtractedText is null
                ? string.Empty
                : document.ExtractedText[..Math.Min(document.ExtractedText.Length, 6000)];

            var prompt = $$"""
You analyze a document and recommend how it should be organized in a hierarchical folder tree.

Return ONLY valid JSON with this exact shape:
{
  "category": "string",
  "confidence": 0.0,
  "reason": "string",
  "existingFolderId": "guid-or-null",
  "proposedFolder": {
    "key": "string",
    "name": "string",
    "namePl": "string",
    "nameEn": "string",
    "nameUa": "string",
    "parentFolderId": "guid-or-null"
  }
}

Rules:
- Distinguish document type from subject/topic.
- A technical document about invoices/KSeF is documentation, not an invoice.
- CVs should prefer a relevant subfolder when one exists, e.g. CV/IT.
- Prefer the deepest suitable existing folder.
- If no good folder exists, propose a concise folder and set parentFolderId if a suitable parent exists.
- Use confidence between 0.00 and 1.00.

File name: {{document.OriginalFileName}}
Content type: {{document.ContentType}}

Existing folders:
{{foldersText}}

Document excerpt:
{{sampleText}}
""";

            var raw = await _openAiService.ExtractStructuredDataAsync(
                prompt,
                "document-folder-analysis",
                null,
                cancellationToken);

            try
            {
                var parsed = JsonSerializer.Deserialize<AiFolderAnalysisResult>(
                    raw,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (parsed is null)
                {
                    return null;
                }

                return new DocumentFolderAnalysisResultDto
                {
                    Category = NormalizeText(parsed.Category, "unknown"),
                    Confidence = Math.Clamp(parsed.Confidence, 0m, 1m),
                    Reason = NormalizeText(parsed.Reason, "AI-based classification."),
                    SuggestedExistingFolderId = parsed.ExistingFolderId,
                    ProposedFolder = parsed.ProposedFolder is null
                        ? null
                        : new DocumentFolderProposalDto
                        {
                            Key = NormalizeKey(parsed.ProposedFolder.Key),
                            Name = NormalizeText(parsed.ProposedFolder.Name, "Folder"),
                            NamePl = NormalizeText(parsed.ProposedFolder.NamePl, "Folder"),
                            NameEn = NormalizeText(parsed.ProposedFolder.NameEn, "Folder"),
                            NameUa = NormalizeText(parsed.ProposedFolder.NameUa, "Folder"),
                            ParentFolderId = parsed.ProposedFolder.ParentFolderId
                        }
                };
            }
            catch
            {
                return null;
            }
        }

        private static void MergeLocalCandidates(
            DocumentFolderAnalysisResultDto local,
            DocumentFolderAnalysisResultDto ai,
            IReadOnlyCollection<DocumentFolder> existingFolders)
        {
            if (ai.SuggestedExistingFolderId is Guid aiFolderId)
            {
                var aiFolder = existingFolders.FirstOrDefault(x => x.Id == aiFolderId);
                if (aiFolder is not null && local.ExistingFolderCandidates.All(x => x.FolderId != aiFolderId))
                {
                    local.ExistingFolderCandidates.Insert(0, new DocumentFolderCandidateDto
                    {
                        FolderId = aiFolder.Id,
                        FolderKey = aiFolder.Key,
                        FolderName = aiFolder.Name,
                        Score = Math.Max(ai.Confidence, 0.76m),
                        Reason = $"AI matched this folder. {ai.Reason}"
                    });
                }
            }

            local.ExistingFolderCandidates = local.ExistingFolderCandidates
                .OrderByDescending(x => x.Score)
                .Take(7)
                .ToList();

            if (local.ExistingFolderCandidates.Count > 0)
            {
                local.SuggestedExistingFolderId = local.ExistingFolderCandidates[0].FolderId;
                local.Confidence = Math.Max(local.Confidence, local.ExistingFolderCandidates[0].Score);
                local.Reason = local.ExistingFolderCandidates[0].Reason;
            }

            if (!string.Equals(ai.Category, "unknown", StringComparison.OrdinalIgnoreCase))
            {
                local.Category = ai.Category;
                local.Confidence = Math.Max(local.Confidence, ai.Confidence);
                local.Reason = ai.Reason;
            }

            if (ai.ProposedFolder is not null)
            {
                local.ProposedFolder = ai.ProposedFolder;
            }
        }

        private static string BuildSource(Document document)
        {
            var builder = new StringBuilder();
            builder.AppendLine(document.OriginalFileName);
            builder.AppendLine(document.ContentType);

            if (!string.IsNullOrWhiteSpace(document.ExtractedText))
            {
                builder.AppendLine(document.ExtractedText[..Math.Min(document.ExtractedText.Length, 8000)]);
            }

            return builder.ToString().ToLowerInvariant();
        }

        private static string DetectDocumentType(string source)
        {
            foreach (var pair in CategoryAliases)
            {
                if (pair.Value.Any(source.Contains))
                {
                    return pair.Key;
                }
            }

            return "unknown";
        }

        private static string DetectTopic(string source)
        {
            foreach (var pair in TopicAliases)
            {
                if (pair.Value.Any(source.Contains))
                {
                    return pair.Key;
                }
            }

            return "general";
        }

        private static decimal EstimateCategoryConfidence(string source, string category)
        {
            if (string.Equals(category, "unknown", StringComparison.OrdinalIgnoreCase))
            {
                return 0.20m;
            }

            if (!CategoryAliases.TryGetValue(category, out var aliases))
            {
                return 0.40m;
            }

            var matches = aliases.Count(source.Contains);

            return matches switch
            {
                >= 5 => 0.95m,
                4 => 0.90m,
                3 => 0.84m,
                2 => 0.74m,
                1 => 0.58m,
                _ => 0.35m
            };
        }

        private static decimal EstimateTopicConfidence(string source, string topic)
        {
            if (string.Equals(topic, "general", StringComparison.OrdinalIgnoreCase))
            {
                return 0.30m;
            }

            if (!TopicAliases.TryGetValue(topic, out var aliases))
            {
                return 0.40m;
            }

            var matches = aliases.Count(source.Contains);

            return matches switch
            {
                >= 4 => 0.92m,
                3 => 0.84m,
                2 => 0.74m,
                1 => 0.58m,
                _ => 0.35m
            };
        }

        private static bool LooksLikeInvoice(string source)
        {
            var invoiceSignals = new[]
            {
                "faktura",
                "faktura zaliczkowa",
                "numer faktury",
                "sprzedawca",
                "nabywca",
                "stawka podatku",
                "kwota netto",
                "kwota brutto",
                "invoice number",
                "seller",
                "buyer"
            };

            // Ale dokumentacja o KSeF / API / instrukcja nie jest samą fakturą.
            if (LooksLikeTechnicalDocumentation(source))
            {
                return false;
            }

            return invoiceSignals.Count(source.Contains) >= 3;
        }

        private static bool LooksLikeTechnicalDocumentation(string source)
        {
            var documentationSignals = new[]
            {
                "documentation",
                "technical documentation",
                "readme",
                "manual",
                "user guide",
                "instruction",
                "guide",
                "dokumentacja",
                "instrukcja",
                "specyfikacja",
                "api",
                "swagger",
                "openapi",
                "endpoint",
                "integration"
            };

            return documentationSignals.Count(source.Contains) >= 2;
        }

        private static bool LooksLikeCv(string source)
        {
            var strongSignals = new[]
            {
                "cv",
                "resume",
                "curriculum vitae",
                "experience",
                "work experience",
                "skills",
                "education",
                "employment",
                "projects"
            };

            return strongSignals.Count(source.Contains) >= 3;
        }

        private static bool LooksLikeContract(string source)
        {
            var strongSignals = new[]
            {
                "contract",
                "agreement",
                "umowa",
                "договір",
                "parties",
                "signature",
                "effective date"
            };

            return strongSignals.Count(source.Contains) >= 2;
        }

        private static DocumentFolderAnalysisResultDto BuildHierarchicalResult(
            string documentType,
            string topic,
            IReadOnlyCollection<DocumentFolder> existingFolders,
            decimal confidence,
            string reason)
        {
            var ranked = existingFolders
                .Select(folder => new
                {
                    Folder = folder,
                    Score = ScoreHierarchicalFolderMatch(folder, existingFolders, documentType, topic)
                })
                .Where(x => x.Score > 0m)
                .OrderByDescending(x => x.Score)
                .ThenByDescending(x => HasParentMatchBonus(x.Folder, existingFolders, documentType, topic))
                .ToList();

            var best = ranked.FirstOrDefault();

            return new DocumentFolderAnalysisResultDto
            {
                Category = documentType,
                Confidence = confidence,
                Reason = reason,
                SuggestedExistingFolderId = best?.Folder.Id,
                ExistingFolderCandidates = ranked
                    .Take(7)
                    .Select(x => new DocumentFolderCandidateDto
                    {
                        FolderId = x.Folder.Id,
                        FolderKey = x.Folder.Key,
                        FolderName = x.Folder.Name,
                        Score = Math.Max(x.Score, 0.60m),
                        Reason = $"Hierarchical folder match for type '{documentType}' and topic '{topic}'."
                    })
                    .ToList(),
                ProposedFolder = BuildDefaultProposal(documentType, topic, existingFolders)
            };
        }

        private static decimal ScoreHierarchicalFolderMatch(
            DocumentFolder folder,
            IReadOnlyCollection<DocumentFolder> allFolders,
            string documentType,
            string topic)
        {
            decimal score = 0m;

            var names = GetFolderTokens(folder);

            if (MatchesType(names, documentType))
            {
                score += 0.70m;
            }

            if (MatchesTopic(names, topic))
            {
                score += 0.55m;
            }

            var parent = folder.ParentFolderId is null
                ? null
                : allFolders.FirstOrDefault(x => x.Id == folder.ParentFolderId);

            if (parent is not null)
            {
                var parentNames = GetFolderTokens(parent);

                if (MatchesType(parentNames, documentType))
                {
                    score += 0.35m;
                }

                if (MatchesTopic(parentNames, topic))
                {
                    score += 0.30m;
                }

                // Dla CV/IT preferujemy dziecko IT pod parentem CV.
                if (documentType == "cv" && topic == "it" &&
                    MatchesType(parentNames, "cv") &&
                    MatchesTopic(names, "it"))
                {
                    score += 0.60m;
                }

                // Dla dokumentacji KSeF preferujemy Dokumentacja/KSeF, a nie Faktury.
                if (documentType == "documentation" && topic == "ksef" &&
                    MatchesType(parentNames, "documentation") &&
                    MatchesTopic(names, "ksef"))
                {
                    score += 0.60m;
                }
            }

            return Math.Min(score, 0.98m);
        }

        private static decimal HasParentMatchBonus(
            DocumentFolder folder,
            IReadOnlyCollection<DocumentFolder> allFolders,
            string documentType,
            string topic)
        {
            var parent = folder.ParentFolderId is null
                ? null
                : allFolders.FirstOrDefault(x => x.Id == folder.ParentFolderId);

            if (parent is null)
            {
                return 0m;
            }

            var parentNames = GetFolderTokens(parent);
            decimal bonus = 0m;

            if (MatchesType(parentNames, documentType))
            {
                bonus += 0.50m;
            }

            if (MatchesTopic(parentNames, topic))
            {
                bonus += 0.30m;
            }

            return bonus;
        }

        private static decimal ScoreFolder(
            DocumentFolder folder,
            Document document,
            string source,
            string documentType,
            string topic,
            IReadOnlyCollection<DocumentFolder> allFolders,
            out string reason)
        {
            decimal score = 0m;
            var reasons = new List<string>();

            var names = GetFolderTokens(folder);

            if (names.Any(x => source.Contains(x)))
            {
                score += 0.35m;
                reasons.Add("document content matches folder name");
            }

            if (!string.IsNullOrWhiteSpace(document.OriginalFileName) &&
                names.Any(x => document.OriginalFileName.Contains(x, StringComparison.OrdinalIgnoreCase)))
            {
                score += 0.15m;
                reasons.Add("file name matches folder");
            }

            if (MatchesType(names, documentType))
            {
                score += 0.40m;
                reasons.Add("folder matches document type");
            }

            if (MatchesTopic(names, topic))
            {
                score += 0.32m;
                reasons.Add("folder matches document topic");
            }

            var parent = folder.ParentFolderId is null
                ? null
                : allFolders.FirstOrDefault(x => x.Id == folder.ParentFolderId);

            if (parent is not null)
            {
                var parentNames = GetFolderTokens(parent);

                if (MatchesType(parentNames, documentType))
                {
                    score += 0.20m;
                    reasons.Add("parent folder matches document type");
                }

                if (MatchesTopic(parentNames, topic))
                {
                    score += 0.18m;
                    reasons.Add("parent folder matches document topic");
                }

                if (documentType == "cv" && topic == "it" &&
                    MatchesType(parentNames, "cv") && MatchesTopic(names, "it"))
                {
                    score += 0.45m;
                    reasons.Add("hierarchy matches CV/IT");
                }

                if (documentType == "documentation" && topic == "ksef" &&
                    MatchesType(parentNames, "documentation") && MatchesTopic(names, "ksef"))
                {
                    score += 0.45m;
                    reasons.Add("hierarchy matches Documentation/KSeF");
                }
            }

            score = Math.Min(score, 0.98m);
            reason = reasons.Count == 0
                ? "Weak semantic match."
                : string.Join("; ", reasons) + ".";

            return score;
        }

        private static bool MatchesType(string[] names, string documentType)
        {
            if (string.IsNullOrWhiteSpace(documentType) || documentType == "unknown")
            {
                return false;
            }

            if (names.Any(x => x == documentType || x.Contains(documentType)))
            {
                return true;
            }

            return CategoryAliases.TryGetValue(documentType, out var aliases) &&
                   aliases.Any(alias => names.Any(name => name.Contains(alias)));
        }

        private static bool MatchesTopic(string[] names, string topic)
        {
            if (string.IsNullOrWhiteSpace(topic) || topic == "general")
            {
                return false;
            }

            if (names.Any(x => x == topic || x.Contains(topic)))
            {
                return true;
            }

            return TopicAliases.TryGetValue(topic, out var aliases) &&
                   aliases.Any(alias => names.Any(name => name.Contains(alias)));
        }

        private static string[] GetFolderTokens(DocumentFolder folder)
        {
            return new[]
            {
                folder.Key,
                folder.Name,
                folder.NamePl,
                folder.NameEn,
                folder.NameUa
            }
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim().ToLowerInvariant())
            .Distinct()
            .ToArray();
        }

        private static DocumentFolderProposalDto? BuildDefaultProposal(
            string documentType,
            string topic,
            IReadOnlyCollection<DocumentFolder> existingFolders)
        {
            // CV/IT -> jeśli istnieje parent CV, twórz dziecko IT pod nim
            if (documentType == "cv" && topic == "it")
            {
                var cvParent = FindBestFolderBySemantic(existingFolders, "cv");
                if (cvParent is not null)
                {
                    return new DocumentFolderProposalDto
                    {
                        Key = "it",
                        Name = "IT",
                        NamePl = "IT",
                        NameEn = "IT",
                        NameUa = "IT",
                        ParentFolderId = cvParent.Id
                    };
                }
            }

            // Dokumentacja KSeF -> jeśli istnieje Dokumentacja, twórz dziecko KSeF pod nią
            if (documentType == "documentation" && topic == "ksef")
            {
                var documentationParent = FindBestFolderBySemantic(existingFolders, "documentation");
                if (documentationParent is not null)
                {
                    return new DocumentFolderProposalDto
                    {
                        Key = "ksef",
                        Name = "KSeF",
                        NamePl = "KSeF",
                        NameEn = "KSeF",
                        NameUa = "KSeF",
                        ParentFolderId = documentationParent.Id
                    };
                }
            }

            return documentType switch
            {
                "documentation" => NewProposal("documentation", "Documentation", "Dokumentacja", "Documentation", "Документація"),
                "architecture" => NewProposal("architecture", "Architecture", "Architektura", "Architecture", "Архітектура"),
                "api-docs" => NewProposal("api-docs", "API Docs", "Dokumentacja API", "API Docs", "Документація API"),
                "cv" => NewProposal("cv", "CV", "CV", "CV", "Резюме"),
                "invoices" => NewProposal("invoices", "Invoices", "Faktury", "Invoices", "Рахунки"),
                "contracts" => NewProposal("contracts", "Contracts", "Umowy", "Contracts", "Договори"),
                "reports" => NewProposal("reports", "Reports", "Raporty", "Reports", "Звіти"),
                "policies" => NewProposal("policies", "Policies", "Polityki", "Policies", "Політики"),
                _ => null
            };
        }

        private static DocumentFolder? FindBestFolderBySemantic(
            IReadOnlyCollection<DocumentFolder> folders,
            string semanticKey)
        {
            return folders
                .Select(folder => new
                {
                    Folder = folder,
                    Score = ScoreFolderSemantic(folder, semanticKey)
                })
                .Where(x => x.Score > 0m)
                .OrderByDescending(x => x.Score)
                .Select(x => x.Folder)
                .FirstOrDefault();
        }

        private static decimal ScoreFolderSemantic(DocumentFolder folder, string semanticKey)
        {
            var names = GetFolderTokens(folder);
            decimal score = 0m;

            if (names.Any(x => x == semanticKey))
            {
                score += 0.90m;
            }

            if (semanticKey == "documentation" &&
                CategoryAliases["documentation"].Any(alias => names.Any(name => name.Contains(alias))))
            {
                score += 0.35m;
            }

            if (semanticKey == "cv" &&
                CategoryAliases["cv"].Any(alias => names.Any(name => name.Contains(alias))))
            {
                score += 0.35m;
            }

            return Math.Min(score, 0.98m);
        }

        private static DocumentFolderProposalDto NewProposal(
            string key,
            string name,
            string namePl,
            string nameEn,
            string nameUa)
        {
            return new DocumentFolderProposalDto
            {
                Key = key,
                Name = name,
                NamePl = namePl,
                NameEn = nameEn,
                NameUa = nameUa,
                ParentFolderId = null
            };
        }

        private static string NormalizeKey(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "folder";
            }

            var cleaned = value
                .Replace("\\", "/")
                .Replace(">", "/")
                .Replace("|", "/")
                .Trim();

            var firstSegment = cleaned
                .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .FirstOrDefault();

            if (string.IsNullOrWhiteSpace(firstSegment))
            {
                return "folder";
            }

            var lower = firstSegment.Trim().ToLowerInvariant();
            var compact = Regex.Replace(lower, @"\s+", "-");
            var safe = Regex.Replace(compact, @"[^a-z0-9\-]", "");
            safe = Regex.Replace(safe, @"\-{2,}", "-").Trim('-');

            return string.IsNullOrWhiteSpace(safe) ? "folder" : safe;
        }

        private static string NormalizeText(string? value, string fallback)
        {
            return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        }

        private sealed class AiFolderAnalysisResult
        {
            public string? Category { get; set; }
            public decimal Confidence { get; set; }
            public string? Reason { get; set; }
            public Guid? ExistingFolderId { get; set; }
            public AiFolderProposal? ProposedFolder { get; set; }
        }

        private sealed class AiFolderProposal
        {
            public string? Key { get; set; }
            public string? Name { get; set; }
            public string? NamePl { get; set; }
            public string? NameEn { get; set; }
            public string? NameUa { get; set; }
            public Guid? ParentFolderId { get; set; }
        }
    }
}