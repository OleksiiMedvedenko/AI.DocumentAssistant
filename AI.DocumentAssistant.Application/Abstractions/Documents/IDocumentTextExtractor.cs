namespace AI.DocumentAssistant.Application.Abstractions.Documents
{
    public interface IDocumentTextExtractor
    {
        bool CanHandle(string fileName, string? contentType);
        Task<string> ExtractTextAsync(Stream stream, CancellationToken cancellationToken);
    }
}
