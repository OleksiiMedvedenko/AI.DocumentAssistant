using AI.DocumentAssistant.Application.Documents.Dtos;

namespace AI.DocumentAssistant.Application.Abstractions.Chats;

public interface IFolderChatService
{
    Task<AskDocumentResultDto> AskFolderAsync(
        Guid folderId,
        AskDocumentDto dto,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<ChatSessionDto>> GetFolderSessionsAsync(
        Guid folderId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<ChatMessageDto>> GetFolderMessagesAsync(
        Guid folderId,
        Guid chatSessionId,
        CancellationToken cancellationToken);
}