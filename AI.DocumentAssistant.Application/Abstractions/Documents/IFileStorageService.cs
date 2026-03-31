namespace AI.DocumentAssistant.Application.Abstractions.Documents
{
    public interface IFileStorageService
    {
        Task<string> SaveAsync(Stream stream, string fileName, CancellationToken cancellationToken);
        Task DeleteAsync(string path, CancellationToken cancellationToken);
        Task<Stream> OpenReadAsync(string path, CancellationToken cancellationToken);
    }
}
