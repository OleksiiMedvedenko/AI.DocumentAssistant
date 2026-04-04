using AI.DocumentAssistant.Application.Abstractions.AI;
using AI.DocumentAssistant.Application.Abstractions.Chats;
using AI.DocumentAssistant.Application.Abstractions.Common;
using AI.DocumentAssistant.Application.Common.Exceptions;
using AI.DocumentAssistant.Application.Documents.Dtos;
using AI.DocumentAssistant.Domain.Entities;
using AI.DocumentAssistant.Domain.Enums;
using AI.DocumentAssistant.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AI.DocumentAssistant.Application.Chats.Services;

public sealed class ChatService : IChatService
{
    private readonly AppDbContext _dbContext;
    private readonly ICurrentUserService _currentUserService;
    private readonly IOpenAiService _openAiService;
    private readonly IChunkRetrievalService _chunkRetrievalService;
    private readonly ChatRetrievalOptions _retrievalOptions;

    public ChatService(
        AppDbContext dbContext,
        ICurrentUserService currentUserService,
        IOpenAiService openAiService,
        IChunkRetrievalService chunkRetrievalService,
        IOptions<ChatRetrievalOptions> retrievalOptions)
    {
        _dbContext = dbContext;
        _currentUserService = currentUserService;
        _openAiService = openAiService;
        _chunkRetrievalService = chunkRetrievalService;
        _retrievalOptions = retrievalOptions.Value;
    }

    public async Task<AskDocumentResultDto> AskAsync(Guid documentId, AskDocumentDto dto, CancellationToken cancellationToken)
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
                    x => x.Id == dto.ChatSessionId.Value
                         && x.DocumentId == documentId
                         && x.UserId == userId,
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

            _dbContext.ChatSessions.Add(session);
        }

        var priorUserMessages = session.Messages?
            .Where(x => x.Role == ChatRole.User)
            .OrderByDescending(x => x.CreatedAtUtc)
            .Take(_retrievalOptions.HistoryMessagesToUse)
            .Select(x => x.Content.Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Reverse()
            .ToList() ?? new List<string>();

        var orderedChunks = document.Chunks
            .OrderBy(x => x.ChunkIndex)
            .ToList();

        var bestChunks = _chunkRetrievalService.GetBestMatchingChunks(
            orderedChunks,
            normalizedMessage,
            priorUserMessages,
            take: _retrievalOptions.DefaultTake);

        var context = bestChunks.Count < 3
            ? document.ExtractedText!
            : BuildContext(bestChunks, document.ExtractedText!, _retrievalOptions.MaxContextCharacters);

        var userMessage = new ChatMessage
        {
            Id = Guid.NewGuid(),
            ChatSessionId = session.Id,
            Role = ChatRole.User,
            Content = normalizedMessage,
            CreatedAtUtc = DateTime.UtcNow
        };

        _dbContext.ChatMessages.Add(userMessage);

        var answer = await _openAiService.AnswerQuestionAsync(context, normalizedMessage, cancellationToken);

        var assistantMessage = new ChatMessage
        {
            Id = Guid.NewGuid(),
            ChatSessionId = session.Id,
            Role = ChatRole.Assistant,
            Content = answer,
            CreatedAtUtc = DateTime.UtcNow
        };

        _dbContext.ChatMessages.Add(assistantMessage);

        await _dbContext.SaveChangesAsync(cancellationToken);

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
                x => x.Id == chatSessionId
                     && x.DocumentId == documentId
                     && x.UserId == userId,
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

    public async Task<IReadOnlyList<ChatSessionDto>> GetSessionsAsync(Guid documentId, CancellationToken cancellationToken)
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
        int maxCharacters)
    {
        if (maxCharacters <= 0)
        {
            maxCharacters = 12000;
        }

        string context;

        if (chunks.Count == 0)
        {
            context = fallbackText;
        }
        else
        {
            var selectedChunkTexts = chunks
                .Select(x => x.Text?.Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.Ordinal)
                .ToList();

            context = string.Join("\n\n---\n\n", selectedChunkTexts);

            if (string.IsNullOrWhiteSpace(context))
            {
                context = fallbackText;
            }
        }

        context = context.Trim();

        if (context.Length <= maxCharacters)
        {
            return context;
        }

        return context[..maxCharacters].TrimEnd();
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
}