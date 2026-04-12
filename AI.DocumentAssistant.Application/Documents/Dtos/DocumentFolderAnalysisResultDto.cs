namespace AI.DocumentAssistant.Application.Documents.Dtos
{
    public sealed class DocumentFolderAnalysisResultDto
    {
        public string Category { get; set; } = "unknown";
        public decimal Confidence { get; set; }
        public string Reason { get; set; } = string.Empty;

        public Guid? SuggestedExistingFolderId { get; set; }
        public List<DocumentFolderCandidateDto> ExistingFolderCandidates { get; set; } = new();

        public DocumentFolderProposalDto? ProposedFolder { get; set; }
    }
}