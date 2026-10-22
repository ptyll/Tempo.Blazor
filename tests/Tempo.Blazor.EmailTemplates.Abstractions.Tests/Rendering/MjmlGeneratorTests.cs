using Tempo.Blazor.EmailTemplates.Abstractions.Model;
using Tempo.Blazor.EmailTemplates.Abstractions.Model.Blocks;
using Tempo.Blazor.EmailTemplates.Abstractions.Rendering;

namespace Tempo.Blazor.EmailTemplates.Abstractions.Tests.Rendering;

public class MjmlGeneratorTests
{
    private static readonly MjmlGenerator Generator = new();

    private static EmailTemplateDocument DocWith(params EmailBlockBase[] blocks)
    {
        var doc = new EmailTemplateDocument();
        var section = new EmailSection();
        var col = new EmailColumn();
        foreach (var block in blocks)
            col.Blocks.Add(block);
        section.Columns.Add(col);
        doc.Sections.Add(section);
        return doc;
    }

    [Fact]
    public void EmptyDocument_ProducesValidSkeleton()
    {
        var doc = new EmailTemplateDocument { Subject = "Hi", Preheader = "Peek" };

        var mjml = Generator.Generate(doc);

        mjml.Should().StartWith("<mjml");
        mjml.Should().Contain("<mj-head>").And.Contain("</mj-head>");
        mjml.Should().Contain("<mj-body");
        mjml.Should().Contain("<mj-title>Hi</mj-title>");
        mjml.Should().Contain("<mj-preview>Peek</mj-preview>");
        mjml.Should().Contain("width=\"600px\""); // body width from styles
    }

    [Fact]
    public void Section_EmitsNonDefaultAttributesOnly()
    {
        var doc = new EmailTemplateDocument();
        doc.Sections.Add(new EmailSection { BackgroundColor = "#fafafa", FullWidth = true });

        var mjml = Generator.Generate(doc);

        mjml.Should().Contain("<mj-section");
        mjml.Should().Contain("background-color=\"#fafafa\"");
        mjml.Should().Contain("full-width=\"full-width\"");
        mjml.Should().NotContain("direction=\"ltr\""); // default omitted
    }

    [Fact]
    public void Column_EmitsWidthAndPropagatesBlocks()
    {
        var doc = DocWith(new EmailTextBlock { Content = "x" });
        doc.Sections[0].Columns[0].Width = "50%";

        var mjml = Generator.Generate(doc);

        mjml.Should().Contain("<mj-column").And.Contain("width=\"50%\"");
        mjml.Should().Contain("<mj-text");
    }

    [Fact]
    public void Text_AllowsWhitelistedInlineHtml_ButStripsScript()
    {
        var doc = DocWith(new EmailTextBlock
        {
            Content = "<b>Hello</b> <a href=\"https://ok\">link</a><script>alert(1)</script>"
        });

        var mjml = Generator.Generate(doc);

        mjml.Should().Contain("<b>Hello</b>");
        mjml.Should().Contain("<a href=\"https://ok\">link</a>");
        mjml.Should().NotContain("<script");
        mjml.Should().NotContain("alert(1)");
    }

    [Fact]
    public void Text_StripsJavascriptHrefAndEventHandlers()
    {
        var doc = DocWith(new EmailTextBlock
        {
            Content = "<a href=\"javascript:alert(1)\" onclick=\"evil()\">x</a>"
        });

        var mjml = Generator.Generate(doc);

        mjml.Should().NotContain("javascript:");
        mjml.Should().NotContain("onclick");
    }

    [Fact]
    public void Attributes_AreEscaped_PreventingMarkupInjection()
    {
        var doc = new EmailTemplateDocument();
        doc.Sections.Add(new EmailSection { BackgroundColor = "\"><script>alert(1)</script>" });

        var mjml = Generator.Generate(doc);

        // Fáze 10: a COLOUR attribute is now validated instead of escaped — the compiler copies it into the
        // message's style, where escaping does not help (a quote and a semicolon would open a declaration of
        // their own). A value that is not a CSS colour is therefore dropped entirely, which is strictly safer
        // than the escaped text the old assertion pinned.
        mjml.Should().NotContain("<script>alert(1)</script>");
        mjml.Should().NotContain("&lt;script&gt;", "the value is not a colour, so nothing of it is emitted");
        mjml.Should().NotContain("background-color=\"\"");
    }

    [Fact]
    public void Button_EmptyHref_DoesNotBreakMjml()
    {
        var doc = DocWith(new EmailButtonBlock { Text = "Go", Href = "" });

        var mjml = Generator.Generate(doc);

        mjml.Should().Contain("<mj-button");
        mjml.Should().Contain(">Go</mj-button>");
        mjml.Should().NotContain("href=\"\"");
    }

    [Fact]
    public void Image_EmitsSrcAndAlt()
    {
        var doc = DocWith(new EmailImageBlock { Src = "https://a/b.png", Alt = "Logo" });

        var mjml = Generator.Generate(doc);

        mjml.Should().Contain("<mj-image");
        mjml.Should().Contain("src=\"https://a/b.png\"");
        mjml.Should().Contain("alt=\"Logo\"");
    }

    [Fact]
    public void DividerAndSpacer_Emit()
    {
        var doc = DocWith(new EmailDividerBlock { BorderColor = "#ccc" }, new EmailSpacerBlock { Height = "40px" });

        var mjml = Generator.Generate(doc);

        mjml.Should().Contain("<mj-divider").And.Contain("border-color=\"#ccc\"");
        mjml.Should().Contain("<mj-spacer").And.Contain("height=\"40px\"");
    }

    [Fact]
    public void Raw_EmitsVerbatim_AndDocumentReportsRawContent()
    {
        var doc = DocWith(new EmailRawBlock { Content = "<custom-tag>keep & <b>this</b></custom-tag>" });

        var mjml = Generator.Generate(doc);

        mjml.Should().Contain("<mj-raw>");
        mjml.Should().Contain("<custom-tag>keep & <b>this</b></custom-tag>");
        doc.ContainsRawContent().Should().BeTrue();
        new EmailTemplateDocument().ContainsRawContent().Should().BeFalse();
    }
}
