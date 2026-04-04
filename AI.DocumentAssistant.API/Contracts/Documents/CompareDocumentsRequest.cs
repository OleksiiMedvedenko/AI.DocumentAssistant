namespace AI.DocumentAssistant.API.Contracts.Documents;

public sealed class CompareDocumentsRequest
{
    public Guid SecondDocumentId { get; set; }
    public string? Prompt { get; set; }
    public string? Language { get; set; }
}