namespace AI.DocumentAssistant.Application.Abstractions.Communication;

public interface IOrganizationInvitationEmailTemplateService
{
    (string Subject, string HtmlBody) BuildInvitationEmail(
        string language,
        string organizationName,
        string inviterEmail,
        string code,
        DateTime expiresAtUtc);
}
