namespace AI.DocumentAssistant.Application.Documents.Dtos;

public sealed class MergeDocumentFoldersRequestDto
{
    public Guid TargetFolderId { get; set; }
    public bool DeleteSourceFolder { get; set; } = true;
}
