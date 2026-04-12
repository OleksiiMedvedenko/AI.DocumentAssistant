using Microsoft.AspNetCore.Http;

namespace AI.DocumentAssistant.Application.Documents.Dtos
{
    public sealed class UploadDocumentRequestDto
    {
        public IFormFile File { get; set; } = default!;
        public Guid? FolderId { get; set; }

        public bool SmartOrganize { get; set; } = true;

        public bool AllowSystemFolderCreation { get; set; } = true;
    }
}