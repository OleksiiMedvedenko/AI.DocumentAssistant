namespace AI.DocumentAssistant.Application.Documents.Dtos;

public sealed class DocumentDashboardDto
{
    public int TotalDocuments { get; set; }
    public int NewDocuments { get; set; }
    public int UnfiledDocuments { get; set; }
    public int PendingReviewDocuments { get; set; }
    public int ReadyDocuments { get; set; }
    public int FailedDocuments { get; set; }
    public List<DocumentDashboardBucketDto> ByStatus { get; set; } = [];
    public List<DocumentDashboardBucketDto> ByFolder { get; set; } = [];
    public List<DocumentDashboardBucketDto> ByDocumentType { get; set; } = [];
    public List<DocumentDto> RecentDocuments { get; set; } = [];
    public List<DocumentFolderSuggestionResponseDto> PendingSuggestions { get; set; } = [];
}

public sealed class DocumentDashboardBucketDto
{
    public string Key { get; set; } = default!;
    public string Name { get; set; } = default!;
    public int Count { get; set; }
}
