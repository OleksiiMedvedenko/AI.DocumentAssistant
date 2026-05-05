namespace AI.DocumentAssistant.Application.Documents.Dtos;

public sealed class FolderDuplicateSuggestionDto
{
    public Guid FirstFolderId { get; set; }
    public string FirstFolderName { get; set; } = default!;
    public Guid SecondFolderId { get; set; }
    public string SecondFolderName { get; set; } = default!;
    public decimal Similarity { get; set; }
    public string Reason { get; set; } = default!;
}
