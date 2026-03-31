namespace AI.DocumentAssistant.Application.Services.Storage
{
    public sealed class LocalStorageOptions
    {
        public const string SectionName = "LocalStorage";
        public string RootPath { get; set; } = "storage";
    }
}
