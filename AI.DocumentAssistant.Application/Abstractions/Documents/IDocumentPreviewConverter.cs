namespace AI.DocumentAssistant.Application.Abstractions.Documents
{
    public interface IDocumentPreviewConverter
    {
        Task<(Stream Stream, string ContentType, string FileName)> ConvertToPreviewAsync(
            string sourcePath,
            string originalFileName,
            string contentType,
            CancellationToken cancellationToken);
    }
}
