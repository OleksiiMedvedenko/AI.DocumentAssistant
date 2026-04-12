namespace AI.DocumentAssistant.API.Contracts.Documents
{
    public sealed class UploadDocumentsRequest
    {
        public List<IFormFile> Files { get; set; } = [];
        public Guid? FolderId { get; set; }
        public bool SmartOrganize { get; set; } = true;
        public bool AllowSystemFolderCreation { get; set; } = true;
    }
}