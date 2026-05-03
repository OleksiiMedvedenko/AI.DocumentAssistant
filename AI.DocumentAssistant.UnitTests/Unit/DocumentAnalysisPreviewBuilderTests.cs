using AI.DocumentAssistant.Application.Documents.Services;
using FluentAssertions;
using Xunit;

namespace AI.DocumentAssistant.UnitTests.Unit;

public sealed class DocumentAnalysisPreviewBuilderTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Build_Should_Return_Empty_String_For_Blank_Input(string? text)
    {
        var result = DocumentAnalysisPreviewBuilder.Build(text);

        result.Should().BeEmpty();
    }

    [Fact]
    public void Build_Should_Return_Trimmed_Text_When_It_Fits_Limit()
    {
        var result = DocumentAnalysisPreviewBuilder.Build("  short document  ");

        result.Should().Be("short document");
    }

    [Fact]
    public void Build_Should_Keep_Beginning_Middle_And_End_For_Long_Text()
    {
        var beginning = "BEGIN-IMPORTANT-SECTION";
        var middle = "MIDDLE-IMPORTANT-SECTION";
        var ending = "END-IMPORTANT-SECTION";
        var text = beginning
            + new string('a', 2400)
            + middle
            + new string('b', 2400)
            + ending;

        var result = DocumentAnalysisPreviewBuilder.Build(text);

        result.Should().Contain(beginning);
        result.Should().Contain(middle);
        result.Should().Contain(ending);
        result.Should().Contain("[...]");
        result.Length.Should().BeLessThan(3800);
    }
}
