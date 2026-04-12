using System.Text;
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
            .Select(x => new
            {
                x.Id,
                x.OriginalFileName,
                x.ExtractedText
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
                    x => x.Id == dto.ChatSessionId.Value
                         && x.UserId == userId
                         && x.FolderId == folderId,
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

        var bestChunks = await _chunkRetrievalService.GetBestMatchingChunksAsync(
            chunks,
            normalizedMessage,
            priorUserMessages,
            _retrievalOptions.DefaultTake,
            cancellationToken);

        var effectiveChunks = bestChunks.Count > 0
            ? bestChunks
            : chunks.Take(Math.Max(1, _retrievalOptions.DefaultTake)).ToList();

        var documentNames = documents.ToDictionary(x => x.Id, x => x.OriginalFileName);

        var folderContext = BuildFolderContext(
            effectiveChunks,
            documentNames,
            _retrievalOptions.MaxContextCharacters);

        var recentUserQuestions = session.Messages?
            .Where(x => x.Role == ChatRole.User)
            .OrderByDescending(x => x.CreatedAtUtc)
            .Take(4)
            .OrderBy(x => x.CreatedAtUtc)
            .Select(x => x.Content.Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToList()
            ?? new List<string>();

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
            folderContext,
            recentUserQuestions,
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

        foreach (var session in sessions)
        {
            session.Title = Truncate(session.Title, 80);
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
                x => x.Id == chatSessionId
                     && x.UserId == userId
                     && x.FolderId == folderId,
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
            var nextLength = parts.Count == 0
                ? block.Length
                : separator.Length + block.Length;

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
        string folderContext,
        IReadOnlyList<string> recentUserQuestions,
        string currentQuestion)
    {
        var sb = new StringBuilder();

        sb.AppendLine("INSTRUCTIONS:");
        sb.AppendLine("- Answer only from the provided folder evidence and the user's question.");
        sb.AppendLine("- The evidence may come from multiple documents.");
        sb.AppendLine("- Do not invent facts that are not supported by the documents.");
        sb.AppendLine("- If information differs across documents, say that clearly.");
        sb.AppendLine("- When comparing documents, explicitly mention which document supports each conclusion.");
        sb.AppendLine("- If the answer is uncertain, say so clearly.");
        sb.AppendLine();

        if (recentUserQuestions.Count > 0)
        {
            sb.AppendLine("RECENT USER QUESTIONS:");
            foreach (var question in recentUserQuestions)
            {
                sb.AppendLine($"- {question}");
            }

            sb.AppendLine();
        }

        sb.AppendLine("CURRENT QUESTION:");
        sb.AppendLine(currentQuestion);
        sb.AppendLine();
        sb.AppendLine("FOLDER EVIDENCE:");
        sb.AppendLine(folderContext);

        return sb.ToString().Trim();
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