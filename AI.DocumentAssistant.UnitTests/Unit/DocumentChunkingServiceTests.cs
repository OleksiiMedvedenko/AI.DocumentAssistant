using AI.DocumentAssistant.Application.Services.DocumentProcessing;
using FluentAssertions;
using Xunit;

namespace AI.DocumentAssistant.UnitTests.Unit;

public sealed class DocumentChunkingServiceTests
{
    private readonly DocumentChunkingService _sut = new();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   \r\n   ")]
    public void Chunk_Should_Return_Empty_List_For_Blank_Text(string? text)
    {
        var result = _sut.Chunk(text!);

        result.Should().BeEmpty();
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(-1, 0)]
    public void Chunk_Should_Reject_Non_Positive_Chunk_Size(int chunkSize, int overlap)
    {
        var act = () => _sut.Chunk("text", chunkSize, overlap);

        act.Should().Throw<ArgumentOutOfRangeException>()
            .Where(x => x.ParamName == "chunkSize");
    }

    [Theory]
    [InlineData(100, -1)]
    [InlineData(100, 100)]
    [InlineData(100, 120)]
    public void Chunk_Should_Reject_Invalid_Overlap(int chunkSize, int overlap)
    {
        var act = () => _sut.Chunk("text", chunkSize, overlap);

        act.Should().Throw<ArgumentOutOfRangeException>()
            .Where(x => x.ParamName == "overlap");
    }

    [Fact]
    public void Chunk_Should_Normalize_Whitespace_And_Preserve_Paragraphs_When_Text_Fits()
    {
        var text = "  First    paragraph.  \r\n\r\n\tSecond     paragraph.  ";

        var result = _sut.Chunk(text, chunkSize: 200, overlap: 20);

        var chunk = result.Should().ContainSingle().Which.Replace("\r\n", "\n");
        chunk.Should().Be("First paragraph.\n\nSecond paragraph.");
    }

    [Fact]
    public void Chunk_Should_Create_Bounded_Chunks_With_Context_Overlap()
    {
        var paragraphs = Enumerable.Range(1, 8)
            .Select(i => $"Paragraph {i} contains enough content to force multiple chunks while keeping readable boundaries.");
        var text = string.Join("\n\n", paragraphs);

        var result = _sut.Chunk(text, chunkSize: 170, overlap: 35);

        result.Should().HaveCountGreaterThan(1);
        result.Should().OnlyContain(x => x.Length <= 170);
        result[1].Should().Contain("Paragraph");
    }

    [Fact]
    public void Chunk_Should_Split_Large_Paragraph_Without_Losing_Content()
    {
        var sentence = "This is a long sentence used for deterministic chunking. ";
        var text = string.Concat(Enumerable.Repeat(sentence, 20));

        var result = _sut.Chunk(text, chunkSize: 180, overlap: 30);

        result.Should().HaveCountGreaterThan(1);
        result.Should().OnlyContain(x => x.Length <= 180);
        string.Concat(result).Should().Contain("deterministic chunking");
    }
}
