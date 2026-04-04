using AI.DocumentAssistant.Domain.Entities;
using AI.DocumentAssistant.Domain.Enums;
using AI.DocumentAssistant.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AI.DocumentAssistant.UnitTests.Infrastructure;

public static class TestDataSeeder
{
    public static async Task<(Guid UserId, Guid DocumentId)> SeedReadyDocumentAsync(
        AppDbContext dbContext,
        string email,
        string text,
        string fileName = "doc1.txt")
    {
        var user = await dbContext.Users
            .FirstOrDefaultAsync(x => x.Email == email);

        if (user is null)
        {
            user = new User
            {
                Id = Guid.NewGuid(),
                Email = email,
                PasswordHash = "hashed",
                CreatedAtUtc = DateTime.UtcNow
            };

            dbContext.Users.Add(user);
            await dbContext.SaveChangesAsync();
        }

        var document = new Document
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            FileName = fileName,
            OriginalFileName = fileName,
            ContentType = "text/plain",
            SizeInBytes = text.Length,
            StoragePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.txt"),
            Status = DocumentStatus.Ready,
            ExtractedText = text,
            UploadedAtUtc = DateTime.UtcNow,
            ProcessedAtUtc = DateTime.UtcNow
        };

        var chunks = Chunk(text)
            .Select((chunk, index) => new DocumentChunk
            {
                Id = Guid.NewGuid(),
                DocumentId = document.Id,
                ChunkIndex = index,
                Text = chunk
            })
            .ToList();

        document.Chunks = chunks;

        dbContext.Documents.Add(document);
        await dbContext.SaveChangesAsync();

        return (user.Id, document.Id);
    }

    private static List<string> Chunk(string text, int size = 300)
    {
        var result = new List<string>();

        for (var i = 0; i < text.Length; i += size)
        {
            var length = Math.Min(size, text.Length - i);
            result.Add(text.Substring(i, length));
        }

        return result;
    }
}