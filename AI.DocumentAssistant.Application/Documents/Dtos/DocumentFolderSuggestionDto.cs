namespace AI.DocumentAssistant.Application.Documents.Dtos
{
    public sealed class DocumentFolderSuggestionDto
    {
        public Guid? ExistingFolderId { get; set; }
        public string ProposedKey { get; set; } = default!;
        public string ProposedName { get; set; } = default!;
        public string ProposedNamePl { get; set; } = default!;
        public string ProposedNameEn { get; set; } = default!;
        public string ProposedNameUa { get; set; } = default!;
        public decimal Confidence { get; set; }
        public string Reason { get; set; } = default!;
    }
}
