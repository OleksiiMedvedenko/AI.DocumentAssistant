using AI.DocumentAssistant.Application.Abstractions.AI;
using AI.DocumentAssistant.Application.Abstractions.Documents;
using AI.DocumentAssistant.Application.Documents.Dtos;
using AI.DocumentAssistant.Domain.Entities;
using AI.DocumentAssistant.Domain.Enums;
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
                ],
                ["psychology"] =
                [
                    "psychologia", "psycholog", "psychologist", "psychoterapia", "therapy", "terapia"
                ]
            };

        // Topics that usually describe project/domain context inside a CV, not the candidate specialization.
        private static readonly HashSet<string> CvWeakTopics =
            new(StringComparer.OrdinalIgnoreCase)
            {
                "api",
                "ksef"
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
            var prioritySource = BuildPrioritySource(document);

            var highPrecision = TryHighPrecisionClassification(document, existingFolders, source, prioritySource);
            if (highPrecision is not null)
            {
                return highPrecision;
            }

            var local = AnalyzeLocally(document, existingFolders, source, prioritySource);

            var bestLocal = local.ExistingFolderCandidates
                .OrderByDescending(x => x.Score)
                .FirstOrDefault();

            // Stop early much more often to keep the classifier fast.
            if (bestLocal is not null && bestLocal.Score >= 0.64m)
            {
                local.SuggestedExistingFolderId = bestLocal.FolderId;
                local.Confidence = Math.Max(local.Confidence, bestLocal.Score);
                local.Reason = bestLocal.Reason;
                return local;
            }

            if (local.Confidence >= 0.80m)
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
            string source,
            string prioritySource)
        {
            // Prioritize CV detection before documentation to avoid CVs with project descriptions
            // being redirected to Documentation/API/KSeF.
            if (LooksLikeCv(prioritySource))
            {
                var topic = DetectTopicForCv(source, prioritySource);
                return BuildHighPrecisionResult(
                    documentType: "cv",
                    topic: topic,
                    existingFolders: existingFolders,
                    confidence: 0.97m,
                    reason: "Strong CV/resume indicators detected.");
            }

            if (LooksLikeTechnicalDocumentation(prioritySource) && !LooksLikeCv(prioritySource))
            {
                var topic = DetectTopic(source, "documentation");
                return BuildHighPrecisionResult(
                    documentType: "documentation",
                    topic: topic,
                    existingFolders: existingFolders,
                    confidence: topic == "ksef" ? 0.97m : 0.95m,
                    reason: topic == "ksef"
                        ? "Technical documentation about KSeF detected."
                        : "Technical documentation detected.");
            }

            if (LooksLikeInvoice(source) && !LooksLikeTechnicalDocumentation(prioritySource))
            {
                var topic = DetectTopic(source, "invoices");
                return BuildHighPrecisionResult(
                    documentType: "invoices",
                    topic: topic,
                    existingFolders: existingFolders,
                    confidence: 0.98m,
                    reason: "Strong invoice indicators detected.");
            }

            if (LooksLikeContract(prioritySource))
            {
                var topic = DetectTopic(source, "contracts");
                return BuildHighPrecisionResult(
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
            string source,
            string prioritySource)
        {
            var signals = BuildDocumentSignals(source, prioritySource);

            var typeConfidence = EstimateCategoryConfidence(source, signals.DocumentType);
            var topicConfidence = EstimateTopicConfidence(source, signals.Topic);

            var pathCandidates = BuildFolderPathCandidates(existingFolders, document, source, signals);

            var ordered = pathCandidates
                .Take(7)
                .Select(x => new DocumentFolderCandidateDto
                {
                    FolderId = x.Leaf.Id,
                    FolderKey = x.Leaf.Key,
                    FolderName = x.Leaf.Name,
                    Score = x.Score,
                    Reason = x.Reason
                })
                .ToList();

            var bestExisting = ordered.FirstOrDefault();
            var finalConfidence = new[] { typeConfidence, topicConfidence, bestExisting?.Score ?? 0m }.Max();

            return new DocumentFolderAnalysisResultDto
            {
                Category = signals.DocumentType,
                Confidence = finalConfidence <= 0m ? 0.35m : finalConfidence,
                Reason = bestExisting?.Reason
                    ?? (!string.Equals(signals.DocumentType, "unknown", StringComparison.OrdinalIgnoreCase)
                        ? $"Detected type '{signals.DocumentType}' with topic '{signals.Topic}'."
                        : "Category is unknown."),
                SuggestedExistingFolderId = bestExisting?.FolderId,
                ExistingFolderCandidates = ordered,
                ProposedFolder = BuildDefaultProposal(signals.DocumentType, signals.Topic, existingFolders)
            };
        }

        private async Task<DocumentFolderAnalysisResultDto?> TryAnalyzeWithAiAsync(
            Document document,
            IReadOnlyCollection<DocumentFolder> existingFolders,
            CancellationToken cancellationToken)
        {
            // Keep prompt size bounded for latency.
            var foldersText = string.Join(
                "\n",
                existingFolders
                    .Take(30)
                    .Select(x =>
                        $"- id: {x.Id}, parentId: {x.ParentFolderId}, key: {x.Key}, names: [{x.NamePl} | {x.NameEn} | {x.NameUa} | {x.Name}]"));

            var maxExcerpt = document.ProcessingProfile == DocumentProcessingProfile.HighAccuracyCv
                ? 5000
                : 2500;

            var sampleText = string.IsNullOrWhiteSpace(document.ExtractedText)
                ? string.Empty
                : DocumentAnalysisPreviewBuilder.Build(document.ExtractedText, maxExcerpt);

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
- A CV mentioning API/KSeF projects is still a CV.
- Prefer the deepest suitable existing folder only when the hierarchy is consistent.
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
                        Score = Math.Max(ai.Confidence, 0.72m),
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

            var previewLimit = document.ProcessingProfile == DocumentProcessingProfile.HighAccuracyCv
                ? 5500
                : 3000;

            var preview = DocumentAnalysisPreviewBuilder.Build(document.ExtractedText, previewLimit);
            if (!string.IsNullOrWhiteSpace(preview))
            {
                builder.AppendLine(preview);
            }

            return builder.ToString().ToLowerInvariant();
        }

        private static string BuildPrioritySource(Document document)
        {
            var builder = new StringBuilder();
            builder.AppendLine(document.OriginalFileName);
            builder.AppendLine(document.ContentType);

            if (!string.IsNullOrWhiteSpace(document.ExtractedText))
            {
                builder.AppendLine(document.ExtractedText[..Math.Min(document.ExtractedText.Length, 2500)]);
            }

            return builder.ToString().ToLowerInvariant();
        }

        private static DocumentSignals BuildDocumentSignals(string source, string prioritySource)
        {
            var documentType = DetectDocumentType(source, prioritySource);
            var topic = documentType == "cv"
                ? DetectTopicForCv(source, prioritySource)
                : DetectTopic(source, documentType);

            var tokens = Tokenize(source);

            var typeAliases = CategoryAliases.TryGetValue(documentType, out var foundTypeAliases)
                ? new HashSet<string>(foundTypeAliases, StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var topicAliases = TopicAliases.TryGetValue(topic, out var foundTopicAliases)
                ? new HashSet<string>(foundTopicAliases, StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            return new DocumentSignals
            {
                DocumentType = documentType,
                Topic = topic,
                Tokens = tokens,
                TypeAliases = typeAliases,
                TopicAliases = topicAliases
            };
        }

        private static string DetectDocumentType(string source, string prioritySource)
        {
            var scores = CategoryAliases.Keys.ToDictionary(x => x, _ => 0m, StringComparer.OrdinalIgnoreCase);

            foreach (var pair in CategoryAliases)
            {
                scores[pair.Key] += CountMatches(source, pair.Value) * 1.0m;
                scores[pair.Key] += CountMatches(prioritySource, pair.Value) * 1.4m;
            }

            if (LooksLikeCv(prioritySource))
            {
                scores["cv"] += 4.0m;
                scores["documentation"] -= 1.0m;
                scores["api-docs"] -= 0.5m;
            }

            if (LooksLikeInvoice(source) && !LooksLikeTechnicalDocumentation(prioritySource))
            {
                scores["invoices"] += 4.0m;
            }

            if (LooksLikeTechnicalDocumentation(prioritySource) && !LooksLikeCv(prioritySource))
            {
                scores["documentation"] += 3.0m;
            }

            if (LooksLikeContract(prioritySource))
            {
                scores["contracts"] += 3.5m;
            }

            var best = scores
                .OrderByDescending(x => x.Value)
                .FirstOrDefault();

            return best.Value <= 0m ? "unknown" : best.Key;
        }

        private static string DetectTopic(string source, string documentType)
        {
            var scores = TopicAliases.Keys.ToDictionary(x => x, _ => 0m, StringComparer.OrdinalIgnoreCase);

            foreach (var pair in TopicAliases)
            {
                scores[pair.Key] += CountMatches(source, pair.Value);
            }

            // Dynamic biasing by document type.
            if (documentType == "documentation")
            {
                scores["api"] += 0.5m;
                scores["ksef"] += 0.5m;
            }

            if (documentType == "invoices")
            {
                scores["finance"] += 0.8m;
            }

            if (documentType == "contracts")
            {
                scores["legal"] += 0.8m;
            }

            var best = scores
                .OrderByDescending(x => x.Value)
                .FirstOrDefault();

            return best.Value < 1m ? "general" : best.Key;
        }

        private static string DetectTopicForCv(string source, string prioritySource)
        {
            // CV specialization should be driven mostly by the profile / header portion,
            // not by project descriptions later in the document.
            var strongTopics = new[] { "it", "legal", "finance", "hr", "psychology" };

            var scores = strongTopics.ToDictionary(x => x, _ => 0m, StringComparer.OrdinalIgnoreCase);

            foreach (var topic in strongTopics)
            {
                if (!TopicAliases.TryGetValue(topic, out var aliases))
                {
                    continue;
                }

                scores[topic] += CountMatches(source, aliases) * 0.7m;
                scores[topic] += CountMatches(prioritySource, aliases) * 1.6m;
            }

            var bestStrong = scores
                .OrderByDescending(x => x.Value)
                .FirstOrDefault();

            if (bestStrong.Value >= 1.2m)
            {
                return bestStrong.Key;
            }

            // Weak project/domain topics like API/KSeF should not override CV specialization.
            var weakScores = CvWeakTopics.ToDictionary(x => x, _ => 0m, StringComparer.OrdinalIgnoreCase);

            foreach (var topic in CvWeakTopics)
            {
                if (!TopicAliases.TryGetValue(topic, out var aliases))
                {
                    continue;
                }

                weakScores[topic] += CountMatches(prioritySource, aliases) * 0.6m;
            }

            var bestWeak = weakScores
                .OrderByDescending(x => x.Value)
                .FirstOrDefault();

            return bestWeak.Value >= 2.0m ? bestWeak.Key : "general";
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

            var matches = CountMatches(source, aliases);

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

            var matches = CountMatches(source, aliases);

            return matches switch
            {
                >= 4 => 0.92m,
                3 => 0.84m,
                2 => 0.74m,
                1 => 0.58m,
                _ => 0.35m
            };
        }

        private static List<FolderPathCandidate> BuildFolderPathCandidates(
            IReadOnlyCollection<DocumentFolder> folders,
            Document document,
            string source,
            DocumentSignals signals)
        {
            var result = new List<FolderPathCandidate>();

            foreach (var leaf in folders)
            {
                var parent = leaf.ParentFolderId is null
                    ? null
                    : folders.FirstOrDefault(x => x.Id == leaf.ParentFolderId);

                var pathTokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var token in GetFolderTokens(leaf))
                {
                    pathTokens.Add(token);
                }

                if (parent is not null)
                {
                    foreach (var token in GetFolderTokens(parent))
                    {
                        pathTokens.Add(token);
                    }
                }

                var score = ScorePath(pathTokens, leaf, parent, document, source, signals, out var reason);
                if (score <= 0m)
                {
                    continue;
                }

                result.Add(new FolderPathCandidate
                {
                    Leaf = leaf,
                    Parent = parent,
                    PathKey = parent is null ? leaf.Key : $"{parent.Key}/{leaf.Key}",
                    PathDisplay = parent is null ? leaf.Name : $"{parent.Name} -> {leaf.Name}",
                    Tokens = pathTokens,
                    Score = score,
                    Reason = reason
                });
            }

            return result
                .OrderByDescending(x => x.Score)
                .ThenByDescending(x => x.Parent is not null)
                .ThenBy(x => x.PathDisplay)
                .ToList();
        }

        private static decimal ScorePath(
            HashSet<string> pathTokens,
            DocumentFolder leaf,
            DocumentFolder? parent,
            Document document,
            string source,
            DocumentSignals signals,
            out string reason)
        {
            decimal score = 0m;
            var reasons = new List<string>();

            var leafTokens = GetFolderTokens(leaf);
            var parentTokens = parent is null ? Array.Empty<string>() : GetFolderTokens(parent);

            var leafTypeMatch = MatchesSemantic(leafTokens, signals.DocumentType, signals.TypeAliases);
            var parentTypeMatch = MatchesSemantic(parentTokens, signals.DocumentType, signals.TypeAliases);

            var leafTopicMatch = signals.Topic != "general" &&
                                 MatchesSemantic(leafTokens, signals.Topic, signals.TopicAliases);

            var parentTopicMatch = signals.Topic != "general" &&
                                   MatchesSemantic(parentTokens, signals.Topic, signals.TopicAliases);

            if (leafTypeMatch)
            {
                score += 0.26m;
                reasons.Add("leaf matches document type");
            }

            if (parentTypeMatch)
            {
                score += 0.34m;
                reasons.Add("parent matches document type");
            }

            if (leafTopicMatch)
            {
                score += 0.30m;
                reasons.Add("leaf matches document topic");
            }

            if (parentTopicMatch)
            {
                score += 0.12m;
                reasons.Add("parent matches document topic");
            }

            var overlapCount = pathTokens.Count(token => signals.Tokens.Contains(token));
            if (overlapCount > 0)
            {
                score += Math.Min(0.14m, overlapCount * 0.03m);
                reasons.Add("path tokens appear in document content");
            }

            if (!string.IsNullOrWhiteSpace(document.OriginalFileName) &&
                pathTokens.Any(x => document.OriginalFileName.Contains(x, StringComparison.OrdinalIgnoreCase)))
            {
                score += 0.06m;
                reasons.Add("path tokens appear in file name");
            }

            if (parent is not null && parentTypeMatch && leafTopicMatch)
            {
                score += 0.10m;
                reasons.Add("parent-child hierarchy is consistent");
            }

            var leafSpecialization = DetectFolderSpecialization(leafTokens);
            if (signals.DocumentType == "cv" &&
                signals.Topic != "general" &&
                parentTypeMatch &&
                leafSpecialization is not null &&
                !string.Equals(leafSpecialization, signals.Topic, StringComparison.OrdinalIgnoreCase))
            {
                score -= 0.28m;
                reasons.Add("specialized leaf does not match CV topic");
            }

            if (signals.DocumentType == "cv" &&
                signals.Topic == "general" &&
                parentTypeMatch &&
                leafSpecialization is not null)
            {
                score -= 0.14m;
                reasons.Add("generic CV should not prefer a specialized child");
            }

            score = Math.Clamp(score, 0m, 0.98m);

            reason = reasons.Count == 0
                ? "Weak path match."
                : string.Join("; ", reasons) + ".";

            return score;
        }

        private static string? DetectFolderSpecialization(string[] tokens)
        {
            foreach (var pair in TopicAliases)
            {
                if (pair.Key == "api" || pair.Key == "ksef")
                {
                    continue;
                }

                if (pair.Value.Any(alias => tokens.Any(t => t.Contains(alias, StringComparison.OrdinalIgnoreCase))))
                {
                    return pair.Key;
                }
            }

            return null;
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

            return CountMatches(source, invoiceSignals) >= 3;
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

            return CountMatches(source, documentationSignals) >= 2;
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

            return CountMatches(source, strongSignals) >= 3;
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

            return CountMatches(source, strongSignals) >= 2;
        }

        private static DocumentFolderAnalysisResultDto BuildHighPrecisionResult(
            string documentType,
            string topic,
            IReadOnlyCollection<DocumentFolder> existingFolders,
            decimal confidence,
            string reason)
        {
            var signals = new DocumentSignals
            {
                DocumentType = documentType,
                Topic = topic,
                Tokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                TypeAliases = CategoryAliases.TryGetValue(documentType, out var typeAliases)
                    ? new HashSet<string>(typeAliases, StringComparer.OrdinalIgnoreCase)
                    : new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                TopicAliases = TopicAliases.TryGetValue(topic, out var topicAliases)
                    ? new HashSet<string>(topicAliases, StringComparer.OrdinalIgnoreCase)
                    : new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            };

            var best = BuildFolderPathCandidates(existingFolders, new Document(), string.Empty, signals)
                .FirstOrDefault();

            return new DocumentFolderAnalysisResultDto
            {
                Category = documentType,
                Confidence = confidence,
                Reason = reason,
                SuggestedExistingFolderId = best?.Leaf.Id,
                ExistingFolderCandidates = best is null
                    ? new List<DocumentFolderCandidateDto>()
                    : new List<DocumentFolderCandidateDto>
                    {
                        new DocumentFolderCandidateDto
                        {
                            FolderId = best.Leaf.Id,
                            FolderKey = best.Leaf.Key,
                            FolderName = best.Leaf.Name,
                            Score = Math.Max(best.Score, 0.60m),
                            Reason = best.Reason
                        }
                    },
                ProposedFolder = BuildDefaultProposal(documentType, topic, existingFolders)
            };
        }

        private static bool MatchesSemantic(
            string[] folderTokens,
            string key,
            IEnumerable<string> aliases)
        {
            if (string.IsNullOrWhiteSpace(key) || key is "unknown" or "general")
            {
                return false;
            }

            if (folderTokens.Any(x => x.Equals(key, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }

            if (folderTokens.Any(x => x.Contains(key, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }

            foreach (var alias in aliases)
            {
                if (folderTokens.Any(x => x.Contains(alias, StringComparison.OrdinalIgnoreCase)))
                {
                    return true;
                }
            }

            return false;
        }

        private static int CountMatches(string source, IEnumerable<string> aliases)
        {
            return aliases
                .Where(alias => !string.IsNullOrWhiteSpace(alias))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count(source.Contains);
        }

        private static HashSet<string> Tokenize(string source)
        {
            return Regex.Split(source.ToLowerInvariant(), @"[^a-zA-Z0-9ąćęłńóśźżА-Яа-яІіЇїЄє]+")
                .Where(x => x.Length >= 3)
                .Distinct()
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
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
            .SelectMany(SplitFolderTokens)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        }

        private static IEnumerable<string> SplitFolderTokens(string value)
        {
            return Regex.Split(value.Trim().ToLowerInvariant(), @"[^a-zA-Z0-9ąćęłńóśźżА-Яа-яІіЇїЄє]+")
                .Where(x => !string.IsNullOrWhiteSpace(x));
        }

        private static DocumentFolderProposalDto? BuildDefaultProposal(
            string documentType,
            string topic,
            IReadOnlyCollection<DocumentFolder> existingFolders)
        {
            if (documentType == "cv" && topic != "general")
            {
                var cvParent = FindBestFolderBySemantic(existingFolders, "cv");
                if (cvParent is not null)
                {
                    var display = topic switch
                    {
                        "it" => "IT",
                        "legal" => "Legal",
                        "finance" => "Finance",
                        "hr" => "HR",
                        "psychology" => "Psychology",
                        _ => topic
                    };

                    return new DocumentFolderProposalDto
                    {
                        Key = topic,
                        Name = display,
                        NamePl = display,
                        NameEn = display,
                        NameUa = display,
                        ParentFolderId = cvParent.Id
                    };
                }
            }

            if (documentType == "documentation" && topic != "general")
            {
                var documentationParent = FindBestFolderBySemantic(existingFolders, "documentation");
                if (documentationParent is not null)
                {
                    var display = topic switch
                    {
                        "ksef" => "KSeF",
                        "api" => "API",
                        _ => topic
                    };

                    return new DocumentFolderProposalDto
                    {
                        Key = topic,
                        Name = display,
                        NamePl = display,
                        NameEn = display,
                        NameUa = display,
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

        private sealed class DocumentSignals
        {
            public string DocumentType { get; init; } = "unknown";
            public string Topic { get; init; } = "general";
            public HashSet<string> Tokens { get; init; } = new(StringComparer.OrdinalIgnoreCase);
            public HashSet<string> TypeAliases { get; init; } = new(StringComparer.OrdinalIgnoreCase);
            public HashSet<string> TopicAliases { get; init; } = new(StringComparer.OrdinalIgnoreCase);
        }

        private sealed class FolderPathCandidate
        {
            public DocumentFolder Leaf { get; init; } = default!;
            public DocumentFolder? Parent { get; init; }
            public string PathKey { get; init; } = string.Empty;
            public string PathDisplay { get; init; } = string.Empty;
            public HashSet<string> Tokens { get; init; } = new(StringComparer.OrdinalIgnoreCase);
            public decimal Score { get; init; }
            public string Reason { get; init; } = string.Empty;
        }
    }
}