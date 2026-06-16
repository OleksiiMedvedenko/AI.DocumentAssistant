using AI.DocumentAssistant.Application.Services.Communication;
using FluentAssertions;
using Xunit;

namespace AI.DocumentAssistant.UnitTests.Unit.Organizations;

public sealed class OrganizationInvitationEmailTemplateServiceTests
{
    private readonly OrganizationInvitationEmailTemplateService _sut = new();

    [Theory]
    [InlineData("en", "Organization invitation", "fallback option")]
    [InlineData("pl", "Zaproszenie do organizacji", "rozwiązaniem awaryjnym")]
    [InlineData("ua", "Запрошення до організації", "резервним варіантом")]
    [InlineData("uk", "Запрошення до організації", "резервним варіантом")]
    [InlineData("unknown", "Organization invitation", "fallback option")]
    public void BuildInvitationEmail_Should_Return_Localized_Template_And_Normalize_Language(
        string language,
        string expectedHeading,
        string expectedFallbackText)
    {
        var result = _sut.BuildInvitationEmail(
            language,
            "Acme <Org>",
            "manager@test.local",
            "ABCD-EFGH-2345",
            new DateTime(2026, 1, 2, 3, 4, 0, DateTimeKind.Utc));

        result.Subject.Should().NotBeNullOrWhiteSpace();
        result.HtmlBody.Should().Contain(expectedHeading);
        result.HtmlBody.Should().Contain(expectedFallbackText);
        result.HtmlBody.Should().Contain("ABCD-EFGH-2345");
        result.HtmlBody.Should().Contain("manager@test.local");
        result.HtmlBody.Should().Contain("Acme &lt;Org&gt;");
        result.HtmlBody.Should().Contain("2026-01-02 03:04 UTC");
    }
}
