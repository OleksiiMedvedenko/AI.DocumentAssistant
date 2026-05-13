namespace AI.DocumentAssistant.Application.Abstractions.Documents;

public interface IAiActionPdfRenderer
{
    byte[] RenderTextReport(string title, string content, string? language);
}
