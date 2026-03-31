using AI.DocumentAssistant.Application.Abstractions.AI;
using AI.DocumentAssistant.Application.Abstractions.Chats;
using AI.DocumentAssistant.Application.Abstractions.Common;
using AI.DocumentAssistant.Application.Common.Exceptions;
using AI.DocumentAssistant.Application.Documents.Dtos;
using AI.DocumentAssistant.Domain.Entities;
using AI.DocumentAssistant.Domain.Enums;
using AI.DocumentAssistant.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AI.DocumentAssistant.Application.Chats.Services
{
    public sealed class ChatService : IChatService
    {
        private readonly AppDbContext _dbContext;
        private readonly ICurrentUserService _currentUserService;
        private readonly IOpenAiService _openAiService;

        public ChatService(
            AppDbContext dbContext,
            ICurrentUserService currentUserService,
            IOpenAiService openAiService)
        {
            _dbContext = dbContext;
            _currentUserService = currentUserService;
            _openAiService = openAiService;
        }

        public async Task<AskDocumentResultDto> AskAsync(Guid documentId, AskDocumentDto dto, CancellationToken cancellationToken)
        {
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
                    .FirstOrDefaultAsync(x => x.Id == dto.ChatSessionId.Value && x.DocumentId == documentId && x.UserId == userId, cancellationToken);
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

            var userMessage = new ChatMessage
            {
                Id = Guid.NewGuid(),
                ChatSessionId = session.Id,
                Role = ChatRole.User,
                Content = dto.Message,
                CreatedAtUtc = DateTime.UtcNow
            };

            _dbContext.ChatMessages.Add(userMessage);

            var context = string.Join("\n\n", document.Chunks.OrderBy(x => x.ChunkIndex).Take(8).Select(x => x.Text));
            if (string.IsNullOrWhiteSpace(context))
            {
                context = document.ExtractedText!;
            }

            var answer = await _openAiService.AnswerQuestionAsync(context, dto.Message, cancellationToken);

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

        public async Task<IReadOnlyCollection<ChatMessageDto>> GetMessagesAsync(Guid documentId, Guid chatSessionId, CancellationToken cancellationToken)
        {
            var userId = _currentUserService.GetUserId();

            var sessionExists = await _dbContext.ChatSessions
                .AnyAsync(x => x.Id == chatSessionId && x.DocumentId == documentId && x.UserId == userId, cancellationToken);

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
    }
}
