using FluentAssertions;
using Tempo.Blazor.AI.Markdown;

namespace Tempo.Blazor.Tests.Components.AI;

/// <summary>
/// Pins <see cref="AiMessageText"/> — the plain-text projection behind <c>TmAiMessage</c>'s Copy
/// action. A copied answer must read like the answer, not like its source: no <c>**</c>, no
/// <c>#</c>, no fences, no <c>[]()</c>, while the block structure (paragraphs, list items, code and
/// table rows) survives as blank lines and one line per item — a copy button that flattens a whole
/// answer into one line would be worse than copying the raw markdown.
/// </summary>
public sealed class AiMessageTextTests
{
    [Fact]
    public void NullOrWhitespace_ProjectsToAnEmptyString()
    {
        AiMessageText.ToPlainText(null).Should().BeEmpty();
        AiMessageText.ToPlainText(string.Empty).Should().BeEmpty();
        AiMessageText.ToPlainText("   \n\t ").Should().BeEmpty();
    }

    [Fact]
    public void PlainParagraph_IsReturnedAsIs()
    {
        AiMessageText.ToPlainText("Just a sentence.").Should().Be("Just a sentence.");
    }

    [Fact]
    public void HeadingsLoseTheirMarkers_AndStayOnTheirOwnLines()
    {
        var text = AiMessageText.ToPlainText("# Title\n\nIntro paragraph.\n\n## Section");

        text.Should().Be("Title\n\nIntro paragraph.\n\nSection");
    }

    [Fact]
    public void EmphasisAndStrikethroughLoseTheirMarkers_ButKeepTheirText()
    {
        AiMessageText.ToPlainText("Some **bold**, *italic*, ~~struck~~ and `inline code` text.")
            .Should().Be("Some bold, italic, struck and inline code text.");
    }

    [Fact]
    public void LinksBecomeTheirLabel_AndCitationsKeepTheirNumber()
    {
        var text = AiMessageText.ToPlainText("See [the docs](https://example.test/docs) and [2](cite:src-2).");

        text.Should().Be("See the docs and 2.");
    }

    [Fact]
    public void ImagesBecomeTheirAltText()
    {
        AiMessageText.ToPlainText("Before ![a chart](https://example.test/chart.png) after.")
            .Should().Be("Before a chart after.");
    }

    [Fact]
    public void FencedCodeBlock_KeepsTheCodeWithoutTheFence()
    {
        var text = AiMessageText.ToPlainText("Run this:\n\n```csharp\nvar x = 1;\n```\n\nThen stop.");

        text.Should().Be("Run this:\n\nvar x = 1;\n\nThen stop.");
    }

    [Fact]
    public void UnorderedListItems_GetAPlainBullet_AndOneLineEach()
    {
        var text = AiMessageText.ToPlainText("- one\n- two\n  - nested");

        text.Should().Be("• one\n• two\n  • nested");
    }

    [Fact]
    public void OrderedListItems_KeepTheirNumbers()
    {
        var text = AiMessageText.ToPlainText("1. first\n2. second");

        text.Should().Be("1. first\n2. second");
    }

    [Fact]
    public void BlockquoteLosesItsMarker()
    {
        AiMessageText.ToPlainText("> quoted words").Should().Be("quoted words");
    }

    [Fact]
    public void TableRows_BecomeOneLinePerRowWithPipedCells()
    {
        var text = AiMessageText.ToPlainText("| Name | Value |\n| --- | --- |\n| a | 1 |\n| b | 2 |");

        text.Should().Be("Name | Value\na | 1\nb | 2");
    }

    [Fact]
    public void RawHtmlIsCopiedAsTheTextTheUserSees_NotAsMarkupThatWouldRunAnywhere()
    {
        var text = AiMessageText.ToPlainText("<script>alert(1)</script>");

        text.Should().Be("<script>alert(1)</script>");
    }

    [Fact]
    public void StructureSurvives_AWholeAnswerReadsAsAnOutline()
    {
        var text = AiMessageText.ToPlainText(
            "## Answer\n\nUse the **fast** path:\n\n- measure\n- cache\n\n```cs\nvar y = 2;\n```");

        text.Should().Be("Answer\n\nUse the fast path:\n\n• measure\n• cache\n\nvar y = 2;");
    }

    [Fact]
    public void HardLineBreakInsideAParagraph_StaysALineBreak()
    {
        AiMessageText.ToPlainText("first  \nsecond").Should().Be("first\nsecond");
    }
}
