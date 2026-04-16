using System.Net;
using AI.DocumentAssistant.Application.Abstractions.Communication;

namespace AI.DocumentAssistant.Application.Services.Communication;

public sealed class AccountEmailTemplateService : IAccountEmailTemplateService
{
    public (string Subject, string HtmlBody) BuildConfirmationEmail(string language, string confirmationUrl, int tokenLifetimeHours)
    {
        var normalizedLanguage = NormalizeLanguage(language);
        var encodedUrl = WebUtility.HtmlEncode(confirmationUrl);

        return normalizedLanguage switch
        {
            "pl" => (
                "Potwierdź swój adres e-mail",
                $$"""
                <div style="font-family:Arial,sans-serif;font-size:14px;line-height:1.7;color:#111827;max-width:640px;margin:0 auto;padding:24px;background:#f9fafb">
                  <div style="background:#ffffff;border:1px solid #e5e7eb;border-radius:18px;padding:32px">
                    <h1 style="margin:0 0 16px;font-size:24px;color:#111827">Aktywuj konto</h1>
                    <p style="margin:0 0 16px">Dziękujemy za rejestrację w AI Document Assistant.</p>
                    <p style="margin:0 0 24px">Aby aktywować konto, kliknij przycisk poniżej.</p>
                    <p style="margin:0 0 24px">
                      <a href="{{encodedUrl}}" style="display:inline-block;background:#111827;color:#ffffff;text-decoration:none;padding:14px 22px;border-radius:12px;font-weight:600">
                        Potwierdź adres e-mail
                      </a>
                    </p>
                    <p style="margin:0 0 12px">Jeżeli przycisk nie działa, użyj tego linku:</p>
                    <p style="margin:0 0 24px;word-break:break-word">
                      <a href="{{encodedUrl}}" style="color:#2563eb">{{encodedUrl}}</a>
                    </p>
                    <p style="margin:0;color:#6b7280">Link wygaśnie za {{tokenLifetimeHours}} godzin.</p>
                  </div>
                </div>
                """
            ),
            "ua" => (
                "Підтвердіть свою електронну адресу",
                $$"""
                <div style="font-family:Arial,sans-serif;font-size:14px;line-height:1.7;color:#111827;max-width:640px;margin:0 auto;padding:24px;background:#f9fafb">
                  <div style="background:#ffffff;border:1px solid #e5e7eb;border-radius:18px;padding:32px">
                    <h1 style="margin:0 0 16px;font-size:24px;color:#111827">Активуйте обліковий запис</h1>
                    <p style="margin:0 0 16px">Дякуємо за реєстрацію в AI Document Assistant.</p>
                    <p style="margin:0 0 24px">Щоб активувати обліковий запис, натисніть кнопку нижче.</p>
                    <p style="margin:0 0 24px">
                      <a href="{{encodedUrl}}" style="display:inline-block;background:#111827;color:#ffffff;text-decoration:none;padding:14px 22px;border-radius:12px;font-weight:600">
                        Підтвердити e-mail
                      </a>
                    </p>
                    <p style="margin:0 0 12px">Якщо кнопка не працює, скористайтеся цим посиланням:</p>
                    <p style="margin:0 0 24px;word-break:break-word">
                      <a href="{{encodedUrl}}" style="color:#2563eb">{{encodedUrl}}</a>
                    </p>
                    <p style="margin:0;color:#6b7280">Посилання дійсне {{tokenLifetimeHours}} годин.</p>
                  </div>
                </div>
                """
            ),
            _ => (
                "Confirm your email address",
                $$"""
                <div style="font-family:Arial,sans-serif;font-size:14px;line-height:1.7;color:#111827;max-width:640px;margin:0 auto;padding:24px;background:#f9fafb">
                  <div style="background:#ffffff;border:1px solid #e5e7eb;border-radius:18px;padding:32px">
                    <h1 style="margin:0 0 16px;font-size:24px;color:#111827">Activate your account</h1>
                    <p style="margin:0 0 16px">Thanks for registering in AI Document Assistant.</p>
                    <p style="margin:0 0 24px">To activate your account, click the button below.</p>
                    <p style="margin:0 0 24px">
                      <a href="{{encodedUrl}}" style="display:inline-block;background:#111827;color:#ffffff;text-decoration:none;padding:14px 22px;border-radius:12px;font-weight:600">
                        Confirm email
                      </a>
                    </p>
                    <p style="margin:0 0 12px">If the button does not work, use this link:</p>
                    <p style="margin:0 0 24px;word-break:break-word">
                      <a href="{{encodedUrl}}" style="color:#2563eb">{{encodedUrl}}</a>
                    </p>
                    <p style="margin:0;color:#6b7280">This link expires in {{tokenLifetimeHours}} hours.</p>
                  </div>
                </div>
                """
            )
        };
    }

    private static string NormalizeLanguage(string? language)
    {
        if (string.IsNullOrWhiteSpace(language))
        {
            return "en";
        }

        var normalized = language.Trim().ToLowerInvariant();

        return normalized switch
        {
            "pl" => "pl",
            "ua" => "ua",
            "uk" => "ua",
            "en" => "en",
            _ => "en"
        };
    }
}