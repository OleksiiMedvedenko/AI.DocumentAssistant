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
        private readonly IDocumentIntelligenceService _documentIntelligenceService;

        public DocumentFolderService(
            AppDbContext dbContext,
            ICurrentUserService currentUserService,
            IDocumentIntelligenceService documentIntelligenceService)
        {
            _dbContext = dbContext;
            _currentUserService = currentUserService;
            _documentIntelligenceService = documentIntelligenceService;
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

            var templates = await _dbContext.AiActionTemplates
                .Where(x => x.UserId == userId && x.FolderId == folderId)
                .ToListAsync(cancellationToken);

            foreach (var template in templates)
            {
                template.FolderId = null;
            }

            _dbContext.DocumentFolders.Remove(folder);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }


        public async Task<List<FolderDuplicateSuggestionDto>> GetDuplicateSuggestionsAsync(CancellationToken cancellationToken)
        {
            var userId = _currentUserService.GetUserId();

            var folders = await _dbContext.DocumentFolders
                .Where(x => x.UserId == userId)
                .OrderBy(x => x.Name)
                .ToListAsync(cancellationToken);

            var result = new List<FolderDuplicateSuggestionDto>();

            for (var i = 0; i < folders.Count; i++)
            {
                for (var j = i + 1; j < folders.Count; j++)
                {
                    var first = folders[i];
                    var second = folders[j];

                    if (first.ParentFolderId != second.ParentFolderId)
                    {
                        continue;
                    }

                    var similarity = CalculateNameSimilarity(first.NameEn, second.NameEn);
                    if (similarity < 0.72m)
                    {
                        continue;
                    }

                    result.Add(new FolderDuplicateSuggestionDto
                    {
                        FirstFolderId = first.Id,
                        FirstFolderName = first.Name,
                        SecondFolderId = second.Id,
                        SecondFolderName = second.Name,
                        Similarity = similarity,
                        Reason = "Folder names are very similar and live under the same parent. Consider merging or renaming them."
                    });
                }
            }

            return result
                .OrderByDescending(x => x.Similarity)
                .Take(20)
                .ToList();
        }


        public async Task<MergeDocumentFoldersResultDto> MergeAsync(
            Guid sourceFolderId,
            MergeDocumentFoldersRequestDto request,
            CancellationToken cancellationToken)
        {
            var userId = _currentUserService.GetUserId();

            if (sourceFolderId == request.TargetFolderId)
            {
                throw new BadRequestException("Source and target folders must be different.");
            }

            var source = await _dbContext.DocumentFolders
                .FirstOrDefaultAsync(x => x.Id == sourceFolderId && x.UserId == userId, cancellationToken);

            var target = await _dbContext.DocumentFolders
                .FirstOrDefaultAsync(x => x.Id == request.TargetFolderId && x.UserId == userId, cancellationToken);

            if (source is null || target is null)
            {
                throw new NotFoundException("Folder not found.");
            }

            var documents = await _dbContext.Documents
                .Where(x => x.UserId == userId && x.FolderId == sourceFolderId)
                .ToListAsync(cancellationToken);

            foreach (var document in documents)
            {
                document.FolderId = target.Id;
                document.FolderClassificationStatus = "merged-folder";
                document.FolderClassificationReason = $"Moved from merged folder '{source.Name}' to '{target.Name}'.";
                document.WasFolderAutoAssigned = false;
            }

            var suggestions = await _dbContext.DocumentFolderSuggestions
                .Where(x => x.UserId == userId && x.ExistingFolderId == sourceFolderId)
                .ToListAsync(cancellationToken);

            foreach (var suggestion in suggestions)
            {
                suggestion.ExistingFolderId = target.Id;
                suggestion.ProposedKey = target.Key;
                suggestion.ProposedName = target.Name;
                suggestion.ProposedNamePl = target.NamePl;
                suggestion.ProposedNameEn = target.NameEn;
                suggestion.ProposedNameUa = target.NameUa;
                suggestion.ProposedParentFolderId = target.ParentFolderId;
                suggestion.Reason = $"{suggestion.Reason} Source folder was merged into target folder.";
            }

            var rules = await _dbContext.UserFolderRules
                .Where(x => x.UserId == userId && x.FolderId == sourceFolderId)
                .ToListAsync(cancellationToken);

            foreach (var rule in rules)
            {
                rule.FolderId = target.Id;
                rule.Weight = Math.Min(3m, rule.Weight + 0.10m);
                rule.LastMatchedAtUtc = DateTime.UtcNow;
            }

            var childFolders = await _dbContext.DocumentFolders
                .Where(x => x.UserId == userId && x.ParentFolderId == sourceFolderId)
                .ToListAsync(cancellationToken);

            foreach (var child in childFolders)
            {
                child.ParentFolderId = target.Id;
            }

            var folderChatSessions = await _dbContext.ChatSessions
                .Where(x => x.UserId == userId && x.FolderId == sourceFolderId)
                .ToListAsync(cancellationToken);

            foreach (var session in folderChatSessions)
            {
                session.FolderId = target.Id;
            }

            var sourceProfile = await _dbContext.FolderEmbeddingProfiles
                .FirstOrDefaultAsync(x => x.UserId == userId && x.FolderId == sourceFolderId, cancellationToken);

            if (sourceProfile is not null)
            {
                _dbContext.FolderEmbeddingProfiles.Remove(sourceProfile);
            }

            var deleted = false;
            if (request.DeleteSourceFolder)
            {
                _dbContext.DocumentFolders.Remove(source);
                deleted = true;
            }

            await _dbContext.SaveChangesAsync(cancellationToken);
            await _documentIntelligenceService.UpdateFolderProfileAsync(userId, target.Id, cancellationToken);

            return new MergeDocumentFoldersResultDto
            {
                SourceFolderId = sourceFolderId,
                TargetFolderId = target.Id,
                MovedDocuments = documents.Count,
                MovedSuggestions = suggestions.Count,
                MovedUserRules = rules.Count,
                SourceFolderDeleted = deleted
            };
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



        private static decimal CalculateNameSimilarity(string first, string second)
        {
            var a = Regex.Replace(first.Trim().ToLowerInvariant(), @"[^a-z0-9]+", "");
            var b = Regex.Replace(second.Trim().ToLowerInvariant(), @"[^a-z0-9]+", "");

            if (a.Length == 0 || b.Length == 0)
            {
                return 0m;
            }

            if (a == b)
            {
                return 1m;
            }

            if (a.Contains(b) || b.Contains(a))
            {
                return 0.86m;
            }

            var distance = LevenshteinDistance(a, b);
            var max = Math.Max(a.Length, b.Length);
            return Math.Round(1m - (decimal)distance / max, 4);
        }

        private static int LevenshteinDistance(string first, string second)
        {
            var costs = new int[second.Length + 1];
            for (var j = 0; j < costs.Length; j++)
            {
                costs[j] = j;
            }

            for (var i = 1; i <= first.Length; i++)
            {
                costs[0] = i;
                var previous = i - 1;

                for (var j = 1; j <= second.Length; j++)
                {
                    var current = costs[j];
                    costs[j] = first[i - 1] == second[j - 1]
                        ? previous
                        : Math.Min(Math.Min(costs[j - 1], costs[j]), previous) + 1;
                    previous = current;
                }
            }

            return costs[second.Length];
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
