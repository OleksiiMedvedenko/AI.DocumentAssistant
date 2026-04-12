namespace AI.DocumentAssistant.Application.Documents.Dtos
{
    public sealed class DocumentFolderDecisionResultDto
    {
        public Guid? FolderId { get; set; }
        public bool CreatedNewFolder { get; set; }
        public bool AutoAssigned { get; set; }

        public string Status { get; set; } = string.Empty;
        public decimal? Confidence { get; set; }
        public string Reason { get; set; } = string.Empty;
    }
}