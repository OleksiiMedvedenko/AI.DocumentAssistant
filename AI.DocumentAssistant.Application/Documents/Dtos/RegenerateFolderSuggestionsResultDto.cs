namespace AI.DocumentAssistant.Application.Documents.Dtos;

public sealed class RegenerateFolderSuggestionsResultDto
{
    public Guid DocumentId { get; set; }
    public DocumentIntelligenceSnapshotDto Intelligence { get; set; } = default!;
    public List<DocumentFolderSuggestionResponseDto> Suggestions { get; set; } = [];
}
