using AI.DocumentAssistant.API.Contracts.Documents;
using AI.DocumentAssistant.Application.Abstractions.Documents;
using AI.DocumentAssistant.Application.Documents.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AI.DocumentAssistant.API.Controllers;

[ApiController]
[Authorize]
[Route("api")]
public sealed class AiActionRunsController : ControllerBase
{
    private readonly IAiActionRunService _service;

    public AiActionRunsController(IAiActionRunService service)
    {
        _service = service;
    }

    [HttpPost("documents/{documentId:guid}/ai-action-templates/{templateId:guid}/run")]
    public async Task<ActionResult<AiActionRunDto>> RunTemplate(
        Guid documentId,
        Guid templateId,
        [FromBody] RunAiActionTemplateRequest? request,
        CancellationToken cancellationToken)
    {
        var result = await _service.RunTemplateAsync(
            documentId,
            templateId,
            new RunAiActionTemplateRequestDto
            {
                Language = request?.Language,
                OutputFormat = request?.OutputFormat,
                SaveResult = request?.SaveResult
            },
            cancellationToken);

        return Ok(result);
    }

    [HttpGet("documents/{documentId:guid}/ai-action-runs")]
    public async Task<ActionResult<List<AiActionRunDto>>> GetDocumentRuns(Guid documentId, CancellationToken cancellationToken)
    {
        return Ok(await _service.GetDocumentRunsAsync(documentId, cancellationToken));
    }

    [HttpGet("ai-action-runs/{runId:guid}")]
    public async Task<ActionResult<AiActionRunDto>> GetById(Guid runId, CancellationToken cancellationToken)
    {
        return Ok(await _service.GetByIdAsync(runId, cancellationToken));
    }

    [HttpGet("ai-action-runs/{runId:guid}/download")]
    public async Task<IActionResult> Download(Guid runId, CancellationToken cancellationToken)
    {
        var result = await _service.OpenDownloadAsync(runId, cancellationToken);
        return File(result.Stream, result.ContentType, result.FileName, enableRangeProcessing: true);
    }
}
