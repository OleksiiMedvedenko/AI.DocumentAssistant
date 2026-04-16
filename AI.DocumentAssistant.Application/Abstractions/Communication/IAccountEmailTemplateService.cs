namespace AI.DocumentAssistant.Application.Abstractions.Communication
{
    public interface IAccountEmailTemplateService
    {
        (string Subject, string HtmlBody) BuildConfirmationEmail(string language, string confirmationUrl, int tokenLifetimeHours);
    }
}
