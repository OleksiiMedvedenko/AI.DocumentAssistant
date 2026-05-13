using System.Text;
using System.Text.Json;
using AI.DocumentAssistant.Application.Abstractions.AI;
using AI.DocumentAssistant.Application.Abstractions.Common;
using AI.DocumentAssistant.Application.Abstractions.Documents;
using AI.DocumentAssistant.Application.Abstractions.Usage;
using AI.DocumentAssistant.Application.Common.Exceptions;
using AI.DocumentAssistant.Application.Documents.Dtos;
using AI.DocumentAssistant.Domain.Entities;
using AI.DocumentAssistant.Domain.Enums;
using AI.DocumentAssistant.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AI.DocumentAssistant.Application.Documents.Services;

public sealed class AiActionRunService : IAiActionRunService
{
    private static readonly HashSet<string> SupportedActionTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "extraction", "summary", "analysis", "comparison", "report", "evaluation", "custom"
    };

    private static readonly HashSet<string> SupportedOutputFormats = new(StringComparer.OrdinalIgnoreCase)
    {
        "json", "markdown", "text", "plaintext", "plain-text", "pdf", "html"
    };

    private readonly AppDbContext _dbContext;
    private readonly ICurrentUserService _currentUserService;
    private readonly IOpenAiService _openAiService;
    private readonly IFileStorageService _fileStorageService;
    private readonly IAiActionPdfRenderer _pdfRenderer;
    private readonly IUsageQuotaService _usageQuotaService;
    private readonly IUsageTrackingService _usageTrackingService;

    public AiActionRunService(
        AppDbContext dbContext,
        ICurrentUserService currentUserService,
        IOpenAiService openAiService,
        IFileStorageService fileStorageService,
        IAiActionPdfRenderer pdfRenderer,
        IUsageQuotaService usageQuotaService,
        IUsageTrackingService usageTrackingService)
    {
        _dbContext = dbContext;
        _currentUserService = currentUserService;
        _openAiService = openAiService;
        _fileStorageService = fileStorageService;
        _pdfRenderer = pdfRenderer;
        _usageQuotaService = usageQuotaService;
        _usageTrackingService = usageTrackingService;
    }

    public async Task<AiActionRunDto> RunTemplateAsync(
        Guid documentId,
        Guid templateId,
        RunAiActionTemplateRequestDto request,
        CancellationToken cancellationToken)
    {
        var userId = _currentUserService.GetUserId();

        var document = await _dbContext.Documents
            .Include(x => x.Folder)
            .FirstOrDefaultAsync(x => x.Id == documentId && x.UserId == userId, cancellationToken);

        if (document is null)
        {
            throw new NotFoundException("Document not found.");
        }

        EnsureDocumentReady(document);

        var template = await _dbContext.AiActionTemplates
            .FirstOrDefaultAsync(x => x.Id == templateId && x.UserId == userId, cancellationToken);

        if (template is null)
        {
            throw new NotFoundException("AI action template not found.");
        }

        await ValidateTemplateAppliesToDocumentAsync(template, document, userId, cancellationToken);

        var outputFormat = NormalizeOutputFormat(request.OutputFormat ?? template.OutputFormat);
        var actionType = NormalizeActionType(template.ActionType);
        var language = NormalizeLanguage(request.Language ?? template.Language);
        var saveResult = request.SaveResult ?? template.SaveResult;
        var usageType = ResolveUsageType(actionType, outputFormat);

        await _usageQuotaService.EnsureWithinQuotaAsync(userId, usageType, 1, cancellationToken);

        var run = new AiActionRun
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            DocumentId = document.Id,
            TemplateId = template.Id,
            TemplateName = template.Name,
            ActionType = actionType,
            OutputFormat = outputFormat,
            Language = language,
            Prompt = template.Prompt,
            Status = "running",
            CreatedAtUtc = DateTime.UtcNow
        };

        if (saveResult)
        {
            _dbContext.AiActionRuns.Add(run);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        try
        {
            var context = BuildDocumentContext(document);
            var result = await _openAiService.RunDocumentActionAsync(
                context,
                actionType,
                outputFormat,
                template.Prompt,
                language,
                cancellationToken);

            await PersistResultAsync(run, document, template, result, outputFormat, language, saveResult, cancellationToken);

            run.Status = "completed";
            run.CompletedAtUtc = DateTime.UtcNow;

            if (saveResult)
            {
                await _dbContext.SaveChangesAsync(cancellationToken);
            }

            await _usageTrackingService.TrackAsync(
                userId,
                usageType,
                1,
                cancellationToken,
                model: "gpt-4o-mini",
                referenceId: run.Id.ToString());
        }
        catch (Exception ex)
        {
            run.Status = "failed";
            run.ErrorMessage = ex.Message[..Math.Min(ex.Message.Length, 2000)];
            run.CompletedAtUtc = DateTime.UtcNow;

            if (saveResult)
            {
                await _dbContext.SaveChangesAsync(cancellationToken);
            }
        }

        return ToDto(run);
    }

    public async Task<List<AiActionRunDto>> GetDocumentRunsAsync(Guid documentId, CancellationToken cancellationToken)
    {
        var userId = _currentUserService.GetUserId();
        var exists = await _dbContext.Documents.AnyAsync(x => x.Id == documentId && x.UserId == userId, cancellationToken);
        if (!exists)
        {
            throw new NotFoundException("Document not found.");
        }

        var runs = await _dbContext.AiActionRuns
            .Where(x => x.UserId == userId && x.DocumentId == documentId)
            .OrderByDescending(x => x.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        return runs.Select(ToDto).ToList();
    }

    public async Task<AiActionRunDto> GetByIdAsync(Guid runId, CancellationToken cancellationToken)
    {
        var userId = _currentUserService.GetUserId();
        var run = await _dbContext.AiActionRuns
            .FirstOrDefaultAsync(x => x.Id == runId && x.UserId == userId, cancellationToken);

        return run is null ? throw new NotFoundException("AI action run not found.") : ToDto(run);
    }

    public async Task<AiActionRunDownloadDto> OpenDownloadAsync(Guid runId, CancellationToken cancellationToken)
    {
        var userId = _currentUserService.GetUserId();
        var run = await _dbContext.AiActionRuns
            .FirstOrDefaultAsync(x => x.Id == runId && x.UserId == userId, cancellationToken);

        if (run is null)
        {
            throw new NotFoundException("AI action run not found.");
        }

        if (string.IsNullOrWhiteSpace(run.ResultFilePath) || !File.Exists(run.ResultFilePath))
        {
            throw new NotFoundException("AI action result file not found.");
        }

        return new AiActionRunDownloadDto
        {
            Stream = await _fileStorageService.OpenReadAsync(run.ResultFilePath, cancellationToken),
            ContentType = string.IsNullOrWhiteSpace(run.ResultContentType) ? "application/octet-stream" : run.ResultContentType,
            FileName = string.IsNullOrWhiteSpace(run.ResultFileName) ? "ai-action-result" : run.ResultFileName
        };
    }

    private async Task PersistResultAsync(
        AiActionRun run,
        Document document,
        AiActionTemplate template,
        string result,
        string outputFormat,
        string? language,
        bool saveResult,
        CancellationToken cancellationToken)
    {
        switch (outputFormat)
        {
            case "json":
                run.ResultJson = NormalizeJson(result);
                break;
            case "pdf":
                run.ResultText = TruncateResult(result);
                if (saveResult)
                {
                    var pdfBytes = _pdfRenderer.RenderTextReport(template.Name, result, language);
                    await SaveFileAsync(run, pdfBytes, BuildSafeFileName(document, template, "pdf"), "application/pdf", cancellationToken);
                }
                break;
            case "html":
                run.ResultText = TruncateResult(result);
                if (saveResult)
                {
                    await SaveFileAsync(run, Encoding.UTF8.GetBytes(result), BuildSafeFileName(document, template, "html"), "text/html; charset=utf-8", cancellationToken);
                }
                break;
            default:
                run.ResultText = TruncateResult(result);
                break;
        }
    }

    private async Task SaveFileAsync(
        AiActionRun run,
        byte[] bytes,
        string fileName,
        string contentType,
        CancellationToken cancellationToken)
    {
        await using var stream = new MemoryStream(bytes);
        var storedFileName = $"ai-action-{run.Id:N}-{fileName}";
        run.ResultFilePath = await _fileStorageService.SaveAsync(stream, storedFileName, cancellationToken);
        run.ResultFileName = fileName;
        run.ResultContentType = contentType;
    }

    private static string BuildDocumentContext(Document document)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"File name: {document.OriginalFileName}");
        builder.AppendLine($"Content type: {document.ContentType}");
        if (document.Folder is not null)
        {
            builder.AppendLine($"Current folder: {document.Folder.Name}");
        }
        if (!string.IsNullOrWhiteSpace(document.QuickSummary)) builder.AppendLine($"Quick summary: {document.QuickSummary}");
        if (!string.IsNullOrWhiteSpace(document.Summary)) builder.AppendLine($"Summary: {document.Summary}");
        builder.AppendLine();
        builder.AppendLine("Document text:");
        builder.AppendLine(document.ExtractedText![..Math.Min(document.ExtractedText.Length, 30_000)]);
        return builder.ToString();
    }

    private static void EnsureDocumentReady(Document document)
    {
        if (document.Status != DocumentStatus.Ready || string.IsNullOrWhiteSpace(document.ExtractedText))
        {
            throw new BadRequestException("Document is not ready for AI actions yet.");
        }
    }

    private async Task ValidateTemplateAppliesToDocumentAsync(
        AiActionTemplate template,
        Document document,
        Guid userId,
        CancellationToken cancellationToken)
    {
        if (template.FolderId is not Guid templateFolderId)
        {
            return;
        }

        if (document.FolderId is not Guid documentFolderId)
        {
            throw new BadRequestException("ai_actions.error.template_folder_required");
        }

        if (documentFolderId == templateFolderId)
        {
            return;
        }

        var folders = await _dbContext.DocumentFolders
            .Where(x => x.UserId == userId)
            .Select(x => new { x.Id, x.ParentFolderId })
            .ToListAsync(cancellationToken);

        var current = folders.FirstOrDefault(x => x.Id == documentFolderId);
        var guard = 0;
        while (current is not null && guard++ < 16)
        {
            if (current.ParentFolderId == templateFolderId)
            {
                return;
            }

            current = current.ParentFolderId is Guid parentId
                ? folders.FirstOrDefault(x => x.Id == parentId)
                : null;
        }

        throw new BadRequestException("ai_actions.error.template_different_folder_tree");
    }

    private static string NormalizeJson(string result)
    {
        using var json = JsonDocument.Parse(result);
        return JsonSerializer.Serialize(json.RootElement, new JsonSerializerOptions { WriteIndented = true });
    }

    private static string NormalizeActionType(string? value)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? "custom" : value.Trim().ToLowerInvariant();
        normalized = normalized.Replace(" ", "-").Replace("_", "-");
        return SupportedActionTypes.Contains(normalized) ? normalized : "custom";
    }

    private static string NormalizeOutputFormat(string? value)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? "markdown" : value.Trim().ToLowerInvariant();
        normalized = normalized.Replace("_", "-");
        if (normalized == "plain-text") normalized = "text";
        if (normalized == "plaintext") normalized = "text";
        if (!SupportedOutputFormats.Contains(normalized))
        {
            throw new BadRequestException("Unsupported AI action output format. Supported: json, markdown, text, html, pdf.");
        }
        return normalized;
    }

    private static string? NormalizeLanguage(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var normalized = value.Trim().ToLowerInvariant();
        return normalized switch
        {
            "pl" or "en" or "ua" or "uk" => normalized,
            _ => normalized.Length <= 20 ? normalized : normalized[..20]
        };
    }

    private static UsageType ResolveUsageType(string actionType, string outputFormat)
        => actionType == "extraction" || outputFormat == "json"
            ? UsageType.ExtractDocument
            : UsageType.SummarizeDocument;

    private static string BuildSafeFileName(Document document, AiActionTemplate template, string extension)
    {
        var doc = Path.GetFileNameWithoutExtension(document.OriginalFileName);
        var baseName = $"{doc}-{template.Name}";
        var safe = new string(baseName.Select(ch => char.IsLetterOrDigit(ch) ? ch : '-').ToArray());
        safe = string.Join('-', safe.Split('-', StringSplitOptions.RemoveEmptyEntries)).ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(safe)) safe = "ai-action-result";
        if (safe.Length > 80) safe = safe[..80].Trim('-');
        return $"{safe}.{extension}";
    }

    private static string TruncateResult(string value)
        => string.IsNullOrWhiteSpace(value) ? string.Empty : value[..Math.Min(value.Length, 16_000)];

    private static AiActionRunDto ToDto(AiActionRun run)
    {
        return new AiActionRunDto
        {
            Id = run.Id,
            DocumentId = run.DocumentId,
            TemplateId = run.TemplateId,
            TemplateName = run.TemplateName,
            ActionType = run.ActionType,
            OutputFormat = run.OutputFormat,
            Language = run.Language,
            Status = run.Status,
            ResultText = run.ResultText,
            ResultJson = run.ResultJson,
            HasFile = !string.IsNullOrWhiteSpace(run.ResultFilePath),
            ResultFileName = run.ResultFileName,
            ResultContentType = run.ResultContentType,
            ErrorMessage = run.ErrorMessage,
            CreatedAtUtc = run.CreatedAtUtc,
            CompletedAtUtc = run.CompletedAtUtc
        };
    }
}
