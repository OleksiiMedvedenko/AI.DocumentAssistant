namespace AI.DocumentAssistant.Application.Documents.Dtos;

public sealed class DocumentFolderSuggestionResponseDto
{
    public Guid Id { get; set; }
    public Guid DocumentId { get; set; }
    public Guid? ExistingFolderId { get; set; }
    public string? ExistingFolderName { get; set; }
    public string ProposedKey { get; set; } = default!;
    public string ProposedName { get; set; } = default!;
    public string ProposedNamePl { get; set; } = default!;
    public string ProposedNameEn { get; set; } = default!;
    public string ProposedNameUa { get; set; } = default!;
    public Guid? ProposedParentFolderId { get; set; }
    public decimal Score { get; set; }
    public decimal RuleScore { get; set; }
    public decimal SemanticScore { get; set; }
    public decimal UserHistoryScore { get; set; }
    public decimal FinalScore { get; set; }
    public int Rank { get; set; }
    public string Reason { get; set; } = default!;
    public string Status { get; set; } = default!;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? AcceptedAtUtc { get; set; }
    public DateTime? RejectedAtUtc { get; set; }
}
