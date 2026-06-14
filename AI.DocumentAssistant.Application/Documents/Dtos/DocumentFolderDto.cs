namespace AI.DocumentAssistant.Application.Documents.Dtos
{
    public sealed class DocumentFolderDto
    {
        public Guid Id { get; set; }
        public Guid? ParentFolderId { get; set; }
        public Guid? OrganizationId { get; set; }
        public string Visibility { get; set; } = default!;
        public bool InheritPermissions { get; set; }

        public string Key { get; set; } = default!;
        public string Name { get; set; } = default!;
        public string NamePl { get; set; } = default!;
        public string NameEn { get; set; } = default!;
        public string NameUa { get; set; } = default!;

        public bool IsSystemGenerated { get; set; }
        public int DocumentCount { get; set; }

        public List<DocumentFolderDto> Children { get; set; } = [];
    }
}
