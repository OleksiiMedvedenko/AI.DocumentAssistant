using AI.DocumentAssistant.Domain.Enums;

namespace AI.DocumentAssistant.Application.Documents.Services;

public static class DocumentProcessingProfileResolver
{
    public static DocumentProcessingProfile Resolve(string fileName, string? contentType)
    {
        var lowerName = fileName.ToLowerInvariant();
        var lowerType = (contentType ?? string.Empty).ToLowerInvariant();

        if (LooksLikeCv(lowerName))
        {
            return DocumentProcessingProfile.HighAccuracyCv;
        }

        if (lowerType.Contains("pdf")
            || lowerType.Contains("word")
            || lowerName.EndsWith(".pdf")
            || lowerName.EndsWith(".docx"))
        {
            return DocumentProcessingProfile.Standard;
        }

        return DocumentProcessingProfile.FastClassification;
    }

    private static bool LooksLikeCv(string fileName)
    {
        return fileName.Contains("cv")
            || fileName.Contains("resume")
            || fileName.Contains("curriculum")
            || fileName.Contains("życiorys")
            || fileName.Contains("rezume")
            || fileName.Contains("резюме");
    }
}