using AI.DocumentAssistant.Application.Documents.Dtos;
using Microsoft.AspNetCore.Http;

namespace AI.DocumentAssistant.Application.Abstractions.Documents;

public interface IDocumentService
{
    Task<DocumentDto> UploadAsync(IFormFile file, CancellationToken cancellationToken);
    Task<IReadOnlyList<DocumentDto>> GetAllAsync(CancellationToken cancellationToken);
    Task<DocumentDetailsDto> GetByIdAsync(Guid documentId, CancellationToken cancellationToken);
    Task<DocumentStatusDto> GetStatusAsync(Guid documentId, CancellationToken cancellationToken);
    Task DeleteAsync(Guid documentId, CancellationToken cancellationToken);
    Task<SummarizeResultDto> SummarizeAsync(
        Guid documentId,
        SummarizeDocumentRequestDto request,
        CancellationToken cancellationToken);

    Task<ExtractedDataDto> ExtractAsync(
        Guid documentId,
        ExtractDocumentRequestDto request,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<ExtractedDataDto>> GetExtractionsAsync(
        Guid documentId,
        CancellationToken cancellationToken);

    Task<ExtractedDataDto> GetExtractionByIdAsync(
        Guid documentId,
        Guid extractionId,
        CancellationToken cancellationToken);

    Task<CompareDocumentsResultDto> CompareAsync(
        Guid firstDocumentId,
        CompareDocumentsRequestDto request,
        CancellationToken cancellationToken);
}