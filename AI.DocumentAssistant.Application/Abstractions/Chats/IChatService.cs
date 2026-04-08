using AI.DocumentAssistant.Application.Documents.Dtos;

namespace AI.DocumentAssistant.Application.Abstractions.Chats;

public interface IChatService
{
    Task<AskDocumentResultDto> AskAsync(Guid documentId, AskDocumentDto dto, CancellationToken cancellationToken);
    Task<IReadOnlyList<ChatMessageDto>> GetMessagesAsync(
        Guid documentId,
        Guid chatSessionId,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<ChatSessionDto>> GetSessionsAsync(
        Guid documentId,
        CancellationToken cancellationToken);
}