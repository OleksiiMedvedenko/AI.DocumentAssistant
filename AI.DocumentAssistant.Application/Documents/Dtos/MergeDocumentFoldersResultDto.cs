namespace AI.DocumentAssistant.Application.Documents.Dtos;

public sealed class MergeDocumentFoldersResultDto
{
    public Guid SourceFolderId { get; set; }
    public Guid TargetFolderId { get; set; }
    public int MovedDocuments { get; set; }
    public int MovedSuggestions { get; set; }
    public int MovedUserRules { get; set; }
    public bool SourceFolderDeleted { get; set; }
}
