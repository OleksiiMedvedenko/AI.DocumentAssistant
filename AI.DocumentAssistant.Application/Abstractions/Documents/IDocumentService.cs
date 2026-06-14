using AI.DocumentAssistant.Application.Documents.Dtos;

namespace AI.DocumentAssistant.Application.Abstractions.Documents;

public interface IDocumentService
{
    Task<DocumentDto> UploadAsync(UploadDocumentRequestDto request, CancellationToken cancellationToken);

    Task<UploadDocumentsResultDto> UploadManyAsync(
        UploadDocumentsRequestDto request,
        CancellationToken cancellationToken);

    Task<List<DocumentDto>> GetAllAsync(Guid? folderId, Guid? organizationId, CancellationToken cancellationToken);
    Task<List<DocumentDto>> GetInboxAsync(CancellationToken cancellationToken);
    Task<DocumentDashboardDto> GetDashboardAsync(CancellationToken cancellationToken);
    Task<List<DocumentFolderSuggestionResponseDto>> GetFolderSuggestionsAsync(Guid documentId, CancellationToken cancellationToken);
    Task<DocumentDto> AcceptFolderSuggestionAsync(Guid documentId, Guid suggestionId, CancellationToken cancellationToken);
    Task<DocumentFolderSuggestionResponseDto> RejectFolderSuggestionAsync(Guid documentId, Guid suggestionId, CancellationToken cancellationToken);
    Task<List<RelatedDocumentDto>> GetRelatedDocumentsAsync(Guid documentId, CancellationToken cancellationToken);
    Task<RegenerateFolderSuggestionsResultDto> RegenerateFolderSuggestionsAsync(Guid documentId, CancellationToken cancellationToken);
    Task<DocumentIntelligenceSnapshotDto> GetIntelligenceSnapshotAsync(Guid documentId, CancellationToken cancellationToken);
    Task<DocumentDetailsDto> GetByIdAsync(Guid documentId, CancellationToken cancellationToken);
    Task<DocumentStatusDto> GetStatusAsync(Guid documentId, CancellationToken cancellationToken);
    Task DeleteAsync(Guid documentId, CancellationToken cancellationToken);
    Task<DocumentDto> ConfirmFolderAssignmentAsync(Guid documentId, CancellationToken cancellationToken);
    Task<DocumentDto> MoveToFolderAsync(Guid documentId, MoveDocumentToFolderRequestDto request, CancellationToken cancellationToken);
    Task<SummarizeResultDto> SummarizeAsync(Guid documentId, SummarizeDocumentRequestDto request, CancellationToken cancellationToken);
    Task<ExtractedDataDto> ExtractAsync(Guid documentId, ExtractDocumentRequestDto request, CancellationToken cancellationToken);
    Task<List<ExtractedDataDto>> GetExtractionsAsync(Guid documentId, CancellationToken cancellationToken);
    Task<ExtractedDataDto> GetExtractionByIdAsync(Guid documentId, Guid extractionId, CancellationToken cancellationToken);
    Task<CompareDocumentsResultDto> CompareAsync(Guid firstDocumentId, CompareDocumentsRequestDto request, CancellationToken cancellationToken);

    Task<DocumentPreviewMetaDto> GetPreviewMetaAsync(Guid documentId, CancellationToken cancellationToken);

    Task<(Stream Stream, string ContentType, string FileName)> OpenOriginalFileAsync(
        Guid documentId,
        CancellationToken cancellationToken);

    Task<(Stream Stream, string ContentType, string FileName)> OpenPreviewFileAsync(
        Guid documentId,
        CancellationToken cancellationToken);
}