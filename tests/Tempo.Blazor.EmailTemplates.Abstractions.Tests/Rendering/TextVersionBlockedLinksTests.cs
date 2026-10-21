using Tempo.Blazor.EmailTemplates.Abstractions.Model;
using Tempo.Blazor.EmailTemplates.Abstractions.Model.Blocks;
using Tempo.Blazor.EmailTemplates.Abstractions.Rendering;

namespace Tempo.Blazor.EmailTemplates.Abstractions.Tests.Rendering;

/// <summary>
/// Fáze 9 review — the plain-text alternative never advertises a link the message itself refuses. A
/// <c>javascript:</c>/<c>data:</c>/network-path target is dropped from the <c>text (url)</c> pair exactly
/// where the markup replaces it with <c>#</c> (§3.7a), so the two versions of the same document agree on
/// which links a reader can follow.
/// </summary>
public class TextVersionBlockedLinksTests
{
    private static EmailTemplateDocument DocWith(EmailBlockBase block)
    {
        var document = new EmailTemplateDocument();
        var section = new EmailSection();
        var column = new EmailColumn();
        column.Blocks.Add(block);
        section.Columns.Add(column);
        document.Sections.Add(section);
        return document;
    }

    private static string Generate(EmailBlockBase block) => new TextVersionGenerator().Generate(DocWith(block));

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/html;base64,PHNjcmlwdD5hbGVydCgxKTwvc2NyaXB0Pg==")]
    [InlineData("vbscript:msgbox(1)")]
    [InlineData("//evil.example.com/x")]
    public void TableCell_WithABlockedLink_PrintsTheTextWithoutTheUrl(string href)
    {
        var text = Generate(new EmailTableBlock
        {
            Rows =
            {
                new EmailTableRow { Cells = { new EmailTableCell { Text = "Docs", Href = href } } },
            },
        });

        text.Should().Contain("Docs");
        text.Should().NotContain("javascript:");
        text.Should().NotContain("vbscript:");
        text.Should().NotContain("evil.example.com");
        text.Should().NotContain("(");
    }

    [Theory]
    [InlineData("https://example.com/docs")]
    [InlineData("mailto:sales@example.com")]
    [InlineData("/relative/path")]
    public void TableCell_WithAnAllowedLink_PrintsTheTextAndTheUrl(string href)
    {
        var text = Generate(new EmailTableBlock
        {
            Rows =
            {
                new EmailTableRow { Cells = { new EmailTableCell { Text = "Docs", Href = href } } },
            },
        });

        text.Should().Contain($"Docs ({href})");
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/html,<script>alert(1)</script>")]
    public void Button_WithABlockedLink_PrintsTheLabelWithoutTheUrl(string href)
    {
        var text = Generate(new EmailButtonBlock { Text = "Shop now", Href = href });

        text.Should().Contain("Shop now");
        text.Should().NotContain("javascript:");
        text.Should().NotContain("(");
    }

    [Fact]
    public void Button_WithAnAllowedLink_PrintsTheLabelAndTheUrl()
    {
        var text = Generate(new EmailButtonBlock { Text = "Shop now", Href = "https://shop.example.com" });

        text.Should().Contain("Shop now (https://shop.example.com)");
    }
}
