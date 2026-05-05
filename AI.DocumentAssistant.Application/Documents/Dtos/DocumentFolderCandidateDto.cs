namespace AI.DocumentAssistant.Application.Documents.Dtos
{
    public sealed class DocumentFolderCandidateDto
    {
        public Guid FolderId { get; set; }
        public string FolderKey { get; set; } = string.Empty;
        public string FolderName { get; set; } = string.Empty;
        public decimal Score { get; set; }
        public decimal RuleScore { get; set; }
        public decimal SemanticScore { get; set; }
        public decimal UserHistoryScore { get; set; }
        public decimal FinalScore { get; set; }
        public string Reason { get; set; } = string.Empty;
    }
}