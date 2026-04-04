namespace AI.DocumentAssistant.Application.Documents.Dtos;

public sealed class CompareDocumentsRequestDto
{
    public Guid SecondDocumentId { get; set; }
    public string? Prompt { get; set; }
}