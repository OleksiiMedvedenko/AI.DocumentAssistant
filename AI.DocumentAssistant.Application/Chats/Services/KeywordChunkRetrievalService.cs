using System.Text.RegularExpressions;
using AI.DocumentAssistant.Application.Abstractions.Chats;
using AI.DocumentAssistant.Domain.Entities;
using Microsoft.Extensions.Options;

namespace AI.DocumentAssistant.Application.Chats.Services;

public sealed class KeywordChunkRetrievalService : IChunkRetrievalService
{
    private static readonly Regex TokenRegex = new(@"\p{L}[\p{L}\p{Nd}_-]*", RegexOptions.Compiled);
    private static readonly Regex YearRegex = new(@"\b\d+\+?\s*(year|years|yr|yrs)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly HashSet<string> _stopWords;
    private readonly ChatRetrievalOptions _options;

    public KeywordChunkRetrievalService(IOptions<ChatRetrievalOptions> options)
    {
        _options = options.Value;
        _stopWords = _options.StopWords.ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<DocumentChunk> GetBestMatchingChunks(
        IReadOnlyCollection<DocumentChunk> chunks,
        string question,
        IReadOnlyCollection<string>? chatHistory = null,
        int take = 6)
    {
        if (chunks.Count == 0)
        {
            return [];
        }

        var finalTake = take > 0 ? take : _options.DefaultTake;
        var orderedChunks = chunks.OrderBy(x => x.ChunkIndex).ToList();

        var combinedQuery = BuildCombinedQuery(question, chatHistory);
        var keywords = ExpandKeywords(ExtractKeywords(combinedQuery), question, chatHistory);
        var phraseBoostTerms = ExtractPhraseBoostTerms(question);
        var normalizedQuestion = Normalize(question);

        if (keywords.Count == 0 && phraseBoostTerms.Count == 0)
        {
            return orderedChunks.Take(finalTake).ToList();
        }

        var ranked = orderedChunks
            .Select(chunk => new
            {
                Chunk = chunk,
                Score = CalculateScore(chunk.Text, keywords, phraseBoostTerms, normalizedQuestion)
            })
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Chunk.ChunkIndex)
            .ToList();

        var best = ranked
            .Where(x => x.Score > 0)
            .Take(finalTake)
            .Select(x => x.Chunk)
            .ToList();

        if (best.Count == 0)
        {
            best = orderedChunks.Take(finalTake).ToList();
        }

        if (_options.IncludeNeighborChunks)
        {
            best = ExpandWithNeighbors(orderedChunks, best, finalTake);
        }

        return best
            .OrderBy(x => x.ChunkIndex)
            .ToList();
    }

    private static string BuildCombinedQuery(string question, IReadOnlyCollection<string>? chatHistory)
    {
        if (chatHistory is null || chatHistory.Count == 0)
        {
            return question ?? string.Empty;
        }

        var history = string.Join(" ", chatHistory.Where(x => !string.IsNullOrWhiteSpace(x)));
        return $"{history} {question}".Trim();
    }

    private HashSet<string> ExtractKeywords(string text)
    {
        var matches = TokenRegex.Matches((text ?? string.Empty).ToLowerInvariant());

        return matches
            .Select(x => x.Value.Trim())
            .Where(x => x.Length >= 2)
            .Where(x => !_stopWords.Contains(x))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private HashSet<string> ExpandKeywords(
        HashSet<string> baseKeywords,
        string question,
        IReadOnlyCollection<string>? chatHistory)
    {
        var expanded = new HashSet<string>(baseKeywords, StringComparer.OrdinalIgnoreCase);

        var combined = $"{question} {string.Join(" ", chatHistory ?? [])}".ToLowerInvariant();

        AddSynonymGroupIfMatch(expanded, combined,
            ["experience", "experienced", "exp", "years", "year", "commercial", "worked", "employment", "career", "background", "seniority"]);

        AddSynonymGroupIfMatch(expanded, combined,
            ["skill", "skills", "technology", "technologies", "stack", "tools", "framework", "frameworks"]);

        AddSynonymGroupIfMatch(expanded, combined,
            ["education", "degree", "university", "study", "studies", "college", "school"]);

        AddSynonymGroupIfMatch(expanded, combined,
            ["project", "projects", "client", "clients", "responsibilities", "responsibility"]);

        AddSynonymGroupIfMatch(expanded, combined,
            ["developer", "engineer", "programmer", "backend", "frontend", "fullstack", "full-stack", ".net", "c#", "react"]);

        if (combined.Contains("cv") || combined.Contains("resume"))
        {
            expanded.UnionWith(new[]
            {
                "experience", "skills", "education", "employment", "career", "developer", "engineer"
            });
        }

        if (combined.Contains("how many") || combined.Contains("ile"))
        {
            expanded.UnionWith(new[]
            {
                "years", "year", "experience"
            });
        }

        if (combined.Contains("senior") || combined.Contains("seniority"))
        {
            expanded.UnionWith(new[]
            {
                "experience", "years", "developer", "engineer"
            });
        }

        return expanded;
    }

    private static void AddSynonymGroupIfMatch(HashSet<string> keywords, string combinedText, IReadOnlyCollection<string> group)
    {
        if (group.Any(term => combinedText.Contains(term, StringComparison.OrdinalIgnoreCase)))
        {
            keywords.UnionWith(group);
        }
    }

    private static List<string> ExtractPhraseBoostTerms(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        return Regex.Matches(text.Trim(), "\"([^\"]+)\"")
            .Select(x => x.Groups[1].Value.Trim())
            .Where(x => x.Length >= 3)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static int CalculateScore(
        string chunkText,
        HashSet<string> keywords,
        IReadOnlyCollection<string> phraseBoostTerms,
        string normalizedQuestion)
    {
        if (string.IsNullOrWhiteSpace(chunkText))
        {
            return 0;
        }

        var normalizedChunk = Normalize(chunkText);
        var score = 0;
        var matchedKeywordCount = 0;

        foreach (var keyword in keywords)
        {
            var normalizedKeyword = Normalize(keyword);
            if (string.IsNullOrWhiteSpace(normalizedKeyword))
            {
                continue;
            }

            var occurrences = CountOccurrences(normalizedChunk, normalizedKeyword);
            if (occurrences <= 0)
            {
                continue;
            }

            matchedKeywordCount++;
            score += occurrences * 10;

            if (normalizedChunk.Contains($"{normalizedKeyword}:"))
            {
                score += 8;
            }

            if (normalizedChunk.StartsWith(normalizedKeyword + " "))
            {
                score += 4;
            }
        }

        score += matchedKeywordCount * 6;

        foreach (var phrase in phraseBoostTerms)
        {
            var normalizedPhrase = Normalize(phrase);
            if (!string.IsNullOrWhiteSpace(normalizedPhrase) &&
                normalizedChunk.Contains(normalizedPhrase, StringComparison.Ordinal))
            {
                score += 25;
            }
        }

        if (LooksLikeExperienceQuestion(normalizedQuestion))
        {
            if (ContainsAny(normalizedChunk, "experience", "employment", "career", "worked", "developer", "engineer"))
            {
                score += 25;
            }

            if (YearRegex.IsMatch(chunkText))
            {
                score += 30;
            }
        }

        if (LooksLikeSkillsQuestion(normalizedQuestion) &&
            ContainsAny(normalizedChunk, "skills", "stack", "technology", "technologies", "framework", "tools"))
        {
            score += 25;
        }

        if (LooksLikeEducationQuestion(normalizedQuestion) &&
            ContainsAny(normalizedChunk, "education", "degree", "university", "college", "school"))
        {
            score += 25;
        }

        if (normalizedChunk.Length <= 1200)
        {
            score += 2;
        }

        return score;
    }

    private static bool LooksLikeExperienceQuestion(string question)
    {
        return ContainsAny(question,
            "experience",
            "years",
            "year",
            "worked",
            "employment",
            "career",
            "seniority",
            "commercial",
            "doświadczenie",
            "lat");
    }

    private static bool LooksLikeSkillsQuestion(string question)
    {
        return ContainsAny(question,
            "skills",
            "skill",
            "stack",
            "technology",
            "technologies",
            "framework",
            "tools",
            "umiejętności",
            "technologie");
    }

    private static bool LooksLikeEducationQuestion(string question)
    {
        return ContainsAny(question,
            "education",
            "degree",
            "university",
            "college",
            "school",
            "edukacja",
            "studia",
            "uczelnia");
    }

    private static bool ContainsAny(string text, params string[] values)
    {
        return values.Any(value => text.Contains(value, StringComparison.OrdinalIgnoreCase));
    }

    private static string Normalize(string value)
    {
        return (value ?? string.Empty).Trim().ToLowerInvariant();
    }

    private static int CountOccurrences(string source, string value)
    {
        var count = 0;
        var index = 0;

        while ((index = source.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }

    private static List<DocumentChunk> ExpandWithNeighbors(
        IReadOnlyCollection<DocumentChunk> allChunks,
        IReadOnlyCollection<DocumentChunk> bestChunks,
        int targetCount)
    {
        var ordered = allChunks.OrderBy(x => x.ChunkIndex).ToList();
        var byIndex = ordered.ToDictionary(x => x.ChunkIndex);
        var selected = new HashSet<int>(bestChunks.Select(x => x.ChunkIndex));

        foreach (var chunk in bestChunks.OrderBy(x => x.ChunkIndex))
        {
            if (selected.Count >= targetCount)
            {
                break;
            }

            if (byIndex.TryGetValue(chunk.ChunkIndex - 1, out var previous))
            {
                selected.Add(previous.ChunkIndex);
            }

            if (selected.Count >= targetCount)
            {
                break;
            }

            if (byIndex.TryGetValue(chunk.ChunkIndex + 1, out var next))
            {
                selected.Add(next.ChunkIndex);
            }
        }

        return selected
            .Where(byIndex.ContainsKey)
            .Select(index => byIndex[index])
            .OrderBy(x => x.ChunkIndex)
            .Take(targetCount)
            .ToList();
    }
}