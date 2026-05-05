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
        public FolderEmbeddingProfile? EmbeddingProfile { get; set; }

        public ICollection<DocumentFolder> Children { get; set; } = new List<DocumentFolder>();
        public ICollection<Document> Documents { get; set; } = new List<Document>();
        public ICollection<ChatSession> ChatSessions { get; set; } = new List<ChatSession>();
        public ICollection<DocumentFolderSuggestion> Suggestions { get; set; } = new List<DocumentFolderSuggestion>();
        public ICollection<UserFolderRule> UserRules { get; set; } = new List<UserFolderRule>();
    }
}