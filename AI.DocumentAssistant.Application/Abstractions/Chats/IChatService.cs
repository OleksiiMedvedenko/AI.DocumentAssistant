using AI.DocumentAssistant.Application.Documents.Dtos;

namespace AI.DocumentAssistant.Application.Abstractions.Chats;

public interface IChatService
{
    Task<AskDocumentResultDto> AskAsync(Guid documentId, AskDocumentDto dto, CancellationToken cancellationToken);
    Task<List<ChatMessageDto>> GetMessagesAsync(
        Guid documentId,
        Guid chatSessionId,
        CancellationToken cancellationToken);
    Task<List<ChatSessionDto>> GetSessionsAsync(Guid documentId, CancellationToken cancellationToken);
}