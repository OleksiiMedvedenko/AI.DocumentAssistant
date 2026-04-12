using AI.DocumentAssistant.Application.Abstractions.Common;
using AI.DocumentAssistant.Application.Abstractions.Documents;
using AI.DocumentAssistant.Application.Common.Exceptions;
using AI.DocumentAssistant.Application.Documents.Dtos;
using AI.DocumentAssistant.Domain.Entities;
using AI.DocumentAssistant.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using System.Text.RegularExpressions;

namespace AI.DocumentAssistant.Application.Documents.Services
{
    public sealed class DocumentFolderService : IDocumentFolderService
    {
        private readonly AppDbContext _dbContext;
        private readonly ICurrentUserService _currentUserService;

        public DocumentFolderService(AppDbContext dbContext, ICurrentUserService currentUserService)
        {
            _dbContext = dbContext;
            _currentUserService = currentUserService;
        }

        public async Task<List<DocumentFolderDto>> GetTreeAsync(CancellationToken cancellationToken)
        {
            var userId = _currentUserService.GetUserId();

            var folders = await _dbContext.DocumentFolders
                .Where(x => x.UserId == userId)
                .OrderBy(x => x.Name)
                .ToListAsync(cancellationToken);

            var documentCounts = await _dbContext.Documents
                .Where(x => x.UserId == userId && x.FolderId != null)
                .GroupBy(x => x.FolderId!.Value)
                .Select(x => new { FolderId = x.Key, Count = x.Count() })
                .ToDictionaryAsync(x => x.FolderId, x => x.Count, cancellationToken);

            var map = folders.ToDictionary(
                x => x.Id,
                x => new DocumentFolderDto
                {
                    Id = x.Id,
                    ParentFolderId = x.ParentFolderId,
                    Key = x.Key,
                    Name = x.Name,
                    NamePl = x.NamePl,
                    NameEn = x.NameEn,
                    NameUa = x.NameUa,
                    IsSystemGenerated = x.IsSystemGenerated,
                    DocumentCount = documentCounts.TryGetValue(x.Id, out var count) ? count : 0
                });

            foreach (var folder in map.Values)
            {
                if (folder.ParentFolderId is { } parentId && map.TryGetValue(parentId, out var parent))
                {
                    parent.Children.Add(folder);
                }
            }

            return map.Values.Where(x => x.ParentFolderId is null).OrderBy(x => x.Name).ToList();
        }

        public async Task<DocumentFolderDto> CreateAsync(CreateDocumentFolderRequestDto request, CancellationToken cancellationToken)
        {
            var userId = _currentUserService.GetUserId();

            await EnsureParentBelongsToUserAsync(userId, request.ParentFolderId, cancellationToken);

            var name = NormalizeRequired(request.Name, "Folder name");
            var namePl = NormalizeRequired(request.NamePl, "Folder name (pl)");
            var nameEn = NormalizeRequired(request.NameEn, "Folder name (en)");
            var nameUa = NormalizeRequired(request.NameUa, "Folder name (ua)");
            var key = Slugify(nameEn);

            var exists = await _dbContext.DocumentFolders.AnyAsync(
                x => x.UserId == userId &&
                     x.ParentFolderId == request.ParentFolderId &&
                     x.Key == key,
                cancellationToken);

            if (exists)
            {
                throw new BadRequestException("A folder with the same key already exists in this location.");
            }

            var entity = new DocumentFolder
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                ParentFolderId = request.ParentFolderId,
                Key = key,
                Name = name,
                NamePl = namePl,
                NameEn = nameEn,
                NameUa = nameUa,
                IsSystemGenerated = false,
                CreatedAtUtc = DateTime.UtcNow
            };

            _dbContext.DocumentFolders.Add(entity);
            await _dbContext.SaveChangesAsync(cancellationToken);

            return ToDto(entity, 0);
        }

        public async Task<DocumentFolderDto> UpdateAsync(Guid folderId, UpdateDocumentFolderRequestDto request, CancellationToken cancellationToken)
        {
            var userId = _currentUserService.GetUserId();

            var folder = await _dbContext.DocumentFolders
                .FirstOrDefaultAsync(x => x.Id == folderId && x.UserId == userId, cancellationToken);

            if (folder is null)
            {
                throw new NotFoundException("Folder not found.");
            }

            folder.Name = NormalizeRequired(request.Name, "Folder name");
            folder.NamePl = NormalizeRequired(request.NamePl, "Folder name (pl)");
            folder.NameEn = NormalizeRequired(request.NameEn, "Folder name (en)");
            folder.NameUa = NormalizeRequired(request.NameUa, "Folder name (ua)");
            folder.Key = Slugify(folder.NameEn);

            var duplicate = await _dbContext.DocumentFolders.AnyAsync(
                x => x.Id != folder.Id &&
                     x.UserId == userId &&
                     x.ParentFolderId == folder.ParentFolderId &&
                     x.Key == folder.Key,
                cancellationToken);

            if (duplicate)
            {
                throw new BadRequestException("Another folder with the same key already exists in this location.");
            }

            await _dbContext.SaveChangesAsync(cancellationToken);

            var count = await _dbContext.Documents.CountAsync(x => x.FolderId == folder.Id, cancellationToken);
            return ToDto(folder, count);
        }

        public async Task DeleteAsync(Guid folderId, CancellationToken cancellationToken)
        {
            var userId = _currentUserService.GetUserId();

            var folder = await _dbContext.DocumentFolders
                .Include(x => x.Children)
                .FirstOrDefaultAsync(x => x.Id == folderId && x.UserId == userId, cancellationToken);

            if (folder is null)
            {
                throw new NotFoundException("Folder not found.");
            }

            if (folder.Children.Count > 0)
            {
                throw new BadRequestException("Cannot delete a folder that still contains subfolders.");
            }

            var hasDocuments = await _dbContext.Documents
                .AnyAsync(x => x.UserId == userId && x.FolderId == folder.Id, cancellationToken);

            if (hasDocuments)
            {
                throw new BadRequestException("Cannot delete a folder that still contains documents.");
            }

            var folderChatSessions = await _dbContext.ChatSessions
                .Where(x => x.UserId == userId && x.FolderId == folder.Id)
                .ToListAsync(cancellationToken);

            foreach (var session in folderChatSessions)
            {
                session.FolderId = null;
                session.Folder = null;
            }

            _dbContext.DocumentFolders.Remove(folder);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        private async Task EnsureParentBelongsToUserAsync(Guid userId, Guid? parentFolderId, CancellationToken cancellationToken)
        {
            if (parentFolderId is null)
            {
                return;
            }

            var exists = await _dbContext.DocumentFolders.AnyAsync(
                x => x.Id == parentFolderId && x.UserId == userId,
                cancellationToken);

            if (!exists)
            {
                throw new BadRequestException("Parent folder was not found.");
            }
        }

        private static string NormalizeRequired(string? value, string fieldName)
        {
            var normalized = value?.Trim();
            if (string.IsNullOrWhiteSpace(normalized))
            {
                throw new BadRequestException($"{fieldName} is required.");
            }

            return normalized;
        }

        private static string Slugify(string value)
        {
            var lower = value.Trim().ToLowerInvariant();
            var compact = Regex.Replace(lower, @"\s+", "-");
            var safe = Regex.Replace(compact, @"[^a-z0-9\-]", "");
            safe = Regex.Replace(safe, @"\-{2,}", "-").Trim('-');
            return string.IsNullOrWhiteSpace(safe) ? "folder" : safe;
        }

        private static DocumentFolderDto ToDto(DocumentFolder folder, int documentCount)
        {
            return new DocumentFolderDto
            {
                Id = folder.Id,
                ParentFolderId = folder.ParentFolderId,
                Key = folder.Key,
                Name = folder.Name,
                NamePl = folder.NamePl,
                NameEn = folder.NameEn,
                NameUa = folder.NameUa,
                IsSystemGenerated = folder.IsSystemGenerated,
                DocumentCount = documentCount
            };
        }
    }
}
