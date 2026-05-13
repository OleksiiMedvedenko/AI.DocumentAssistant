using AI.DocumentAssistant.Application.Abstractions.Common;
using AI.DocumentAssistant.Application.Abstractions.Documents;
using AI.DocumentAssistant.Application.Common.Exceptions;
using AI.DocumentAssistant.Application.Documents.Dtos;
using AI.DocumentAssistant.Domain.Entities;
using AI.DocumentAssistant.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AI.DocumentAssistant.Application.Documents.Services;

public sealed class AiActionTemplateService : IAiActionTemplateService
{
    private static readonly HashSet<string> SupportedActionTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "extraction", "summary", "analysis", "comparison", "report", "evaluation", "custom"
    };

    private static readonly HashSet<string> SupportedOutputFormats = new(StringComparer.OrdinalIgnoreCase)
    {
        "json", "markdown", "text", "html", "pdf"
    };

    private readonly AppDbContext _dbContext;
    private readonly ICurrentUserService _currentUserService;

    public AiActionTemplateService(AppDbContext dbContext, ICurrentUserService currentUserService)
    {
        _dbContext = dbContext;
        _currentUserService = currentUserService;
    }

    public async Task<List<AiActionTemplateDto>> GetAllAsync(string? documentType, Guid? folderId, CancellationToken cancellationToken)
    {
        var userId = _currentUserService.GetUserId();
        var query = _dbContext.AiActionTemplates.Where(x => x.UserId == userId);

        if (!string.IsNullOrWhiteSpace(documentType))
        {
            var normalized = NormalizeOptional(documentType);
            query = query.Where(x => x.DocumentType == normalized || x.DocumentType == null);
        }

        if (folderId.HasValue)
        {
            query = query.Where(x => x.FolderId == folderId.Value || x.FolderId == null);
        }

        return await query
            .OrderBy(x => x.DocumentType)
            .ThenBy(x => x.Name)
            .Select(x => new AiActionTemplateDto
            {
                Id = x.Id,
                FolderId = x.FolderId,
                Name = x.Name,
                Description = x.Description,
                DocumentType = x.DocumentType,
                ActionType = x.ActionType,
                Prompt = x.Prompt,
                OutputFormat = x.OutputFormat,
                Language = x.Language,
                SaveResult = x.SaveResult,
                CreatedAtUtc = x.CreatedAtUtc,
                UpdatedAtUtc = x.UpdatedAtUtc
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<AiActionTemplateDto> CreateAsync(CreateAiActionTemplateRequestDto request, CancellationToken cancellationToken)
    {
        var userId = _currentUserService.GetUserId();
        var name = NormalizeRequired(request.Name, "Template name");
        var prompt = NormalizeRequired(request.Prompt, "Prompt");
        var documentType = NormalizeOptional(request.DocumentType);
        var actionType = NormalizeActionType(request.ActionType);
        var outputFormat = NormalizeOutputFormat(request.OutputFormat);
        var language = NormalizeLanguage(request.Language);
        var description = NormalizeDescription(request.Description);

        await EnsureFolderBelongsToUserAsync(userId, request.FolderId, cancellationToken);

        var duplicate = await _dbContext.AiActionTemplates.AnyAsync(
            x => x.UserId == userId && x.Name == name && x.DocumentType == documentType && x.FolderId == request.FolderId,
            cancellationToken);

        if (duplicate)
        {
            throw new BadRequestException("An AI action template with this name already exists for this scope.");
        }

        var entity = new AiActionTemplate
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            FolderId = request.FolderId,
            Name = name,
            Description = description,
            DocumentType = documentType,
            ActionType = actionType,
            Prompt = prompt,
            OutputFormat = outputFormat,
            Language = language,
            SaveResult = request.SaveResult ?? true,
            CreatedAtUtc = DateTime.UtcNow
        };

        _dbContext.AiActionTemplates.Add(entity);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return ToDto(entity);
    }

    public async Task<AiActionTemplateDto> UpdateAsync(Guid templateId, CreateAiActionTemplateRequestDto request, CancellationToken cancellationToken)
    {
        var userId = _currentUserService.GetUserId();
        var entity = await _dbContext.AiActionTemplates
            .FirstOrDefaultAsync(x => x.Id == templateId && x.UserId == userId, cancellationToken);

        if (entity is null)
        {
            throw new NotFoundException("AI action template not found.");
        }

        await EnsureFolderBelongsToUserAsync(userId, request.FolderId, cancellationToken);

        entity.FolderId = request.FolderId;
        entity.Name = NormalizeRequired(request.Name, "Template name");
        entity.Description = NormalizeDescription(request.Description);
        entity.Prompt = NormalizeRequired(request.Prompt, "Prompt");
        entity.DocumentType = NormalizeOptional(request.DocumentType);
        entity.ActionType = NormalizeActionType(request.ActionType);
        entity.OutputFormat = NormalizeOutputFormat(request.OutputFormat);
        entity.Language = NormalizeLanguage(request.Language);
        entity.SaveResult = request.SaveResult ?? entity.SaveResult;
        entity.UpdatedAtUtc = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);
        return ToDto(entity);
    }

    public async Task DeleteAsync(Guid templateId, CancellationToken cancellationToken)
    {
        var userId = _currentUserService.GetUserId();
        var entity = await _dbContext.AiActionTemplates
            .FirstOrDefaultAsync(x => x.Id == templateId && x.UserId == userId, cancellationToken);

        if (entity is null)
        {
            throw new NotFoundException("AI action template not found.");
        }

        _dbContext.AiActionTemplates.Remove(entity);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task EnsureFolderBelongsToUserAsync(Guid userId, Guid? folderId, CancellationToken cancellationToken)
    {
        if (folderId is null)
        {
            return;
        }

        var exists = await _dbContext.DocumentFolders.AnyAsync(x => x.Id == folderId && x.UserId == userId, cancellationToken);
        if (!exists)
        {
            throw new BadRequestException("Selected folder does not exist.");
        }
    }

    private static string NormalizeRequired(string? value, string fieldName)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new BadRequestException($"{fieldName} is required.");
        }

        return normalized.Length > 8000 ? normalized[..8000] : normalized;
    }

    private static string? NormalizeOptional(string? value)
    {
        var normalized = value?.Trim().ToLowerInvariant();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    private static string? NormalizeDescription(string? value)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized)) return null;
        return normalized.Length > 500 ? normalized[..500] : normalized;
    }

    private static string NormalizeActionType(string? value)
    {
        var normalized = NormalizeOptional(value)?.Replace("_", "-") ?? "custom";
        return SupportedActionTypes.Contains(normalized) ? normalized : "custom";
    }

    private static string NormalizeOutputFormat(string? value)
    {
        var normalized = NormalizeOptional(value)?.Replace("_", "-") ?? "markdown";
        if (normalized == "plain-text" || normalized == "plaintext") normalized = "text";
        if (!SupportedOutputFormats.Contains(normalized))
        {
            throw new BadRequestException("Unsupported output format. Supported: json, markdown, text, html, pdf.");
        }
        return normalized;
    }

    private static string? NormalizeLanguage(string? value)
    {
        var normalized = NormalizeOptional(value);
        if (normalized is null) return null;
        return normalized.Length > 20 ? normalized[..20] : normalized;
    }

    private static AiActionTemplateDto ToDto(AiActionTemplate entity)
    {
        return new AiActionTemplateDto
        {
            Id = entity.Id,
            FolderId = entity.FolderId,
            Name = entity.Name,
            Description = entity.Description,
            DocumentType = entity.DocumentType,
            ActionType = entity.ActionType,
            Prompt = entity.Prompt,
            OutputFormat = entity.OutputFormat,
            Language = entity.Language,
            SaveResult = entity.SaveResult,
            CreatedAtUtc = entity.CreatedAtUtc,
            UpdatedAtUtc = entity.UpdatedAtUtc
        };
    }
}
