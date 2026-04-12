namespace AI.DocumentAssistant.API.Contracts.Documents
{
    public sealed class CreateDocumentFolderRequest
    {
        public Guid? ParentFolderId { get; set; }
        public string Name { get; set; } = default!;
        public string NamePl { get; set; } = default!;
        public string NameEn { get; set; } = default!;
        public string NameUa { get; set; } = default!;
    }
}
