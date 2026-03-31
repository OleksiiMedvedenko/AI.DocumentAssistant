namespace AI.DocumentAssistant.Abstraction.Abstractions.Documents
{
    public interface IPdfTextExtractor
    {
        Task<string> ExtractTextAsync(Stream stream, CancellationToken cancellationToken);
    }
}
