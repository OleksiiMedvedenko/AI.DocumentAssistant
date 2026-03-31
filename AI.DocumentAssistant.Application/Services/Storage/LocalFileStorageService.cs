using AI.DocumentAssistant.Application.Abstractions.Documents;
using Microsoft.Extensions.Options;

namespace AI.DocumentAssistant.Application.Services.Storage
{
    public sealed class LocalFileStorageService : IFileStorageService
    {
        private readonly string _rootPath;

        public LocalFileStorageService(IOptions<LocalStorageOptions> options)
        {
            _rootPath = Path.GetFullPath(options.Value.RootPath);
            Directory.CreateDirectory(_rootPath);
        }

        public async Task<string> SaveAsync(Stream stream, string fileName, CancellationToken cancellationToken)
        {
            var fullPath = Path.Combine(_rootPath, fileName);

            await using var fileStream = File.Create(fullPath);
            await stream.CopyToAsync(fileStream, cancellationToken);

            return fullPath;
        }

        public Task DeleteAsync(string path, CancellationToken cancellationToken)
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }

            return Task.CompletedTask;
        }

        public Task<Stream> OpenReadAsync(string path, CancellationToken cancellationToken)
        {
            Stream stream = File.OpenRead(path);
            return Task.FromResult(stream);
        }
    }
}
