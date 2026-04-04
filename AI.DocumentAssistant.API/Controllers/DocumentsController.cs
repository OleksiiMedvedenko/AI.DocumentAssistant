using AI.DocumentAssistant.API.Contracts.Documents;
using AI.DocumentAssistant.Application.Abstractions.Documents;
using AI.DocumentAssistant.Application.Documents.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AI.DocumentAssistant.API.Controllers;

[ApiController]
[Authorize]
[Route("api/documents")]
public sealed class DocumentsController : ControllerBase
{
    private readonly IDocumentService _documentService;

    public DocumentsController(IDocumentService documentService)
    {
        _documentService = documentService;
    }

    [HttpPost("upload")]
    public async Task<IActionResult> Upload(IFormFile file, CancellationToken cancellationToken)
    {
        var result = await _documentService.UploadAsync(file, cancellationToken);
        return Ok(result);
    }

    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken cancellationToken)
    {
        return Ok(await _documentService.GetAllAsync(cancellationToken));
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
    {
        return Ok(await _documentService.GetByIdAsync(id, cancellationToken));
    }

    [HttpGet("{documentId:guid}/status")]
    public async Task<IActionResult> GetStatus(Guid documentId, CancellationToken cancellationToken)
    {
        var result = await _documentService.GetStatusAsync(documentId, cancellationToken);
        return Ok(result);
    }

    [HttpGet("{documentId:guid}/extractions")]
    public async Task<IActionResult> GetExtractions(Guid documentId, CancellationToken cancellationToken)
    {
        var result = await _documentService.GetExtractionsAsync(documentId, cancellationToken);
        return Ok(result);
    }

    [HttpGet("{documentId:guid}/extractions/{extractionId:guid}")]
    public async Task<IActionResult> GetExtractionById(
        Guid documentId,
        Guid extractionId,
        CancellationToken cancellationToken)
    {
        var result = await _documentService.GetExtractionByIdAsync(documentId, extractionId, cancellationToken);
        return Ok(result);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await _documentService.DeleteAsync(id, cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:guid}/summarize")]
    public async Task<IActionResult> Summarize(Guid id, CancellationToken cancellationToken)
    {
        return Ok(await _documentService.SummarizeAsync(id, cancellationToken));
    }

    [HttpPost("{id:guid}/extract")]
    public async Task<IActionResult> Extract(
        Guid id,
        [FromBody] ExtractDocumentRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _documentService.ExtractAsync(
            id,
            new ExtractDocumentRequestDto
            {
                ExtractionType = request.ExtractionType,
                Fields = request.Fields
            },
            cancellationToken);

        return Ok(result);
    }
}