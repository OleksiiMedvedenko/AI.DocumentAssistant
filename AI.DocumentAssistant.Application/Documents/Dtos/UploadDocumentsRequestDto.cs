using Microsoft.AspNetCore.Http;

namespace AI.DocumentAssistant.Application.Documents.Dtos;

public sealed class UploadDocumentsRequestDto
{
    public List<IFormFile> Files { get; set; } = [];
    public Guid? FolderId { get; set; }
    public bool SmartOrganize { get; set; } = true;
    public bool AllowSystemFolderCreation { get; set; } = true;
}