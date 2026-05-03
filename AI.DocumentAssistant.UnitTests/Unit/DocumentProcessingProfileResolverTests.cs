using AI.DocumentAssistant.Application.Documents.Services;
using AI.DocumentAssistant.Domain.Enums;
using FluentAssertions;
using Xunit;

namespace AI.DocumentAssistant.UnitTests.Unit;

public sealed class DocumentProcessingProfileResolverTests
{
    [Theory]
    [InlineData("john-doe-cv.pdf")]
    [InlineData("senior-backend-resume.docx")]
    [InlineData("curriculum-vitae.txt")]
    [InlineData("życiorys-kandydata.pdf")]
    [InlineData("резюме-кандидата.pdf")]
    public void Resolve_Should_Use_High_Accuracy_Profile_For_Cv_Like_File_Names(string fileName)
    {
        var result = DocumentProcessingProfileResolver.Resolve(fileName, "application/pdf");

        result.Should().Be(DocumentProcessingProfile.HighAccuracyCv);
    }

    [Theory]
    [InlineData("invoice.pdf", "application/pdf")]
    [InlineData("contract.docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document")]
    [InlineData("unknown.bin", "application/msword")]
    public void Resolve_Should_Use_Standard_Profile_For_Pdf_And_Word_Documents(string fileName, string contentType)
    {
        var result = DocumentProcessingProfileResolver.Resolve(fileName, contentType);

        result.Should().Be(DocumentProcessingProfile.Standard);
    }

    [Theory]
    [InlineData("notes.txt", "text/plain")]
    [InlineData("data.json", "application/json")]
    [InlineData("audit.log", null)]
    public void Resolve_Should_Use_Fast_Classification_For_Lightweight_Text_Files(string fileName, string? contentType)
    {
        var result = DocumentProcessingProfileResolver.Resolve(fileName, contentType);

        result.Should().Be(DocumentProcessingProfile.FastClassification);
    }
}
