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
using System.Text;

namespace AI.DocumentAssistant.Application.Chats.Services;

public sealed class ChatService : IChatService
{
    private readonly IApplicationDbContext _dbContext;
    private readonly ICurrentUserService _currentUserService;
    private readonly IOpenAiService _openAiService;
    private readonly IChunkRetrievalService _chunkRetrievalService;
    private readonly IUsageQuotaService _usageQuotaService;
    private readonly IUsageTrackingService _usageTrackingService;
    private readonly ChatRetrievalOptions _retrievalOptions;

    public ChatService(
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

    public async Task<AskDocumentResultDto> AskAsync(
        Guid documentId,
        AskDocumentDto dto,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(dto.Message))
        {
            throw new BadRequestException("Message is required.");
        }

        var normalizedMessage = dto.Message.Trim();
        var userId = _currentUserService.GetUserId();

        var document = await _dbContext.Documents
            .Include(x => x.Chunks)
            .FirstOrDefaultAsync(x => x.Id == documentId && x.UserId == userId, cancellationToken);

        if (document is null)
        {
            throw new NotFoundException("Document not found.");
        }

        if (string.IsNullOrWhiteSpace(document.ExtractedText))
        {
            throw new BadRequestException("Document has not been processed yet.");
        }

        ChatSession? session = null;

        if (dto.ChatSessionId.HasValue)
        {
            session = await _dbContext.ChatSessions
                .Include(x => x.Messages)
                .FirstOrDefaultAsync(
                    x => x.Id == dto.ChatSessionId.Value &&
                         x.DocumentId == documentId &&
                         x.UserId == userId,
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
                DocumentId = documentId,
                UserId = userId,
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

        var orderedChunks = document.Chunks
            .OrderBy(x => x.ChunkIndex)
            .ToList();

        var bestChunks = await _chunkRetrievalService.GetBestMatchingChunksAsync(
            orderedChunks,
            normalizedMessage,
            priorUserMessages,
            _retrievalOptions.DefaultTake,
            cancellationToken);

        var context = BuildContext(
            bestChunks,
            document.ExtractedText!,
            normalizedMessage,
            _retrievalOptions.MaxContextCharacters);

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

        var recentConversation = session.Messages?
            .OrderByDescending(x => x.CreatedAtUtc)
            .Take(6)
            .OrderBy(x => x.CreatedAtUtc)
            .Select(x => $"{x.Role}: {x.Content}")
            .ToList()
            ?? new List<string>();

        await _dbContext.ChatMessages.AddAsync(userMessage, cancellationToken);

        var enrichedContext = BuildPromptContext(
            context,
            recentConversation,
            normalizedMessage);

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

    public async Task<IReadOnlyList<ChatMessageDto>> GetMessagesAsync(
        Guid documentId,
        Guid chatSessionId,
        CancellationToken cancellationToken)
    {
        var userId = _currentUserService.GetUserId();

        var sessionExists = await _dbContext.ChatSessions
            .AnyAsync(
                x => x.Id == chatSessionId &&
                     x.DocumentId == documentId &&
                     x.UserId == userId,
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

    public async Task<IReadOnlyList<ChatSessionDto>> GetSessionsAsync(
        Guid documentId,
        CancellationToken cancellationToken)
    {
        var userId = _currentUserService.GetUserId();

        var documentExists = await _dbContext.Documents
            .AnyAsync(x => x.Id == documentId && x.UserId == userId, cancellationToken);

        if (!documentExists)
        {
            throw new NotFoundException("Document not found.");
        }

        var sessions = await _dbContext.ChatSessions
            .Where(x => x.DocumentId == documentId && x.UserId == userId)
            .Select(x => new ChatSessionDto
            {
                Id = x.Id,
                DocumentId = x.DocumentId,
                CreatedAtUtc = x.CreatedAtUtc,
                LastMessageAtUtc = x.Messages
                    .OrderByDescending(m => m.CreatedAtUtc)
                    .Select(m => m.CreatedAtUtc)
                    .FirstOrDefault(),
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

        foreach (var session in sessions)
        {
            if (session.LastMessageAtUtc == default)
            {
                session.LastMessageAtUtc = session.CreatedAtUtc;
            }

            session.Title = Truncate(session.Title, 80);
        }

        return sessions;
    }

    private static string BuildContext(
    IReadOnlyList<DocumentChunk> chunks,
    string fallbackText,
    string question,
    int maxCharacters)
    {
        if (maxCharacters <= 0)
        {
            maxCharacters = 12_000;
        }

        if (IsBroadQuestion(question))
        {
            return TrimToBoundary(fallbackText, maxCharacters);
        }

        var selectedChunkTexts = chunks
            .OrderBy(x => x.ChunkIndex)
            .Select(x => x.Text?.Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (selectedChunkTexts.Count == 0)
        {
            return TrimToBoundary(fallbackText, maxCharacters);
        }

        const string separator = "\n\n---\n\n";
        var parts = new List<string>();
        var currentLength = 0;

        foreach (var chunkText in selectedChunkTexts)
        {
            var text = chunkText!;
            var additionalLength = parts.Count == 0 ? text.Length : separator.Length + text.Length;

            if (currentLength + additionalLength > maxCharacters)
            {
                break;
            }

            parts.Add(text);
            currentLength += additionalLength;
        }

        var context = string.Join(separator, parts);

        if (string.IsNullOrWhiteSpace(context))
        {
            return TrimToBoundary(fallbackText, maxCharacters);
        }

        return context;
    }

    private static bool IsBroadQuestion(string question)
    {
        if (string.IsNullOrWhiteSpace(question))
        {
            return false;
        }

        var q = question.Trim().ToLowerInvariant();

        string[] broadPatterns =
        [
            "summarize",
            "summary",
            "what is this document about",
            "what is this about",
            "main points",
            "key points",
            "overall",
            "in general",
            "general overview",
            "o czym jest",
            "podsumuj",
            "podsumowanie",
            "najważniejsze",
            "ogólnie",
            "w skrócie",
            "про що цей документ",
            "підсумуй",
            "загалом"
        ];

        return broadPatterns.Any(q.Contains);
    }

    private static string TrimToBoundary(string value, int maxCharacters)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var trimmed = value.Trim();

        if (trimmed.Length <= maxCharacters)
        {
            return trimmed;
        }

        var candidate = trimmed[..maxCharacters];
        var lastBoundary = Math.Max(
            candidate.LastIndexOf('\n'),
            Math.Max(candidate.LastIndexOf('.'), candidate.LastIndexOf(' ')));

        if (lastBoundary > maxCharacters / 2)
        {
            candidate = candidate[..lastBoundary];
        }

        return candidate.TrimEnd();
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

    private static string BuildPromptContext(
    string documentContext,
    IReadOnlyList<string> recentConversation,
    string currentQuestion)
    {
        var sb = new StringBuilder();

        if (recentConversation.Count > 0)
        {
            sb.AppendLine("RECENT CONVERSATION:");
            foreach (var message in recentConversation)
            {
                sb.AppendLine(message);
            }

            sb.AppendLine();
        }

        sb.AppendLine("CURRENT QUESTION:");
        sb.AppendLine(currentQuestion);
        sb.AppendLine();
        sb.AppendLine("DOCUMENT EVIDENCE:");
        sb.AppendLine(documentContext);

        return sb.ToString().Trim();
    }
}