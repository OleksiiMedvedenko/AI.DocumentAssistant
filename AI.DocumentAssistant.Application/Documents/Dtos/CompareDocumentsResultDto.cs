namespace AI.DocumentAssistant.Application.Documents.Dtos;

public sealed class CompareDocumentsResultDto
{
    public Guid FirstDocumentId { get; set; }
    public Guid SecondDocumentId { get; set; }
    public string FirstDocumentName { get; set; } = default!;
    public string SecondDocumentName { get; set; } = default!;
    public string Result { get; set; } = default!;
}