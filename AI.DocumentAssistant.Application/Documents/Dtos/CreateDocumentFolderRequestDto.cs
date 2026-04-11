namespace AI.DocumentAssistant.Application.Documents.Dtos
{
    public sealed class CreateDocumentFolderRequestDto
    {
        public Guid? ParentFolderId { get; set; }
        public string Name { get; set; } = default!;
        public string NamePl { get; set; } = default!;
        public string NameEn { get; set; } = default!;
        public string NameUa { get; set; } = default!;
    }
}
