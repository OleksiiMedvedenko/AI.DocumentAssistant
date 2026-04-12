using AI.DocumentAssistant.Application.Abstractions.Chats;
using AI.DocumentAssistant.Application.Documents.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AI.DocumentAssistant.API.Controllers;

[ApiController]
[Authorize]
[Route("api/document-folders/{folderId:guid}/chat")]
public sealed class FolderChatsController : ControllerBase
{
    private readonly IFolderChatService _folderChatService;

    public FolderChatsController(IFolderChatService folderChatService)
    {
        _folderChatService = folderChatService;
    }

    [HttpPost]
    public async Task<ActionResult<AskDocumentResultDto>> Ask(
        Guid folderId,
        [FromBody] AskDocumentDto request,
        CancellationToken cancellationToken)
    {
        var result = await _folderChatService.AskFolderAsync(folderId, request, cancellationToken);
        return Ok(result);
    }

    [HttpGet("sessions")]
    public async Task<ActionResult<IReadOnlyList<ChatSessionDto>>> GetSessions(
        Guid folderId,
        CancellationToken cancellationToken)
    {
        var result = await _folderChatService.GetFolderSessionsAsync(folderId, cancellationToken);
        return Ok(result);
    }

    [HttpGet("sessions/{chatSessionId:guid}/messages")]
    public async Task<ActionResult<IReadOnlyList<ChatMessageDto>>> GetMessages(
        Guid folderId,
        Guid chatSessionId,
        CancellationToken cancellationToken)
    {
        var result = await _folderChatService.GetFolderMessagesAsync(folderId, chatSessionId, cancellationToken);
        return Ok(result);
    }
}