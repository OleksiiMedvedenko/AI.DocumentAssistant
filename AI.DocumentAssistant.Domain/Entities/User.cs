using AI.DocumentAssistant.Domain.Enums;

namespace AI.DocumentAssistant.Domain.Entities;

public sealed class User
{
    public Guid Id { get; set; }
    public string Email { get; set; } = default!;
    public string PasswordHash { get; set; } = default!;
    public string? DisplayName { get; set; }
    public UserRole Role { get; set; } = UserRole.User;
    public bool IsActive { get; set; } = true;
    public bool EmailConfirmed { get; set; }
    public DateTime? EmailConfirmedAtUtc { get; set; }
    public string? EmailConfirmationTokenHash { get; set; }
    public DateTime? EmailConfirmationTokenExpiresAtUtc { get; set; }
    public DateTime? EmailConfirmationSentAtUtc { get; set; }
    public AuthProvider AuthProvider { get; set; } = AuthProvider.Local;
    public string? ExternalProviderId { get; set; }
    public bool HasUnlimitedAiUsage { get; set; } = false;
    public int MonthlyChatMessageLimit { get; set; } = 100;
    public int MonthlyDocumentUploadLimit { get; set; } = 30;
    public int MonthlySummarizationLimit { get; set; } = 20;
    public int MonthlyExtractionLimit { get; set; } = 20;
    public int MonthlyComparisonLimit { get; set; } = 10;
    public DateTime CreatedAtUtc { get; set; }

    public ICollection<Document> Documents { get; set; } = new List<Document>();
    public ICollection<DocumentFolder> DocumentFolders { get; set; } = new List<DocumentFolder>();
    public ICollection<RefreshToken> RefreshTokens { get; set; } = new List<RefreshToken>();
    public ICollection<UserUsageRecord> UsageRecords { get; set; } = new List<UserUsageRecord>();
    public ICollection<UserQuotaOverride> QuotaOverrides { get; set; } = new List<UserQuotaOverride>();
}