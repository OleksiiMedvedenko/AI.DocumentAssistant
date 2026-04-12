namespace AI.DocumentAssistant.API.Contracts.Documents
{
    public sealed class UploadDocumentRequest
    {
        public IFormFile File { get; set; } = default!;
        public Guid? FolderId { get; set; }
        public bool SmartOrganize { get; set; } = true;
        public bool AllowSystemFolderCreation { get; set; } = true;
    }
}
