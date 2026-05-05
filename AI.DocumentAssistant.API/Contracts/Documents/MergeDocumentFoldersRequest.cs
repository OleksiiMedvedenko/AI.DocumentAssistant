namespace AI.DocumentAssistant.API.Contracts.Documents;

public sealed class MergeDocumentFoldersRequest
{
    public Guid TargetFolderId { get; set; }
    public bool DeleteSourceFolder { get; set; } = true;
}
