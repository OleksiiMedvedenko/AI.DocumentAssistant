using AI.DocumentAssistant.API.Contracts.Documents;
using AI.DocumentAssistant.Application.Abstractions.Chats;
using AI.DocumentAssistant.Application.Documents.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AI.DocumentAssistant.API.Controllers;

[ApiController]
[Authorize]
[Route("api/documents/{documentId:guid}/chat")]
public sealed class ChatsController : ControllerBase
{
    private readonly IChatService _chatService;

    public ChatsController(IChatService chatService)
    {
        _chatService = chatService;
    }

    [HttpPost]
    public async Task<IActionResult> Ask(Guid documentId, AskDocumentRequest request, CancellationToken cancellationToken)
    {
        var result = await _chatService.AskAsync(documentId, new AskDocumentDto
        {
            ChatSessionId = request.ChatSessionId,
            Message = request.Message
        }, cancellationToken);

        return Ok(result);
    }

    [HttpGet("messages")]
    public async Task<IActionResult> GetMessages(Guid documentId, [FromQuery] Guid chatSessionId, CancellationToken cancellationToken)
    {
        return Ok(await _chatService.GetMessagesAsync(documentId, chatSessionId, cancellationToken));
    }
}