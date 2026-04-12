namespace AI.DocumentAssistant.Application.Documents.Dtos
{
    public sealed class DocumentFolderProposalDto
    {
        public string Key { get; set; } = default!;
        public string Name { get; set; } = default!;
        public string NamePl { get; set; } = default!;
        public string NameEn { get; set; } = default!;
        public string NameUa { get; set; } = default!;
        public Guid? ParentFolderId { get; set; }
    }
}