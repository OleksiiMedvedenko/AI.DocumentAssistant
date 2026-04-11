namespace AI.DocumentAssistant.Domain.Entities
{
    public sealed class DocumentFolder
    {
        public Guid Id { get; set; }
        public Guid UserId { get; set; }
        public Guid? ParentFolderId { get; set; }

        public string Key { get; set; } = default!;
        public string Name { get; set; } = default!;
        public string NamePl { get; set; } = default!;
        public string NameEn { get; set; } = default!;
        public string NameUa { get; set; } = default!;

        public bool IsSystemGenerated { get; set; }
        public DateTime CreatedAtUtc { get; set; }

        public User User { get; set; } = default!;
        public DocumentFolder? ParentFolder { get; set; }
        public ICollection<DocumentFolder> Children { get; set; } = new List<DocumentFolder>();
        public ICollection<Document> Documents { get; set; } = new List<Document>();
    }
}
