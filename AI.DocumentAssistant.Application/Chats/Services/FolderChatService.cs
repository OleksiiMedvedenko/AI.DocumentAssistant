using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using AI.DocumentAssistant.Application.Abstractions.AI;
using AI.DocumentAssistant.Application.Abstractions.Chats;
using AI.DocumentAssistant.Application.Abstractions.Common;
using AI.DocumentAssistant.Application.Abstractions.Usage;
using AI.DocumentAssistant.Application.Common.Exceptions;
using AI.DocumentAssistant.Application.Documents.Dtos;
using AI.DocumentAssistant.Domain.Entities;
using AI.DocumentAssistant.Domain.Enums;
using AI.DocumentAssistant.Infrastructure.Persistence.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AI.DocumentAssistant.Application.Chats.Services;

public sealed class FolderChatService : IFolderChatService
{
    private static readonly Regex StrongIdentifierRegex = new(
        @"\b[\p{L}\p{N}]{2,}(?:[-_/\.][\p{L}\p{N}]{2,}){1,}\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex TokenRegex = new(
        @"\p{L}[\p{L}\p{N}_-]*|\p{N}{2,}",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly IApplicationDbContext _dbContext;
    private readonly ICurrentUserService _currentUserService;
    private readonly IOpenAiService _openAiService;
    private readonly IChunkRetrievalService _chunkRetrievalService;
    private readonly IUsageQuotaService _usageQuotaService;
    private readonly IUsageTrackingService _usageTrackingService;
    private readonly ChatRetrievalOptions _retrievalOptions;

    public FolderChatService(
        IApplicationDbContext dbContext,
        ICurrentUserService currentUserService,
        IOpenAiService openAiService,
        IChunkRetrievalService chunkRetrievalService,
        IUsageQuotaService usageQuotaService,
        IUsageTrackingService usageTrackingService,
        IOptions<ChatRetrievalOptions> retrievalOptions)
    {
        _dbContext = dbContext;
        _currentUserService = currentUserService;
        _openAiService = openAiService;
        _chunkRetrievalService = chunkRetrievalService;
        _usageQuotaService = usageQuotaService;
        _usageTrackingService = usageTrackingService;
        _retrievalOptions = retrievalOptions.Value;
    }

    public async Task<AskDocumentResultDto> AskFolderAsync(
        Guid folderId,
        AskDocumentDto dto,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(dto.Message))
        {
            throw new BadRequestException("Message is required.");
        }

        var normalizedMessage = dto.Message.Trim();
        var userId = _currentUserService.GetUserId();

        var folder = await _dbContext.DocumentFolders
            .FirstOrDefaultAsync(x => x.Id == folderId && x.UserId == userId, cancellationToken);

        if (folder is null)
        {
            throw new NotFoundException("Folder not found.");
        }

        var folderIds = await GetFolderTreeIdsAsync(folderId, userId, cancellationToken);

        var documents = await _dbContext.Documents
            .Where(x =>
                x.UserId == userId &&
                x.FolderId.HasValue &&
                folderIds.Contains(x.FolderId.Value) &&
                x.Status == DocumentStatus.Ready &&
                !string.IsNullOrWhiteSpace(x.ExtractedText))
            .Select(x => new FolderDocumentProjection
            {
                Id = x.Id,
                OriginalFileName = x.OriginalFileName,
                ExtractedText = x.ExtractedText!
            })
            .ToListAsync(cancellationToken);

        if (documents.Count == 0)
        {
            throw new BadRequestException("Folder does not contain processed documents.");
        }

        var documentIds = documents.Select(x => x.Id).ToList();

        var chunks = await _dbContext.DocumentChunks
            .Where(x => documentIds.Contains(x.DocumentId))
            .OrderBy(x => x.DocumentId)
            .ThenBy(x => x.ChunkIndex)
            .ToListAsync(cancellationToken);

        if (chunks.Count == 0)
        {
            throw new BadRequestException("No indexed chunks found for this folder.");
        }

        ChatSession? session = null;

        if (dto.ChatSessionId.HasValue)
        {
            session = await _dbContext.ChatSessions
                .Include(x => x.Messages)
                .FirstOrDefaultAsync(
                    x => x.Id == dto.ChatSessionId.Value &&
                         x.UserId == userId &&
                         x.FolderId == folderId,
                    cancellationToken);

            if (session is null)
            {
                throw new NotFoundException("Chat session not found.");
            }
        }

        if (session is null)
        {
            session = new ChatSession
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                FolderId = folderId,
                DocumentId = null,
                CreatedAtUtc = DateTime.UtcNow
            };

            await _dbContext.ChatSessions.AddAsync(session, cancellationToken);
        }

        var priorUserMessages = session.Messages?
            .Where(x => x.Role == ChatRole.User)
            .OrderByDescending(x => x.CreatedAtUtc)
            .Take(_retrievalOptions.HistoryMessagesToUse)
            .Select(x => x.Content.Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Reverse()
            .ToList()
            ?? new List<string>();

        var recentUserQuestions = session.Messages?
            .Where(x => x.Role == ChatRole.User)
            .OrderByDescending(x => x.CreatedAtUtc)
            .Take(4)
            .OrderBy(x => x.CreatedAtUtc)
            .Select(x => x.Content.Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToList()
            ?? new List<string>();

        var questionMode = DetectQuestionMode(normalizedMessage);

        var evidencePack = await BuildEvidencePackAsync(
            documents,
            chunks,
            normalizedMessage,
            priorUserMessages,
            cancellationToken);

        await _usageQuotaService.EnsureWithinQuotaAsync(
            userId,
            UsageType.ChatMessage,
            1,
            cancellationToken);

        var userMessage = new ChatMessage
        {
            Id = Guid.NewGuid(),
            ChatSessionId = session.Id,
            Role = ChatRole.User,
            Content = normalizedMessage,
            CreatedAtUtc = DateTime.UtcNow
        };

        await _dbContext.ChatMessages.AddAsync(userMessage, cancellationToken);

        var enrichedContext = BuildPromptContext(
            evidencePack,
            recentUserQuestions,
            normalizedMessage,
            questionMode,
            documents.Count);

        var answer = await _openAiService.AnswerQuestionAsync(
            enrichedContext,
            normalizedMessage,
            dto.Language,
            cancellationToken);

        var assistantMessage = new ChatMessage
        {
            Id = Guid.NewGuid(),
            ChatSessionId = session.Id,
            Role = ChatRole.Assistant,
            Content = answer,
            CreatedAtUtc = DateTime.UtcNow
        };

        await _dbContext.ChatMessages.AddAsync(assistantMessage, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _usageTrackingService.TrackAsync(
            userId,
            UsageType.ChatMessage,
            1,
            cancellationToken,
            model: "gpt-4o-mini",
            referenceId: session.Id.ToString());

        return new AskDocumentResultDto
        {
            ChatSessionId = session.Id,
            Answer = answer
        };
    }

    public async Task<IReadOnlyList<ChatSessionDto>> GetFolderSessionsAsync(
        Guid folderId,
        CancellationToken cancellationToken)
    {
        var userId = _currentUserService.GetUserId();

        var folderExists = await _dbContext.DocumentFolders
            .AnyAsync(x => x.Id == folderId && x.UserId == userId, cancellationToken);

        if (!folderExists)
        {
            throw new NotFoundException("Folder not found.");
        }

        var sessions = await _dbContext.ChatSessions
            .Where(x => x.UserId == userId && x.FolderId == folderId)
            .Select(x => new ChatSessionDto
            {
                Id = x.Id,
                DocumentId = x.DocumentId ?? Guid.Empty,
                CreatedAtUtc = x.CreatedAtUtc,
                LastMessageAtUtc = x.Messages
                    .OrderByDescending(m => m.CreatedAtUtc)
                    .Select(m => (DateTime?)m.CreatedAtUtc)
                    .FirstOrDefault() ?? x.CreatedAtUtc,
                MessageCount = x.Messages.Count(),
                Title = x.Messages
                    .OrderBy(m => m.CreatedAtUtc)
                    .Where(m => m.Role == ChatRole.User)
                    .Select(m => m.Content)
                    .FirstOrDefault() ?? "New chat"
            })
            .OrderByDescending(x => x.LastMessageAtUtc)
            .ThenByDescending(x => x.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        foreach (var item in sessions)
        {
            item.Title = Truncate(item.Title, 80);
        }

        return sessions;
    }

    public async Task<IReadOnlyList<ChatMessageDto>> GetFolderMessagesAsync(
        Guid folderId,
        Guid chatSessionId,
        CancellationToken cancellationToken)
    {
        var userId = _currentUserService.GetUserId();

        var sessionExists = await _dbContext.ChatSessions
            .AnyAsync(
                x => x.Id == chatSessionId &&
                     x.UserId == userId &&
                     x.FolderId == folderId,
                cancellationToken);

        if (!sessionExists)
        {
            throw new NotFoundException("Chat session not found.");
        }

        return await _dbContext.ChatMessages
            .Where(x => x.ChatSessionId == chatSessionId)
            .OrderBy(x => x.CreatedAtUtc)
            .Select(x => new ChatMessageDto
            {
                Id = x.Id,
                Role = x.Role,
                Content = x.Content,
                CreatedAtUtc = x.CreatedAtUtc
            })
            .ToListAsync(cancellationToken);
    }

    private async Task<List<Guid>> GetFolderTreeIdsAsync(
        Guid rootFolderId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var folders = await _dbContext.DocumentFolders
            .Where(x => x.UserId == userId)
            .Select(x => new { x.Id, x.ParentFolderId })
            .ToListAsync(cancellationToken);

        var result = new List<Guid>();
        var queue = new Queue<Guid>();
        queue.Enqueue(rootFolderId);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();

            if (result.Contains(current))
            {
                continue;
            }

            result.Add(current);

            foreach (var child in folders.Where(x => x.ParentFolderId == current))
            {
                queue.Enqueue(child.Id);
            }
        }

        return result;
    }

    private async Task<EvidencePack> BuildEvidencePackAsync(
        IReadOnlyList<FolderDocumentProjection> documents,
        IReadOnlyList<DocumentChunk> allChunks,
        string question,
        IReadOnlyList<string> priorUserMessages,
        CancellationToken cancellationToken)
    {
        var mode = DetectQuestionMode(question);
        var maxCharacters = _retrievalOptions.MaxContextCharacters > 0
            ? _retrievalOptions.MaxContextCharacters
            : 12_000;

        var chunksByDocument = allChunks
            .GroupBy(x => x.DocumentId)
            .ToDictionary(g => g.Key, g => g.OrderBy(x => x.ChunkIndex).ToList());

        var exactIdentifierChunks = FindExactIdentifierChunks(allChunks, documents, question);
        var semanticMatches = await _chunkRetrievalService.GetBestMatchingChunksAsync(
            allChunks,
            question,
            priorUserMessages,
            Math.Max(_retrievalOptions.DefaultTake, 8),
            cancellationToken);

        var selected = new List<DocumentChunk>();

        foreach (var chunk in exactIdentifierChunks)
        {
            AddChunk(selected, chunk);
        }

        if (mode is FolderQuestionMode.ExhaustiveListing
            or FolderQuestionMode.Aggregation
            or FolderQuestionMode.DuplicateDetection)
        {
            var rankedDocs = RankDocumentsForCoverage(documents, allChunks, question);

            foreach (var candidate in rankedDocs)
            {
                if (!chunksByDocument.TryGetValue(candidate.DocumentId, out var docChunks) || docChunks.Count == 0)
                {
                    continue;
                }

                var localMatches = await _chunkRetrievalService.GetBestMatchingChunksAsync(
                    docChunks,
                    question,
                    priorUserMessages,
                    take: 2,
                    cancellationToken);

                if (localMatches.Count == 0)
                {
                    AddChunk(selected, docChunks[0]);
                    continue;
                }

                foreach (var chunk in localMatches)
                {
                    AddChunk(selected, chunk);
                }
            }

            foreach (var chunk in semanticMatches)
            {
                AddChunk(selected, chunk);
            }
        }
        else if (mode == FolderQuestionMode.Comparison)
        {
            foreach (var group in semanticMatches.GroupBy(x => x.DocumentId))
            {
                foreach (var chunk in group.Take(2))
                {
                    AddChunk(selected, chunk);
                }
            }
        }
        else
        {
            foreach (var chunk in semanticMatches)
            {
                AddChunk(selected, chunk);
            }
        }

        if (selected.Count == 0)
        {
            foreach (var chunk in allChunks.Take(Math.Max(1, _retrievalOptions.DefaultTake)))
            {
                AddChunk(selected, chunk);
            }
        }

        var balanced = BalanceAcrossDocuments(
            selected,
            mode switch
            {
                FolderQuestionMode.Lookup => 4,
                FolderQuestionMode.Comparison => 3,
                FolderQuestionMode.DuplicateDetection => 3,
                FolderQuestionMode.ExhaustiveListing => 2,
                FolderQuestionMode.Aggregation => 2,
                _ => 3
            });

        var documentNames = documents.ToDictionary(x => x.Id, x => x.DisplayName);

        var context = BuildFolderContext(
            balanced,
            documentNames,
            maxCharacters);

        var usedDocumentIds = balanced
            .Select(x => x.DocumentId)
            .Distinct()
            .ToHashSet();

        var omittedDocumentNames = documents
            .Where(x => !usedDocumentIds.Contains(x.Id))
            .Select(x => x.DisplayName)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new EvidencePack
        {
            Mode = mode,
            Context = context,
            TotalDocuments = documents.Count,
            UsedDocuments = usedDocumentIds.Count,
            UsedDocumentNames = documents
                .Where(x => usedDocumentIds.Contains(x.Id))
                .Select(x => x.DisplayName)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            OmittedDocumentNames = omittedDocumentNames,
            HasExactIdentifierMatches = exactIdentifierChunks.Count > 0
        };
    }

    private static FolderQuestionMode DetectQuestionMode(string question)
    {
        var normalized = Normalize(question);

        if (ContainsAny(normalized, "duplicate", "duplicates", "duplikat", "duplikaty", "dubluje", "dublikuja"))
        {
            return FolderQuestionMode.DuplicateDetection;
        }

        if (ContainsAny(normalized, "porownaj", "porównaj", "compare", "difference", "differences", "roznice", "różnice"))
        {
            return FolderQuestionMode.Comparison;
        }

        if (ContainsAny(normalized, "ile", "how many", "count", "zlicz", "policz", "sum", "suma"))
        {
            return FolderQuestionMode.Aggregation;
        }

        if (ContainsAny(normalized, "wszystkie", "all ", "every", "lista", "list ", "wymien", "wymień", "podaj wszystkie", "show all"))
        {
            return FolderQuestionMode.ExhaustiveListing;
        }

        if (ExtractStrongIdentifiers(question).Count > 0)
        {
            return FolderQuestionMode.Lookup;
        }

        if (ContainsAny(normalized, "podsumuj", "summary", "summarize", "overview", "o czym", "what is this about"))
        {
            return FolderQuestionMode.Overview;
        }

        return FolderQuestionMode.SpecificQa;
    }

    private static List<DocumentChunk> FindExactIdentifierChunks(
        IReadOnlyList<DocumentChunk> chunks,
        IReadOnlyList<FolderDocumentProjection> documents,
        string question)
    {
        var identifiers = ExtractStrongIdentifiers(question);
        if (identifiers.Count == 0)
        {
            return new List<DocumentChunk>();
        }

        var fileNames = documents.ToDictionary(x => x.Id, x => x.DisplayName);
        var result = new List<DocumentChunk>();

        foreach (var chunk in chunks)
        {
            var text = chunk.Text ?? string.Empty;
            fileNames.TryGetValue(chunk.DocumentId, out var fileName);
            fileName ??= string.Empty;

            foreach (var id in identifiers)
            {
                if (ContainsNormalized(text, id) || ContainsNormalized(fileName, id))
                {
                    result.Add(chunk);
                    break;
                }
            }
        }

        return result
            .OrderBy(x => x.DocumentId)
            .ThenBy(x => x.ChunkIndex)
            .DistinctBy(x => x.Id)
            .ToList();
    }

    private static List<DocumentCoverageCandidate> RankDocumentsForCoverage(
        IReadOnlyList<FolderDocumentProjection> documents,
        IReadOnlyList<DocumentChunk> chunks,
        string question)
    {
        var normalizedQuestion = Normalize(question);
        var identifiers = ExtractStrongIdentifiers(question);
        var chunksByDocument = chunks
            .GroupBy(x => x.DocumentId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var result = new List<DocumentCoverageCandidate>();

        foreach (var doc in documents)
        {
            chunksByDocument.TryGetValue(doc.Id, out var docChunks);
            docChunks ??= new List<DocumentChunk>();

            var score = 0d;
            score += ScoreLexical(doc.DisplayName, normalizedQuestion) * 3d;
            score += ScoreLexical(doc.ExtractedText, normalizedQuestion);

            foreach (var id in identifiers)
            {
                if (ContainsNormalized(doc.DisplayName, id))
                {
                    score += 10d;
                }

                if (ContainsNormalized(doc.ExtractedText, id))
                {
                    score += 8d;
                }
            }

            score += Math.Min(docChunks.Count, 4) * 0.05d;

            result.Add(new DocumentCoverageCandidate(doc.Id, doc.DisplayName, score));
        }

        return result
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.DocumentName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static List<DocumentChunk> BalanceAcrossDocuments(
        IReadOnlyList<DocumentChunk> chunks,
        int perDocumentLimit)
    {
        var result = new List<DocumentChunk>();
        var counters = new Dictionary<Guid, int>();

        foreach (var chunk in chunks
                     .OrderBy(x => x.DocumentId)
                     .ThenBy(x => x.ChunkIndex))
        {
            counters.TryGetValue(chunk.DocumentId, out var current);

            if (current >= perDocumentLimit)
            {
                continue;
            }

            result.Add(chunk);
            counters[chunk.DocumentId] = current + 1;
        }

        return result
            .DistinctBy(x => x.Id)
            .OrderBy(x => x.DocumentId)
            .ThenBy(x => x.ChunkIndex)
            .ToList();
    }

    private static void AddChunk(ICollection<DocumentChunk> target, DocumentChunk chunk)
    {
        if (target.Any(x => x.Id == chunk.Id))
        {
            return;
        }

        target.Add(chunk);
    }

    private static string BuildFolderContext(
        IReadOnlyList<DocumentChunk> chunks,
        IReadOnlyDictionary<Guid, string> documentNames,
        int maxCharacters)
    {
        if (maxCharacters <= 0)
        {
            maxCharacters = 12_000;
        }

        const string separator = "\n\n---\n\n";
        var parts = new List<string>();
        var currentLength = 0;

        foreach (var chunk in chunks.OrderBy(x => x.DocumentId).ThenBy(x => x.ChunkIndex))
        {
            if (string.IsNullOrWhiteSpace(chunk.Text))
            {
                continue;
            }

            var documentName = documentNames.TryGetValue(chunk.DocumentId, out var value)
                ? value
                : "Unknown document";

            var block = $"[document: {documentName} | chunk: {chunk.ChunkIndex}]\n{chunk.Text.Trim()}";
            var nextLength = parts.Count == 0 ? block.Length : separator.Length + block.Length;

            if (currentLength + nextLength > maxCharacters)
            {
                break;
            }

            parts.Add(block);
            currentLength += nextLength;
        }

        return string.Join(separator, parts);
    }

    private static string BuildPromptContext(
        EvidencePack evidence,
        IReadOnlyList<string> recentUserQuestions,
        string currentQuestion,
        FolderQuestionMode mode,
        int totalFolderDocuments)
    {
        var sb = new StringBuilder();

        sb.AppendLine("INSTRUCTIONS:");
        sb.AppendLine("- Answer only from the provided folder evidence and the user's question.");
        sb.AppendLine("- The evidence may come from multiple documents.");
        sb.AppendLine("- Do not invent facts that are not supported by the documents.");
        sb.AppendLine("- Prefer exact textual evidence over semantic guesses.");
        sb.AppendLine("- Do not assume duplicates only because two documents look similar.");
        sb.AppendLine("- If the answer is incomplete, say clearly that it may be partial.");
        sb.AppendLine("- When listing items, avoid merging two separate documents into one unless the evidence explicitly proves they are the same.");
        sb.AppendLine();

        sb.AppendLine("FOLDER ANALYSIS:");
        sb.AppendLine($"- Question mode: {mode}");
        sb.AppendLine($"- Total processed documents in folder scope: {totalFolderDocuments}");
        sb.AppendLine($"- Documents included in evidence: {evidence.UsedDocuments}");
        sb.AppendLine($"- Exact identifier matches present: {(evidence.HasExactIdentifierMatches ? "yes" : "no")}");

        if (mode is FolderQuestionMode.ExhaustiveListing
            or FolderQuestionMode.Aggregation
            or FolderQuestionMode.DuplicateDetection)
        {
            sb.AppendLine("- This question likely expects broad coverage across documents.");
            sb.AppendLine("- Do not claim a complete list unless the evidence clearly covers the full scope.");
        }

        if (recentUserQuestions.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("RECENT USER QUESTIONS:");
            foreach (var question in recentUserQuestions)
            {
                sb.AppendLine($"- {question}");
            }
        }

        if (evidence.UsedDocumentNames.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("DOCUMENTS USED IN EVIDENCE:");
            foreach (var name in evidence.UsedDocumentNames)
            {
                sb.AppendLine($"- {name}");
            }
        }

        if (evidence.OmittedDocumentNames.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("DOCUMENTS NOT INCLUDED IN EVIDENCE:");
            foreach (var name in evidence.OmittedDocumentNames)
            {
                sb.AppendLine($"- {name}");
            }
        }

        sb.AppendLine();
        sb.AppendLine("CURRENT QUESTION:");
        sb.AppendLine(currentQuestion);
        sb.AppendLine();
        sb.AppendLine("FOLDER EVIDENCE:");
        sb.AppendLine(evidence.Context);

        return sb.ToString().Trim();
    }

    private static List<string> ExtractStrongIdentifiers(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return new List<string>();
        }

        return StrongIdentifierRegex.Matches(text)
            .Select(x => x.Value.Trim())
            .Where(x => x.Length >= 8)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static double ScoreLexical(string source, string normalizedQuestion)
    {
        if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(normalizedQuestion))
        {
            return 0d;
        }

        var sourceNormalized = Normalize(source);
        var questionTokens = TokenRegex.Matches(normalizedQuestion)
            .Select(x => x.Value)
            .Where(x => x.Length >= 3)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (questionTokens.Count == 0)
        {
            return 0d;
        }

        var matched = 0d;

        foreach (var token in questionTokens)
        {
            if (sourceNormalized.Contains(token, StringComparison.Ordinal))
            {
                matched += 1d;
            }
        }

        return matched / questionTokens.Count;
    }

    private static bool ContainsNormalized(string source, string value)
    {
        if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return Normalize(source).Contains(Normalize(value), StringComparison.Ordinal);
    }

    private static bool ContainsAny(string normalized, params string[] phrases)
        => phrases.Any(x => normalized.Contains(Normalize(x), StringComparison.Ordinal));

    private static string Normalize(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var lowered = value
            .Trim()
            .ToLowerInvariant()
            .Normalize(NormalizationForm.FormD);

        var sb = new StringBuilder(lowered.Length);

        foreach (var ch in lowered)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (category == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            sb.Append(char.IsLetterOrDigit(ch) ? ch : ' ');
        }

        var normalized = sb.ToString();
        while (normalized.Contains("  ", StringComparison.Ordinal))
        {
            normalized = normalized.Replace("  ", " ", StringComparison.Ordinal);
        }

        return normalized.Trim();
    }

    private static string Truncate(string value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "New chat";
        }

        return value.Length <= maxLength
            ? value
            : value[..maxLength].TrimEnd() + "...";
    }

    private sealed class FolderDocumentProjection
    {
        public Guid Id { get; init; }
        public string? OriginalFileName { get; init; }
        public string ExtractedText { get; init; } = string.Empty;

        public string DisplayName
            => string.IsNullOrWhiteSpace(OriginalFileName) ? Id.ToString() : OriginalFileName!;
    }

    private sealed record EvidencePack
    {
        public FolderQuestionMode Mode { get; init; }
        public string Context { get; init; } = string.Empty;
        public int TotalDocuments { get; init; }
        public int UsedDocuments { get; init; }
        public List<string> UsedDocumentNames { get; init; } = new();
        public List<string> OmittedDocumentNames { get; init; } = new();
        public bool HasExactIdentifierMatches { get; init; }
    }

    private sealed record DocumentCoverageCandidate(Guid DocumentId, string DocumentName, double Score);

    private enum FolderQuestionMode
    {
        SpecificQa = 0,
        Overview = 1,
        Lookup = 2,
        ExhaustiveListing = 3,
        Aggregation = 4,
        Comparison = 5,
        DuplicateDetection = 6
    }
}