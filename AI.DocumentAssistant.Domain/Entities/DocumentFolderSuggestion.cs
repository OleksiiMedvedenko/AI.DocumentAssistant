namespace AI.DocumentAssistant.Domain.Entities;

public sealed class DocumentFolderSuggestion
{
    public Guid Id { get; set; }
    public Guid DocumentId { get; set; }
    public Guid UserId { get; set; }
    public Guid? ExistingFolderId { get; set; }

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
    public string Status { get; set; } = "pending";

    public DateTime CreatedAtUtc { get; set; }
    public DateTime? AcceptedAtUtc { get; set; }
    public DateTime? RejectedAtUtc { get; set; }

    public Document Document { get; set; } = default!;
    public User User { get; set; } = default!;
    public DocumentFolder? ExistingFolder { get; set; }
    public DocumentFolder? ProposedParentFolder { get; set; }
}
