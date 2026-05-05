using AI.DocumentAssistant.API.Contracts.Documents;
using AI.DocumentAssistant.Application.Abstractions.Documents;
using AI.DocumentAssistant.Application.Documents.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AI.DocumentAssistant.API.Controllers;

[ApiController]
[Authorize]
[Route("api/ai-action-templates")]
public sealed class AiActionTemplatesController : ControllerBase
{
    private readonly IAiActionTemplateService _service;

    public AiActionTemplatesController(IAiActionTemplateService service)
    {
        _service = service;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] string? documentType, CancellationToken cancellationToken)
    {
        return Ok(await _service.GetAllAsync(documentType, cancellationToken));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateAiActionTemplateRequest request, CancellationToken cancellationToken)
    {
        var result = await _service.CreateAsync(new CreateAiActionTemplateRequestDto
        {
            Name = request.Name,
            DocumentType = request.DocumentType,
            Prompt = request.Prompt,
            OutputFormat = request.OutputFormat
        }, cancellationToken);

        return Ok(result);
    }

    [HttpPut("{templateId:guid}")]
    public async Task<IActionResult> Update(Guid templateId, [FromBody] CreateAiActionTemplateRequest request, CancellationToken cancellationToken)
    {
        var result = await _service.UpdateAsync(templateId, new CreateAiActionTemplateRequestDto
        {
            Name = request.Name,
            DocumentType = request.DocumentType,
            Prompt = request.Prompt,
            OutputFormat = request.OutputFormat
        }, cancellationToken);

        return Ok(result);
    }

    [HttpDelete("{templateId:guid}")]
    public async Task<IActionResult> Delete(Guid templateId, CancellationToken cancellationToken)
    {
        await _service.DeleteAsync(templateId, cancellationToken);
        return NoContent();
    }
}
