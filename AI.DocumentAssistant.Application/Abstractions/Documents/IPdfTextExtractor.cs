namespace AI.DocumentAssistant.Application.Abstractions.Documents
{
    public interface IPdfTextExtractor
    {
        Task<string> ExtractTextAsync(Stream stream, CancellationToken cancellationToken);
    }
}
