namespace AI.DocumentAssistant.Application.Documents.Dtos
{
    public sealed class DocumentFolderAnalysisResultDto
    {
        public string Category { get; set; } = "unknown";
        public string Topic { get; set; } = "general";
        public string Decision { get; set; } = "needs_review";
        public decimal Confidence { get; set; }
        // Machine-readable code only. The frontend translates this code to PL/EN/UA.
        public string Reason { get; set; } = "smart_folder.needs_review";
        public string ReasonCode { get; set; } = "smart_folder.needs_review";

        public Guid? SuggestedExistingFolderId { get; set; }
        public List<DocumentFolderCandidateDto> ExistingFolderCandidates { get; set; } = new();

        // Backward-compatible single folder proposal used by older UI/suggestion flows.
        public DocumentFolderProposalDto? ProposedFolder { get; set; }

        // New internal smart-folder model: AI/local logic can propose a full folder path,
        // e.g. Rachunki/PIT, Rachunki/Faktury, CV/IT, CV/HR, Dokumentacja/API.
        // This is application DTO only, so it does not require any DB migration.
        public List<DocumentFolderProposalDto> ProposedPath { get; set; } = new();
    }
}
