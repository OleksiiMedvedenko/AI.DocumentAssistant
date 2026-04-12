using System.Text;
using AI.DocumentAssistant.Application.Abstractions.AI;
using AI.DocumentAssistant.Application.Abstractions.Chats;
using AI.DocumentAssistant.Application.Abstractions.Common;
using AI.DocumentAssistant.Application.Abstractions.Usage;
using AI.DocumentAssistant.Application.Common.Exceptions;
using AI.DocumentAssistant.Application.Documents.Dtos;
using AI.DocumentAssistant.Domain.Entities;
using AI.DocumentAssistant.Domain.Enums;
using AI.DocumentAssistant.Infrastructure.Persistence.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AI.DocumentAssistant.Application.Chats.Services;

public sealed class ChatService : IChatService
{
    private readonly IApplicationDbContext _dbContext;
    private readonly ICurrentUserService _currentUserService;
    private readonly IOpenAiService _openAiService;
    private readonly IChunkRetrievalService _chunkRetrievalService;
    private readonly IUsageQuotaService _usageQuotaService;
    private readonly IUsageTrackingService _usageTrackingService;
    private readonly ChatRetrievalOptions _retrievalOptions;

    public ChatService(
        IApplicationDbContext dbContext,
        ICurrentUserService currentUserService,
        IOpenAiService openAiService,
        IChunkRetrievalService chunkRetrievalService,
        IUsageQuotaService usageQuotaService,
        IUsageTrackingService usageTrackingService,
        IOptions<ChatRetrievalOptions> retrievalOptions)
    {
        _dbContext = dbContext;
        _currentUserService = currentUserService;
        _openAiService = openAiService;
        _chunkRetrievalService = chunkRetrievalService;
        _usageQuotaService = usageQuotaService;
        _usageTrackingService = usageTrackingService;
        _retrievalOptions = retrievalOptions.Value;
    }

    public async Task<AskDocumentResultDto> AskAsync(
        Guid documentId,
        AskDocumentDto dto,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(dto.Message))
        {
            throw new BadRequestException("Message is required.");
        }

        var normalizedMessage = dto.Message.Trim();
        var userId = _currentUserService.GetUserId();

        var document = await _dbContext.Documents
            .Include(x => x.Chunks)
            .FirstOrDefaultAsync(x => x.Id == documentId && x.UserId == userId, cancellationToken);

        if (document is null)
        {
            throw new NotFoundException("Document not found.");
        }

        if (string.IsNullOrWhiteSpace(document.ExtractedText))
        {
            throw new BadRequestException("Document has not been processed yet.");
        }

        ChatSession? session = null;

        if (dto.ChatSessionId.HasValue)
        {
            session = await _dbContext.ChatSessions
                .Include(x => x.Messages)
                .FirstOrDefaultAsync(
                    x => x.Id == dto.ChatSessionId.Value &&
                         x.DocumentId == documentId &&
                         x.UserId == userId,
                    cancellationToken);

            if (session is null)
            {
                throw new NotFoundException("Chat session not found.");
            }
        }

        if (session is null)
        {
            session = new ChatSession
            {
                Id = Guid.NewGuid(),
                DocumentId = documentId,
                UserId = userId,
                CreatedAtUtc = DateTime.UtcNow
            };

            await _dbContext.ChatSessions.AddAsync(session, cancellationToken);
        }

        var priorUserMessages = session.Messages?
            .Where(x => x.Role == ChatRole.User)
            .OrderByDescending(x => x.CreatedAtUtc)
            .Take(_retrievalOptions.HistoryMessagesToUse)
            .Select(x => x.Content.Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Reverse()
            .ToList()
            ?? new List<string>();

        var orderedChunks = document.Chunks
            .OrderBy(x => x.ChunkIndex)
            .ToList();

        var bestChunks = await _chunkRetrievalService.GetBestMatchingChunksAsync(
            orderedChunks,
            normalizedMessage,
            priorUserMessages,
            _retrievalOptions.DefaultTake,
            cancellationToken);

        var context = BuildContext(
            bestChunks,
            document.ExtractedText!,
            normalizedMessage,
            _retrievalOptions.MaxContextCharacters);

        await _usageQuotaService.EnsureWithinQuotaAsync(
            userId,
            UsageType.ChatMessage,
            1,
            cancellationToken);

        var userMessage = new ChatMessage
        {
            Id = Guid.NewGuid(),
            ChatSessionId = session.Id,
            Role = ChatRole.User,
            Content = normalizedMessage,
            CreatedAtUtc = DateTime.UtcNow
        };

        var recentUserQuestions = session.Messages?
            .Where(x => x.Role == ChatRole.User)
            .OrderByDescending(x => x.CreatedAtUtc)
            .Take(4)
            .OrderBy(x => x.CreatedAtUtc)
            .Select(x => x.Content.Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToList()
            ?? new List<string>();

        await _dbContext.ChatMessages.AddAsync(userMessage, cancellationToken);

        var enrichedContext = BuildPromptContext(
            context,
            recentUserQuestions,
            normalizedMessage,
            document.ExtractedText!);

        var answer = await _openAiService.AnswerQuestionAsync(
            enrichedContext,
            normalizedMessage,
            dto.Language,
            cancellationToken);

        var assistantMessage = new ChatMessage
        {
            Id = Guid.NewGuid(),
            ChatSessionId = session.Id,
            Role = ChatRole.Assistant,
            Content = answer,
            CreatedAtUtc = DateTime.UtcNow
        };

        await _dbContext.ChatMessages.AddAsync(assistantMessage, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _usageTrackingService.TrackAsync(
            userId,
            UsageType.ChatMessage,
            1,
            cancellationToken,
            model: "gpt-4o-mini",
            referenceId: session.Id.ToString());

        return new AskDocumentResultDto
        {
            ChatSessionId = session.Id,
            Answer = answer
        };
    }

    public async Task<IReadOnlyList<ChatMessageDto>> GetMessagesAsync(
        Guid documentId,
        Guid chatSessionId,
        CancellationToken cancellationToken)
    {
        var userId = _currentUserService.GetUserId();

        var sessionExists = await _dbContext.ChatSessions
            .AnyAsync(
                x => x.Id == chatSessionId &&
                     x.DocumentId == documentId &&
                     x.UserId == userId,
                cancellationToken);

        if (!sessionExists)
        {
            throw new NotFoundException("Chat session not found.");
        }

        return await _dbContext.ChatMessages
            .Where(x => x.ChatSessionId == chatSessionId)
            .OrderBy(x => x.CreatedAtUtc)
            .Select(x => new ChatMessageDto
            {
                Id = x.Id,
                Role = x.Role,
                Content = x.Content,
                CreatedAtUtc = x.CreatedAtUtc
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ChatSessionDto>> GetSessionsAsync(
        Guid documentId,
        CancellationToken cancellationToken)
    {
        var userId = _currentUserService.GetUserId();

        var documentExists = await _dbContext.Documents
            .AnyAsync(x => x.Id == documentId && x.UserId == userId, cancellationToken);

        if (!documentExists)
        {
            throw new NotFoundException("Document not found.");
        }

        var sessions = await _dbContext.ChatSessions
            .Where(x => x.DocumentId == documentId && x.UserId == userId)
            .Select(x => new ChatSessionDto
            {
                Id = x.Id,
                DocumentId = (Guid)x.DocumentId,
                CreatedAtUtc = x.CreatedAtUtc,
                LastMessageAtUtc = x.Messages
                    .OrderByDescending(m => m.CreatedAtUtc)
                    .Select(m => m.CreatedAtUtc)
                    .FirstOrDefault(),
                MessageCount = x.Messages.Count(),
                Title = x.Messages
                    .OrderBy(m => m.CreatedAtUtc)
                    .Where(m => m.Role == ChatRole.User)
                    .Select(m => m.Content)
                    .FirstOrDefault() ?? "New chat"
            })
            .OrderByDescending(x => x.LastMessageAtUtc)
            .ThenByDescending(x => x.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        foreach (var session in sessions)
        {
            if (session.LastMessageAtUtc == default)
            {
                session.LastMessageAtUtc = session.CreatedAtUtc;
            }

            session.Title = Truncate(session.Title, 80);
        }

        return sessions;
    }

    private static string BuildContext(
        IReadOnlyList<DocumentChunk> chunks,
        string fallbackText,
        string question,
        int maxCharacters)
    {
        if (maxCharacters <= 0)
        {
            maxCharacters = 12_000;
        }

        var intent = DetectQuestionIntent(question, fallbackText);

        if (intent is QuestionIntent.BroadOverview
            or QuestionIntent.DocumentType
            or QuestionIntent.CandidateProfile)
        {
            return BuildOverviewContext(chunks, fallbackText, maxCharacters);
        }

        var selectedChunkTexts = chunks
            .OrderBy(x => x.ChunkIndex)
            .Select(x => x.Text?.Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (selectedChunkTexts.Count == 0)
        {
            return TrimToBoundary(fallbackText, Math.Min(maxCharacters, 4000));
        }

        const string separator = "\n\n---\n\n";
        var parts = new List<string>();
        var currentLength = 0;

        foreach (var chunkText in selectedChunkTexts)
        {
            var block = $"[chunk]\n{chunkText}";
            var nextLength = parts.Count == 0
                ? block.Length
                : separator.Length + block.Length;

            if (currentLength + nextLength > maxCharacters)
            {
                break;
            }

            parts.Add(block);
            currentLength += nextLength;
        }

        return parts.Count > 0
            ? string.Join(separator, parts)
            : TrimToBoundary(fallbackText, Math.Min(maxCharacters, 4000));
    }

    private static string BuildOverviewContext(
        IReadOnlyList<DocumentChunk> chunks,
        string fallbackText,
        int maxCharacters)
    {
        var selected = chunks
            .OrderBy(x => x.ChunkIndex)
            .Select(x => x.Text?.Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Take(8)
            .ToList();

        if (selected.Count == 0)
        {
            return TrimToBoundary(fallbackText, Math.Min(maxCharacters, 4000));
        }

        const string separator = "\n\n---\n\n";
        var content = string.Join(separator, selected.Select(x => $"[overview-chunk]\n{x}"));
        return TrimToBoundary(content, maxCharacters);
    }



    private static string TrimToBoundary(string value, int maxCharacters)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var trimmed = value.Trim();
        if (trimmed.Length <= maxCharacters)
        {
            return trimmed;
        }

        var candidate = trimmed[..maxCharacters];
        var lastBoundary = Math.Max(
            candidate.LastIndexOf('\n'),
            Math.Max(candidate.LastIndexOf('.'), candidate.LastIndexOf(' ')));

        if (lastBoundary > maxCharacters / 2)
        {
            candidate = candidate[..lastBoundary];
        }

        return candidate.TrimEnd();
    }

    private static string Truncate(string value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "New chat";
        }

        return value.Length <= maxLength
            ? value
            : value[..maxLength].TrimEnd() + "...";
    }

    private static string BuildPromptContext(
    string documentContext,
    IReadOnlyList<string> recentUserQuestions,
    string currentQuestion,
    string fullDocumentText)
    {
        var sb = new StringBuilder();
        var intent = DetectQuestionIntent(currentQuestion, fullDocumentText);
        var looksLikeCv = LooksLikeResumeOrCv(fullDocumentText);

        sb.AppendLine("INSTRUCTIONS:");
        sb.AppendLine("- Answer only from the document evidence and the user's question.");
        sb.AppendLine("- Do not invent facts that are not supported by the document.");
        sb.AppendLine("- If the document is a CV/resume, explicitly treat it as a candidate profile.");
        sb.AppendLine("- For document-type questions, first identify what kind of document it is, then explain briefly why.");
        sb.AppendLine("- For candidate questions, focus on the candidate's experience, skills, technologies, roles, projects, and education only if supported by the document.");
        sb.AppendLine("- If the answer is uncertain, say so clearly.");
        sb.AppendLine();

        sb.AppendLine("DOCUMENT ANALYSIS HINTS:");

        if (looksLikeCv)
        {
            sb.AppendLine("- The document strongly resembles a CV/resume.");
            sb.AppendLine("- Prefer interpreting role names, technologies, project descriptions, employment periods, education, and skills as parts of a candidate profile.");
        }
        else
        {
            sb.AppendLine("- The document is not confidently recognized as a CV/resume.");
        }

        switch (intent)
        {
            case QuestionIntent.DocumentType:
                sb.AppendLine("- The user is asking what kind of document this is.");
                sb.AppendLine("- Start by identifying the document type, e.g. CV/resume, invoice, contract, report, specification, etc.");
                break;

            case QuestionIntent.CandidateProfile:
                sb.AppendLine("- The user is asking about the candidate/person described in the document.");
                sb.AppendLine("- Summarize the person, role, seniority, skills, technologies, domains, and relevant experience.");
                break;

            case QuestionIntent.BroadOverview:
                sb.AppendLine("- The user is asking for a general overview or summary.");
                break;

            default:
                sb.AppendLine("- The user is asking a specific question.");
                break;
        }

        sb.AppendLine();

        if (recentUserQuestions.Count > 0)
        {
            sb.AppendLine("RECENT USER QUESTIONS:");
            foreach (var question in recentUserQuestions)
            {
                sb.AppendLine($"- {question}");
            }

            sb.AppendLine();
        }

        sb.AppendLine("CURRENT QUESTION:");
        sb.AppendLine(currentQuestion);
        sb.AppendLine();

        sb.AppendLine("DOCUMENT EVIDENCE:");
        sb.AppendLine(documentContext);

        return sb.ToString().Trim();
    }

    private enum QuestionIntent
    {
        Specific = 0,
        BroadOverview = 1,
        DocumentType = 2,
        CandidateProfile = 3
    }

    private static QuestionIntent DetectQuestionIntent(string question, string fullDocumentText)
    {
        if (string.IsNullOrWhiteSpace(question))
        {
            return QuestionIntent.Specific;
        }

        var normalized = NormalizeForIntentDetection(question);

        if (ContainsAnyPhrase(normalized, DocumentTypePhrases))
        {
            return QuestionIntent.DocumentType;
        }

        if (ContainsAnyPhrase(normalized, CandidateProfilePhrases))
        {
            return QuestionIntent.CandidateProfile;
        }

        if (ContainsAnyPhrase(normalized, BroadOverviewPhrases))
        {
            return QuestionIntent.BroadOverview;
        }

        // Dodatkowa heurystyka:
        // jeżeli dokument wygląda jak CV, a pytanie brzmi jak pytanie o osobę,
        // traktujemy to jako CandidateProfile.
        if (LooksLikeResumeOrCv(fullDocumentText) && LooksLikePersonQuestion(normalized))
        {
            return QuestionIntent.CandidateProfile;
        }

        return QuestionIntent.Specific;
    }

    private static bool LooksLikePersonQuestion(string normalizedQuestion)
    {
        return ContainsAnyPhrase(normalizedQuestion, new[]
        {
        "who is the candidate",
        "who is this candidate",
        "tell me about the candidate",
        "describe the candidate",
        "who is this person",
        "tell me about this person",
        "describe this person",

        "kim jest kandydat",
        "kim jest ten kandydat",
        "opisz kandydata",
        "powiedz o kandydacie",
        "kim jest ta osoba",
        "opisz te osobe",

        "хто такий кандидат",
        "хто цей кандидат",
        "опиши кандидата",
        "розкажи про кандидата",
        "хто ця людина",

        "кто такой кандидат",
        "кто этот кандидат",
        "опиши кандидата",
        "расскажи о кандидате",
        "кто этот человек",

        "wer ist der kandidat",
        "beschreibe den kandidaten",
        "wer ist diese person",

        "qui est le candidat",
        "decris le candidat",
        "parle moi du candidat",

        "quien es el candidato",
        "describe al candidato",
        "hablame del candidato",

        "chi e il candidato",
        "descrivi il candidato",
        "parlami del candidato",

        "quem e o candidato",
        "descreva o candidato",
        "fale sobre o candidato"
    });
    }

    private static bool LooksLikeResumeOrCv(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var normalized = NormalizeForIntentDetection(text);

        var score = 0;

        score += CountMatches(normalized, new[]
        {
        "curriculum vitae",
        "resume",
        "cv",
        "work experience",
        "professional experience",
        "employment history",
        "experience",
        "education",
        "skills",
        "technical skills",
        "projects",
        "certifications",
        "summary",
        "profile",
        "linkedin",

        "doswiadczenie zawodowe",
        "doswiadczenie",
        "wyksztalcenie",
        "umiejetnosci",
        "technologie",
        "projekty",
        "certyfikaty",
        "profil",
        "podsumowanie",

        "досвід роботи",
        "досвід",
        "освіта",
        "навички",
        "технології",
        "проєкти",
        "проекти",
        "сертифікати",
        "профіль",

        "опыт работы",
        "опыт",
        "образование",
        "навыки",
        "технологии",
        "проекты",
        "сертификаты",
        "профиль",

        "berufserfahrung",
        "ausbildung",
        "kenntnisse",
        "fahigkeiten",
        "projekte",
        "profil",

        "experience professionnelle",
        "formation",
        "competences",
        "projets",
        "profil",

        "experiencia laboral",
        "educacion",
        "habilidades",
        "competencias",
        "proyectos",
        "perfil",

        "esperienza lavorativa",
        "istruzione",
        "competenze",
        "progetti",
        "profilo",

        "experiencia profissional",
        "formacao",
        "habilidades",
        "competencias",
        "projetos",
        "perfil"
    });

        score += CountMatches(normalized, new[]
        {
        "asp.net",
        ".net",
        "c#",
        "react",
        "flutter",
        "sql",
        "rest api",
        "backend",
        "frontend",
        "full stack",
        "fullstack",
        "developer",
        "engineer",
        "software engineer"
    });

        return score >= 4;
    }

    private static int CountMatches(string normalizedText, IEnumerable<string> phrases)
    {
        var count = 0;

        foreach (var phrase in phrases)
        {
            if (normalizedText.Contains(phrase, StringComparison.Ordinal))
            {
                count++;
            }
        }

        return count;
    }

    private static bool ContainsAnyPhrase(string normalizedText, IEnumerable<string> phrases)
    {
        foreach (var phrase in phrases)
        {
            if (normalizedText.Contains(phrase, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static string NormalizeForIntentDetection(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = value
            .Trim()
            .ToLowerInvariant()
            .Normalize(System.Text.NormalizationForm.FormD);

        var sb = new StringBuilder(normalized.Length);

        foreach (var ch in normalized)
        {
            var unicodeCategory = System.Globalization.CharUnicodeInfo.GetUnicodeCategory(ch);

            if (unicodeCategory == System.Globalization.UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsLetterOrDigit(ch) || char.IsWhiteSpace(ch))
            {
                sb.Append(ch);
            }
            else
            {
                sb.Append(' ');
            }
        }

        var cleaned = sb.ToString();

        while (cleaned.Contains("  ", StringComparison.Ordinal))
        {
            cleaned = cleaned.Replace("  ", " ", StringComparison.Ordinal);
        }

        return cleaned.Trim();
    }

    private static readonly string[] BroadOverviewPhrases =
    [
        "summarize",
    "summary",
    "main points",
    "key points",
    "overall",
    "in general",
    "general overview",
    "what is this about",
    "what is this document about",
    "give me an overview",
    "brief overview",

    "podsumuj",
    "podsumowanie",
    "najwazniejsze",
    "glowne punkty",
    "kluczowe punkty",
    "ogolnie",
    "w skrocie",
    "o czym jest",
    "daj przeglad",
    "krotkie podsumowanie",

    "підсумуй",
    "підсумок",
    "загалом",
    "коротко",
    "про що цей документ",
    "огляд",
    "основні моменти",
    ];

    private static readonly string[] DocumentTypePhrases =
    [
        "what is this document",
    "what kind of document is this",
    "what type of document is this",
    "identify this document",
    "is this a cv",
    "is this resume",
    "is this a resume",
    "is this curriculum vitae",
    "what document is this",

    "co to jest za dokument",
    "jaki to dokument",
    "jakiego typu to dokument",
    "okresl typ dokumentu",
    "czy to jest cv",
    "czy to cv",
    "czy to zyciorys",
    "czy to resume",

    "що це за документ",
    "який це документ",
    "який тип документа",
    "визнач тип документа",
    "чи це cv",
    "чи це резюме",
    ];

    private static readonly string[] CandidateProfilePhrases =
    [
        "tell me about the candidate",
    "describe the candidate",
    "who is the candidate",
    "who is this candidate",
    "what is the candidate",
    "what does the candidate do",
    "candidate profile",
    "about the candidate",

    "powiedz o kandydacie",
    "opisz kandydata",
    "kim jest kandydat",
    "kim jest ten kandydat",
    "co to za kandydat",
    "czym zajmuje sie kandydat",
    "profil kandydata",
    "o kandydacie",

    "розкажи про кандидата",
    "опиши кандидата",
    "хто такий кандидат",
    "хто цей кандидат",
    "чим займається кандидат",
    "профіль кандидата",
    ];
}
