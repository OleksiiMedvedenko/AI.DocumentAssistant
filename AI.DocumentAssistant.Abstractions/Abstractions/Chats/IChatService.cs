

using AI.DocumentAssistant.Application.Documents.Dtos;

namespace AI.DocumentAssistant.Abstraction.Abstractions.Chats
{
    public interface IChatService
    {
        Task<AskDocumentResultDto> AskAsync(Guid documentId, AskDocumentDto dto, CancellationToken cancellationToken);
        Task<IReadOnlyCollection<ChatMessageDto>> GetMessagesAsync(Guid documentId, Guid chatSessionId, CancellationToken cancellationToken);
    }
}
