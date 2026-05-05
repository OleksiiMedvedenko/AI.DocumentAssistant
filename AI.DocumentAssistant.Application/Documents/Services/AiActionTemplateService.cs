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
    private readonly AppDbContext _dbContext;
    private readonly ICurrentUserService _currentUserService;

    public AiActionTemplateService(AppDbContext dbContext, ICurrentUserService currentUserService)
    {
        _dbContext = dbContext;
        _currentUserService = currentUserService;
    }

    public async Task<List<AiActionTemplateDto>> GetAllAsync(string? documentType, CancellationToken cancellationToken)
    {
        var userId = _currentUserService.GetUserId();
        var query = _dbContext.AiActionTemplates.Where(x => x.UserId == userId);

        if (!string.IsNullOrWhiteSpace(documentType))
        {
            var normalized = documentType.Trim().ToLowerInvariant();
            query = query.Where(x => x.DocumentType == normalized || x.DocumentType == null);
        }

        return await query
            .OrderBy(x => x.DocumentType)
            .ThenBy(x => x.Name)
            .Select(x => new AiActionTemplateDto
            {
                Id = x.Id,
                Name = x.Name,
                DocumentType = x.DocumentType,
                Prompt = x.Prompt,
                OutputFormat = x.OutputFormat,
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
        var outputFormat = NormalizeOptional(request.OutputFormat) ?? "text";

        var duplicate = await _dbContext.AiActionTemplates.AnyAsync(
            x => x.UserId == userId && x.Name == name && x.DocumentType == documentType,
            cancellationToken);

        if (duplicate)
        {
            throw new BadRequestException("An AI action template with this name already exists for this document type.");
        }

        var entity = new AiActionTemplate
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Name = name,
            DocumentType = documentType,
            Prompt = prompt,
            OutputFormat = outputFormat,
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

        entity.Name = NormalizeRequired(request.Name, "Template name");
        entity.Prompt = NormalizeRequired(request.Prompt, "Prompt");
        entity.DocumentType = NormalizeOptional(request.DocumentType);
        entity.OutputFormat = NormalizeOptional(request.OutputFormat) ?? "text";
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

    private static string NormalizeRequired(string? value, string fieldName)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new BadRequestException($"{fieldName} is required.");
        }

        return normalized;
    }

    private static string? NormalizeOptional(string? value)
    {
        var normalized = value?.Trim().ToLowerInvariant();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    private static AiActionTemplateDto ToDto(AiActionTemplate entity)
    {
        return new AiActionTemplateDto
        {
            Id = entity.Id,
            Name = entity.Name,
            DocumentType = entity.DocumentType,
            Prompt = entity.Prompt,
            OutputFormat = entity.OutputFormat,
            CreatedAtUtc = entity.CreatedAtUtc,
            UpdatedAtUtc = entity.UpdatedAtUtc
        };
    }
}
