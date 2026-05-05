using System.Text.Json;
using System.Text.RegularExpressions;
using AI.DocumentAssistant.Application.Abstractions.AI;
using AI.DocumentAssistant.Application.Abstractions.Documents;
using AI.DocumentAssistant.Application.Documents.Dtos;
using AI.DocumentAssistant.Domain.Entities;
using AI.DocumentAssistant.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AI.DocumentAssistant.Application.Documents.Services;

public sealed class DocumentIntelligenceService : IDocumentIntelligenceService
{
    private readonly AppDbContext _dbContext;
    private readonly IEmbeddingService _embeddingService;

    public DocumentIntelligenceService(AppDbContext dbContext, IEmbeddingService embeddingService)
    {
        _dbContext = dbContext;
        _embeddingService = embeddingService;
    }

    public async Task<DocumentIntelligenceSnapshotDto> EnsureSnapshotAsync(
        Document document,
        CancellationToken cancellationToken)
    {
        var existing = await _dbContext.DocumentIntelligenceSnapshots
            .FirstOrDefaultAsync(x => x.DocumentId == document.Id, cancellationToken);

        var source = BuildSource(document);
        var documentType = DetectDocumentType(source, document.OriginalFileName);
        var domain = DetectBusinessDomain(source, documentType);
        var topic = DetectTopic(source, documentType, domain);
        var (year, month) = DetectYearMonth(source, document.UploadedAtUtc);
        var dueDate = DetectDateAfterLabels(source, ["due", "payment due", "termin płatności", "termin platnosci"]);
        var effectiveDate = DetectDateAfterLabels(source, ["effective", "effective date", "data wejścia", "data wejscia"]);
        var keywords = ExtractKeywords(source, documentType, domain, topic);
        var confidence = CalculateConfidence(documentType, domain, topic, keywords.Count);
        var reason = BuildReason(documentType, domain, topic, year, month, keywords);
        var now = DateTime.UtcNow;

        if (existing is null)
        {
            existing = new DocumentIntelligenceSnapshot
            {
                Id = Guid.NewGuid(),
                DocumentId = document.Id,
                UserId = document.UserId,
                CreatedAtUtc = now
            };

            _dbContext.DocumentIntelligenceSnapshots.Add(existing);
        }

        existing.DocumentType = documentType;
        existing.BusinessDomain = domain;
        existing.Topic = topic;
        existing.Year = year;
        existing.Month = month;
        existing.DueDate = dueDate;
        existing.EffectiveDate = effectiveDate;
        existing.Confidence = confidence;
        existing.Keywords = string.Join(",", keywords);
        existing.Reason = reason;
        existing.UpdatedAtUtc = now;

        await _dbContext.SaveChangesAsync(cancellationToken);
        return ToDto(existing);
    }

    public async Task UpdateFolderProfilesAsync(Guid userId, IEnumerable<Guid> folderIds, CancellationToken cancellationToken)
    {
        foreach (var folderId in folderIds.Distinct())
        {
            await UpdateFolderProfileAsync(userId, folderId, cancellationToken);
        }
    }

    public async Task UpdateFolderProfileAsync(Guid userId, Guid folderId, CancellationToken cancellationToken)
    {
        var folder = await _dbContext.DocumentFolders
            .FirstOrDefaultAsync(x => x.Id == folderId && x.UserId == userId, cancellationToken);

        if (folder is null)
        {
            return;
        }

        var documents = await _dbContext.Documents
            .Where(x => x.UserId == userId && x.FolderId == folderId)
            .OrderByDescending(x => x.UploadedAtUtc)
            .Take(25)
            .Select(x => new
            {
                x.OriginalFileName,
                x.QuickSummary,
                x.Summary,
                x.ExtractedText
            })
            .ToListAsync(cancellationToken);

        var source = BuildFolderSource(folder, documents.Select(x =>
            $"{x.OriginalFileName} {x.QuickSummary} {x.Summary} {Take(x.ExtractedText, 700)}"));

        float[] embedding;
        try
        {
            embedding = await _embeddingService.GenerateEmbeddingAsync(source, cancellationToken);
        }
        catch
        {
            embedding = [];
        }

        var profile = await _dbContext.FolderEmbeddingProfiles
            .FirstOrDefaultAsync(x => x.FolderId == folderId && x.UserId == userId, cancellationToken);

        if (profile is null)
        {
            profile = new FolderEmbeddingProfile
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                FolderId = folderId
            };
            _dbContext.FolderEmbeddingProfiles.Add(profile);
        }

        profile.SourceText = Take(source, 4000);
        profile.EmbeddingJson = JsonSerializer.Serialize(embedding);
        profile.DocumentCount = documents.Count;
        profile.UpdatedAtUtc = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<Dictionary<Guid, decimal>> CalculateFolderSimilaritiesAsync(
        Document document,
        IReadOnlyCollection<DocumentFolder> folders,
        CancellationToken cancellationToken)
    {
        if (folders.Count == 0)
        {
            return new Dictionary<Guid, decimal>();
        }

        var source = BuildSource(document);
        float[] documentEmbedding;
        try
        {
            documentEmbedding = await _embeddingService.GenerateEmbeddingAsync(source, cancellationToken);
        }
        catch
        {
            return new Dictionary<Guid, decimal>();
        }

        var folderIds = folders.Select(x => x.Id).ToList();
        var profiles = await _dbContext.FolderEmbeddingProfiles
            .Where(x => x.UserId == document.UserId && folderIds.Contains(x.FolderId))
            .ToListAsync(cancellationToken);

        var missingProfileFolderIds = folderIds.Except(profiles.Select(x => x.FolderId)).ToList();
        foreach (var missingId in missingProfileFolderIds)
        {
            await UpdateFolderProfileAsync(document.UserId, missingId, cancellationToken);
        }

        profiles = await _dbContext.FolderEmbeddingProfiles
            .Where(x => x.UserId == document.UserId && folderIds.Contains(x.FolderId))
            .ToListAsync(cancellationToken);

        var result = new Dictionary<Guid, decimal>();
        foreach (var profile in profiles)
        {
            var folderEmbedding = DeserializeEmbedding(profile.EmbeddingJson);
            if (folderEmbedding.Length == 0)
            {
                continue;
            }

            var similarity = CosineSimilarity(documentEmbedding, folderEmbedding);
            result[profile.FolderId] = Math.Round(Math.Clamp((decimal)similarity, 0m, 1m), 4);
        }

        return result;
    }

    public static DocumentIntelligenceSnapshotDto ToDto(DocumentIntelligenceSnapshot snapshot)
    {
        return new DocumentIntelligenceSnapshotDto
        {
            DocumentId = snapshot.DocumentId,
            DocumentType = snapshot.DocumentType,
            BusinessDomain = snapshot.BusinessDomain,
            Topic = snapshot.Topic,
            Year = snapshot.Year,
            Month = snapshot.Month,
            DueDate = snapshot.DueDate,
            EffectiveDate = snapshot.EffectiveDate,
            Confidence = snapshot.Confidence,
            Keywords = snapshot.Keywords.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            Reason = snapshot.Reason,
            UpdatedAtUtc = snapshot.UpdatedAtUtc
        };
    }

    private static string BuildSource(Document document)
    {
        return Take($"{document.OriginalFileName}\n{document.QuickSummary}\n{document.Summary}\n{document.ExtractedText}", 8000).ToLowerInvariant();
    }

    private static string BuildFolderSource(DocumentFolder folder, IEnumerable<string> documentTexts)
    {
        return Take($"{folder.Name} {folder.NameEn} {folder.NamePl} {folder.Key}\n" + string.Join("\n", documentTexts), 8000).ToLowerInvariant();
    }

    private static string DetectDocumentType(string source, string fileName)
    {
        if (ContainsAny(source, "invoice", "faktura", "vat", "seller", "buyer", "payment due", "kwota brutto", "nip")) return "invoice";
        if (ContainsAny(source, "contract", "agreement", "umowa", "parties", "signature", "effective date")) return "contract";
        if (ContainsAny(source, "cv", "resume", "curriculum vitae", "candidate", "work experience", "skills", "education")) return "cv";
        if (ContainsAny(source, "api", "openapi", "swagger", "endpoint", "documentation", "readme", "manual", "guide")) return "technicalDocumentation";
        if (ContainsAny(source, "report", "raport", "analysis", "summary", "podsumowanie")) return "report";
        if (ContainsAny(source, "policy", "procedure", "regulation", "gdpr", "rodo", "privacy")) return "policy";
        return Path.GetExtension(fileName).Equals(".csv", StringComparison.OrdinalIgnoreCase) ? "report" : "other";
    }

    private static string DetectBusinessDomain(string source, string documentType)
    {
        if (documentType == "invoice" || ContainsAny(source, "vat", "tax", "payment", "accounting", "księgowość", "ksef")) return "finance";
        if (documentType == "cv" || ContainsAny(source, "candidate", "recruitment", "hiring", "hr")) return "hr";
        if (documentType == "contract" || ContainsAny(source, "legal", "compliance", "umowa", "agreement")) return "legal";
        if (documentType == "technicalDocumentation" || ContainsAny(source, "api", "software", "backend", "frontend", "database", "integration")) return "it";
        if (ContainsAny(source, "project", "roadmap", "milestone", "delivery")) return "project";
        return "unknown";
    }

    private static string DetectTopic(string source, string documentType, string domain)
    {
        if (ContainsAny(source, "ksef", "krajowy system e-faktur", "e-faktur")) return "ksef";
        if (ContainsAny(source, "openapi", "swagger", "endpoint", "rest", "graphql")) return "api";
        if (ContainsAny(source, ".net", "dotnet", "c#", "backend")) return "backend";
        if (ContainsAny(source, "react", "angular", "frontend")) return "frontend";
        if (documentType == "invoice") return "invoices";
        if (documentType == "cv") return "candidates";
        if (documentType == "contract") return "contracts";
        return domain;
    }

    private static (int? Year, int? Month) DetectYearMonth(string source, DateTime fallback)
    {
        var dateMatch = Regex.Match(source, @"\b(?<year>20\d{2})[-./](?<month>0?[1-9]|1[0-2])[-./]\d{1,2}\b");
        if (dateMatch.Success)
        {
            return (int.Parse(dateMatch.Groups["year"].Value), int.Parse(dateMatch.Groups["month"].Value));
        }

        var yearMatch = Regex.Match(source, @"\b(20\d{2})\b");
        if (yearMatch.Success)
        {
            return (int.Parse(yearMatch.Value), null);
        }

        return (fallback.Year, fallback.Month);
    }

    private static DateTime? DetectDateAfterLabels(string source, string[] labels)
    {
        foreach (var label in labels)
        {
            var match = Regex.Match(source, $@"{Regex.Escape(label)}[^0-9]{{0,30}}(?<date>20\d{{2}}[-./](0?[1-9]|1[0-2])[-./](0?[1-9]|[12]\d|3[01]))", RegexOptions.IgnoreCase);
            if (match.Success && DateTime.TryParse(match.Groups["date"].Value.Replace('.', '-').Replace('/', '-'), out var date))
            {
                return date;
            }
        }

        return null;
    }

    private static List<string> ExtractKeywords(string source, string documentType, string domain, string topic)
    {
        var keywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { documentType, domain, topic };
        foreach (var token in Regex.Matches(source, @"[a-zA-ZąćęłńóśźżĄĆĘŁŃÓŚŹŻ0-9#\.]{4,}").Select(x => x.Value.ToLowerInvariant()))
        {
            if (keywords.Count >= 15) break;
            if (StopWords.Contains(token)) continue;
            keywords.Add(token);
        }
        return keywords.Where(x => !string.IsNullOrWhiteSpace(x) && x != "unknown").Take(15).ToList();
    }

    private static decimal CalculateConfidence(string documentType, string domain, string topic, int keywordCount)
    {
        var score = 0.35m;
        if (documentType != "other") score += 0.25m;
        if (domain != "unknown") score += 0.20m;
        if (topic != "unknown") score += 0.10m;
        score += Math.Min(0.10m, keywordCount * 0.01m);
        return Math.Clamp(score, 0m, 0.98m);
    }

    private static string BuildReason(string documentType, string domain, string topic, int? year, int? month, IReadOnlyCollection<string> keywords)
    {
        var period = year.HasValue ? month.HasValue ? $", period {year.Value:0000}-{month.Value:00}" : $", year {year.Value}" : string.Empty;
        return $"Detected type '{documentType}', domain '{domain}', topic '{topic}'{period}. Keywords: {string.Join(", ", keywords.Take(8))}.";
    }

    private static bool ContainsAny(string source, params string[] values)
    {
        return values.Any(value => source.Contains(value, StringComparison.OrdinalIgnoreCase));
    }

    private static float[] DeserializeEmbedding(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<float[]>(json) ?? [];
        }
        catch
        {
            return [];
        }
    }

    private static double CosineSimilarity(float[] first, float[] second)
    {
        var length = Math.Min(first.Length, second.Length);
        if (length == 0) return 0;

        double dot = 0;
        double firstNorm = 0;
        double secondNorm = 0;
        for (var i = 0; i < length; i++)
        {
            dot += first[i] * second[i];
            firstNorm += first[i] * first[i];
            secondNorm += second[i] * second[i];
        }

        if (firstNorm == 0 || secondNorm == 0) return 0;
        return dot / (Math.Sqrt(firstNorm) * Math.Sqrt(secondNorm));
    }

    private static string Take(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var normalized = Regex.Replace(value.Trim(), @"\s+", " ");
        return normalized.Length <= maxLength ? normalized : normalized[..maxLength];
    }

    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "this", "that", "with", "from", "have", "will", "your", "document", "content", "file",
        "oraz", "jest", "oraz", "dla", "the", "and", "for", "are", "was", "were"
    };
}
