namespace AI.DocumentAssistant.Application.Documents.Dtos;

public sealed class AiActionRunDownloadDto
{
    public Stream Stream { get; set; } = default!;
    public string ContentType { get; set; } = "application/octet-stream";
    public string FileName { get; set; } = "ai-action-result";
}
