using AI.DocumentAssistant.Application.Documents.Dtos;

namespace AI.DocumentAssistant.Application.Abstractions.Documents;

public interface IDocumentService
{
    Task<DocumentDto> UploadAsync(UploadDocumentRequestDto request, CancellationToken cancellationToken);

    Task<UploadDocumentsResultDto> UploadManyAsync(
        UploadDocumentsRequestDto request,
        CancellationToken cancellationToken);

    Task<List<DocumentDto>> GetAllAsync(Guid? folderId, CancellationToken cancellationToken);
    Task<DocumentDetailsDto> GetByIdAsync(Guid documentId, CancellationToken cancellationToken);
    Task<DocumentStatusDto> GetStatusAsync(Guid documentId, CancellationToken cancellationToken);
    Task DeleteAsync(Guid documentId, CancellationToken cancellationToken);
    Task<DocumentDto> MoveToFolderAsync(Guid documentId, MoveDocumentToFolderRequestDto request, CancellationToken cancellationToken);
    Task<SummarizeResultDto> SummarizeAsync(Guid documentId, SummarizeDocumentRequestDto request, CancellationToken cancellationToken);
    Task<ExtractedDataDto> ExtractAsync(Guid documentId, ExtractDocumentRequestDto request, CancellationToken cancellationToken);
    Task<List<ExtractedDataDto>> GetExtractionsAsync(Guid documentId, CancellationToken cancellationToken);
    Task<ExtractedDataDto> GetExtractionByIdAsync(Guid documentId, Guid extractionId, CancellationToken cancellationToken);
    Task<CompareDocumentsResultDto> CompareAsync(Guid firstDocumentId, CompareDocumentsRequestDto request, CancellationToken cancellationToken);
}