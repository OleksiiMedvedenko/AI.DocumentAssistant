using System.Net;
using AI.DocumentAssistant.Application.Abstractions.Communication;

namespace AI.DocumentAssistant.Application.Services.Communication;

public sealed class OrganizationInvitationEmailTemplateService : IOrganizationInvitationEmailTemplateService
{
    public (string Subject, string HtmlBody) BuildInvitationEmail(
        string language,
        string organizationName,
        string inviterEmail,
        string code,
        DateTime expiresAtUtc)
    {
        var normalizedLanguage = NormalizeLanguage(language);
        var safeOrganizationName = WebUtility.HtmlEncode(organizationName);
        var safeInviterEmail = WebUtility.HtmlEncode(inviterEmail);
        var safeCode = WebUtility.HtmlEncode(code);
        var expiresText = WebUtility.HtmlEncode(expiresAtUtc.ToString("yyyy-MM-dd HH:mm 'UTC'"));

        return normalizedLanguage switch
        {
            "pl" => (
                $"Zaproszenie do organizacji {organizationName}",
                $$"""
                <div style="font-family:Arial,sans-serif;font-size:14px;line-height:1.7;color:#111827;max-width:680px;margin:0 auto;padding:24px;background:#f9fafb">
                  <div style="background:#ffffff;border:1px solid #e5e7eb;border-radius:18px;padding:32px">
                    <h1 style="margin:0 0 16px;font-size:24px;color:#111827">Zaproszenie do organizacji</h1>
                    <p style="margin:0 0 14px">Użytkownik <strong>{{safeInviterEmail}}</strong> zaprosił Cię do organizacji <strong>{{safeOrganizationName}}</strong>.</p>
                    <p style="margin:0 0 14px">Zaloguj się do aplikacji i zaakceptuj zaproszenie jednym kliknięciem w module powiadomień.</p>
                    <p style="margin:0 0 14px">Kod poniżej jest rozwiązaniem awaryjnym, gdyby powiadomienie w aplikacji było niedostępne.</p>
                    <div style="font-size:22px;font-weight:700;letter-spacing:2px;background:#f3f4f6;border-radius:12px;padding:16px 20px;margin:18px 0;color:#111827">{{safeCode}}</div>
                    <p style="margin:0 0 14px;color:#6b7280">Kod wygasa: {{expiresText}}.</p>
                    <p style="margin:0;color:#6b7280">Jeżeli nie oczekiwałeś tego zaproszenia, zignoruj tę wiadomość.</p>
                  </div>
                </div>
                """),
            "ua" => (
                $"Запрошення до організації {organizationName}",
                $$"""
                <div style="font-family:Arial,sans-serif;font-size:14px;line-height:1.7;color:#111827;max-width:680px;margin:0 auto;padding:24px;background:#f9fafb">
                  <div style="background:#ffffff;border:1px solid #e5e7eb;border-radius:18px;padding:32px">
                    <h1 style="margin:0 0 16px;font-size:24px;color:#111827">Запрошення до організації</h1>
                    <p style="margin:0 0 14px">Користувач <strong>{{safeInviterEmail}}</strong> запросив вас до організації <strong>{{safeOrganizationName}}</strong>.</p>
                    <p style="margin:0 0 14px">Увійдіть у застосунок і прийміть запрошення одним кліком у модулі сповіщень.</p>
                    <p style="margin:0 0 14px">Код нижче є резервним варіантом, якщо сповіщення в застосунку недоступне.</p>
                    <div style="font-size:22px;font-weight:700;letter-spacing:2px;background:#f3f4f6;border-radius:12px;padding:16px 20px;margin:18px 0;color:#111827">{{safeCode}}</div>
                    <p style="margin:0 0 14px;color:#6b7280">Код дійсний до: {{expiresText}}.</p>
                    <p style="margin:0;color:#6b7280">Якщо ви не очікували цього запрошення, проігноруйте цей лист.</p>
                  </div>
                </div>
                """),
            _ => (
                $"Invitation to {organizationName}",
                $$"""
                <div style="font-family:Arial,sans-serif;font-size:14px;line-height:1.7;color:#111827;max-width:680px;margin:0 auto;padding:24px;background:#f9fafb">
                  <div style="background:#ffffff;border:1px solid #e5e7eb;border-radius:18px;padding:32px">
                    <h1 style="margin:0 0 16px;font-size:24px;color:#111827">Organization invitation</h1>
                    <p style="margin:0 0 14px"><strong>{{safeInviterEmail}}</strong> invited you to join <strong>{{safeOrganizationName}}</strong>.</p>
                    <p style="margin:0 0 14px">Sign in to the application and accept the invitation with one click in the notifications module.</p>
                    <p style="margin:0 0 14px">The code below is a fallback option in case the in-app notification is unavailable.</p>
                    <div style="font-size:22px;font-weight:700;letter-spacing:2px;background:#f3f4f6;border-radius:12px;padding:16px 20px;margin:18px 0;color:#111827">{{safeCode}}</div>
                    <p style="margin:0 0 14px;color:#6b7280">Code expires at: {{expiresText}}.</p>
                    <p style="margin:0;color:#6b7280">If you did not expect this invitation, you can ignore this email.</p>
                  </div>
                </div>
                """)
        };
    }

    private static string NormalizeLanguage(string? language)
    {
        if (string.IsNullOrWhiteSpace(language)) return "en";
        return language.Trim().ToLowerInvariant() switch
        {
            "pl" => "pl",
            "ua" => "ua",
            "uk" => "ua",
            "en" => "en",
            _ => "en"
        };
    }
}
