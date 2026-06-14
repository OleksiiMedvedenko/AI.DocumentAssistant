using AI.DocumentAssistant.API.Contracts.Documents;
using AI.DocumentAssistant.Application.Abstractions.Documents;
using AI.DocumentAssistant.Application.Documents.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AI.DocumentAssistant.API.Controllers
{
    [ApiController]
    [Authorize]
    [Route("api/document-folders")]
    public sealed class DocumentFoldersController : ControllerBase
    {
        private readonly IDocumentFolderService _documentFolderService;

        public DocumentFoldersController(IDocumentFolderService documentFolderService)
        {
            _documentFolderService = documentFolderService;
        }

        [HttpGet("tree")]
        public async Task<IActionResult> GetTree([FromQuery] Guid? organizationId, CancellationToken cancellationToken)
        {
            return Ok(await _documentFolderService.GetTreeAsync(organizationId, cancellationToken));
        }

        [HttpGet("duplicate-suggestions")]
        public async Task<IActionResult> GetDuplicateSuggestions(CancellationToken cancellationToken)
        {
            return Ok(await _documentFolderService.GetDuplicateSuggestionsAsync(cancellationToken));
        }

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] CreateDocumentFolderRequest request, CancellationToken cancellationToken)
        {
            var result = await _documentFolderService.CreateAsync(
                new CreateDocumentFolderRequestDto
                {
                    ParentFolderId = request.ParentFolderId,
                    OrganizationId = request.OrganizationId,
                    Visibility = request.Visibility,
                    Name = request.Name,
                    NamePl = request.NamePl,
                    NameEn = request.NameEn,
                    NameUa = request.NameUa
                },
                cancellationToken);

            return Ok(result);
        }

        [HttpPut("{folderId:guid}")]
        public async Task<IActionResult> Update(Guid folderId, [FromBody] UpdateDocumentFolderRequest request, CancellationToken cancellationToken)
        {
            var result = await _documentFolderService.UpdateAsync(
                folderId,
                new UpdateDocumentFolderRequestDto
                {
                    Name = request.Name,
                    NamePl = request.NamePl,
                    NameEn = request.NameEn,
                    NameUa = request.NameUa
                },
                cancellationToken);

            return Ok(result);
        }

        [HttpPost("{sourceFolderId:guid}/merge")]
        public async Task<IActionResult> Merge(Guid sourceFolderId, [FromBody] MergeDocumentFoldersRequest request, CancellationToken cancellationToken)
        {
            var result = await _documentFolderService.MergeAsync(
                sourceFolderId,
                new MergeDocumentFoldersRequestDto
                {
                    TargetFolderId = request.TargetFolderId,
                    DeleteSourceFolder = request.DeleteSourceFolder
                },
                cancellationToken);

            return Ok(result);
        }

        [HttpDelete("{folderId:guid}")]
        public async Task<IActionResult> Delete(Guid folderId, CancellationToken cancellationToken)
        {
            await _documentFolderService.DeleteAsync(folderId, cancellationToken);
            return NoContent();
        }
    }
}
