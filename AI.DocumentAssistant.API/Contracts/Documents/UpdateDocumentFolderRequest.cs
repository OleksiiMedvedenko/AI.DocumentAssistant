namespace AI.DocumentAssistant.API.Contracts.Documents
{
    public sealed class UpdateDocumentFolderRequest
    {
        public string Name { get; set; } = default!;
        public string NamePl { get; set; } = default!;
        public string NameEn { get; set; } = default!;
        public string NameUa { get; set; } = default!;
    }
}
