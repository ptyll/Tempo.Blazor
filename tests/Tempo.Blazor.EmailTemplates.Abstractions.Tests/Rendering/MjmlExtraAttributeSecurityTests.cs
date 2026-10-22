using System.Xml.Linq;
using Tempo.Blazor.EmailTemplates.Abstractions.Model;
using Tempo.Blazor.EmailTemplates.Abstractions.Model.Blocks;
using Tempo.Blazor.EmailTemplates.Abstractions.Rendering;

namespace Tempo.Blazor.EmailTemplates.Abstractions.Tests.Rendering;

/// <summary>
/// Fáze 10 review (security 2 + 3): an attribute preserved from an import is re-emitted through an ALLOW-list
/// of the tag's known MJML attributes (a deny filter is one invented attribute name away from a live handler),
/// its value has to survive the rule that stops a value from opening a declaration of its own
/// (<c>x;background:url(…)</c>), and the model-driven style-bound attributes
/// (<c>padding</c>/<c>border</c>/<c>font-family</c>/<c>width</c>/<c>height</c>/<c>line-height</c>/
/// <c>letter-spacing</c>) are validated at emission because the compiler copies them into the message's
/// inline <c>style</c>.
/// </summary>
public class MjmlExtraAttributeSecurityTests
{
    private static readonly MjmlGenerator Generator = new();

    private static EmailTemplateDocument DocWith(EmailBlockBase block)
    {
        var doc = new EmailTemplateDocument();
        var section = new EmailSection();
        var column = new EmailColumn();
        column.Blocks.Add(block);
        section.Columns.Add(column);
        doc.Sections.Add(section);
        return doc;
    }

    private static (string Mjml, IReadOnlyList<RenderError> Diagnostics) Generate(EmailTemplateDocument doc)
    {
        var diagnostics = new List<RenderError>();
        var mjml = Generator.Generate(doc, MjmlGeneratorOptions.Default, diagnostics);
        return (mjml, diagnostics);
    }

    private static EmailTextBlock TextWith(params (string Name, string Value)[] extra)
    {
        var block = new EmailTextBlock { Content = "hi" };
        foreach (var (name, value) in extra)
        {
            block.ExtraAttributes[name] = value;
        }

        return block;
    }

    [Theory]
    [InlineData("srcdoc", "<script>alert(1)</script>")]
    [InlineData("onclick", "alert(1)")]
    [InlineData("style", "background:url(https://tracker)")]
    [InlineData("some-future-attr", "x")]
    public void ExtraAttribute_NotInTheTagsAllowList_IsNotEmitted(string name, string value)
    {
        var (mjml, _) = Generate(DocWith(TextWith((name, value))));

        XDocument.Parse(mjml).Descendants()
            .SelectMany(e => e.Attributes())
            .Should().NotContain(a => a.Name.LocalName == name);
    }

    [Theory]
    [InlineData("background", "javascript:alert(1)")]
    [InlineData("data", "javascript:alert(1)")]
    [InlineData("href", "javascript:alert(1)")]
    public void ExtraAttribute_UrlName_NeverCarriesABlockedScheme(string name, string value)
    {
        var (mjml, _) = Generate(DocWith(TextWith((name, "javascript:alert(1)"))));

        mjml.Should().NotContain("javascript:");
        XDocument.Parse(mjml).Descendants()
            .SelectMany(e => e.Attributes())
            .Where(a => a.Name.LocalName == name)
            .Should().OnlyContain(a => a.Value == EmailUrlPolicy.BlockedUrlPlaceholder || a.Value.Length == 0);
    }

    [Theory]
    [InlineData("color", "x;background:url(javascript:alert(1))")]
    [InlineData("padding", "x;background:url(javascript:alert(1))")]
    [InlineData("background-color", "x;background:url(javascript:alert(1))")]
    public void ExtraAttribute_ValueWithACssDeclarationBreak_IsDropped(string name, string value)
    {
        var (mjml, _) = Generate(DocWith(TextWith((name, value))));

        mjml.Should().NotContain("javascript:");
        mjml.Should().NotContain("url(");
    }

    [Fact]
    public void ExtraAttribute_DuplicatingAModelAttribute_IsEmittedOnce()
    {
        var block = TextWith(("color", "#ff0000"));
        block.Color = "#00ff00";

        var (mjml, _) = Generate(DocWith(block));

        // A duplicate attribute makes the export unparsable XML and lets the LAST value win in a lenient
        // parser, so exactly one of them may reach the tag.
        var text = XDocument.Parse(mjml).Descendants().Single(e => e.Name.LocalName == "mj-text");
        text.Attributes().Count(a => a.Name.LocalName == "color").Should().Be(1);
    }

    [Theory]
    [InlineData("data-track", "42")]
    [InlineData("letter-spacing", "2px")]
    [InlineData("container-background-color", "#eeeeee")]
    public void ExtraAttribute_KnownOrInert_IsPreserved(string name, string value)
    {
        var (mjml, _) = Generate(DocWith(TextWith((name, value))));

        mjml.Should().Contain(name + "=");
    }

    [Fact]
    public void ContainerBackgroundColour_WithADeclarationBreak_IsDropped()
    {
        var block = new EmailTextBlock { Content = "hi", ContainerBackgroundColor = "red;background-image:url(https://tracker)" };

        var (mjml, _) = Generate(DocWith(block));

        mjml.Should().NotContain("tracker");
        mjml.Should().NotContain("container-background-color");
    }

    [Fact]
    public void CarouselThumbnailsSrc_WithABlockedScheme_IsNeverEmitted()
    {
        var carousel = new EmailCarouselBlock();
        carousel.Images.Add(new EmailCarouselImage { Src = "https://example.com/a.png", ThumbnailsSrc = "javascript:alert(1)" });

        var (mjml, _) = Generate(DocWith(carousel));

        mjml.Should().NotContain("javascript:");
        XDocument.Parse(mjml).Descendants().Single(e => e.Name.LocalName == "mj-carousel-image")
            .Attributes().Single(a => a.Name.LocalName == "thumbnails-src").Value
            .Should().Be(EmailUrlPolicy.BlockedUrlPlaceholder);
    }

    [Theory]
    [InlineData("padding-top", "0;background:url(https://tracker)")]
    [InlineData("border", "1px solid red;background:url(https://tracker)")]
    [InlineData("font-family", "Arial;background:url(https://tracker)")]
    [InlineData("line-height", "1.4;background:url(https://tracker)")]
    [InlineData("letter-spacing", "1px;background:url(https://tracker)")]
    public void StyleBoundAttribute_WithADeclarationBreak_IsDroppedAndReported(string attribute, string value)
    {
        EmailBlockBase block;
        if (attribute == "border")
        {
            block = new EmailButtonBlock { Text = "Go", Border = value };
        }
        else
        {
            var text = new EmailTextBlock { Content = "hi" };
            switch (attribute)
            {
                case "padding-top": text.PaddingTop = value; break;
                case "font-family": text.FontFamily = value; break;
                case "line-height": text.LineHeight = value; break;
                default: text.LetterSpacing = value; break;
            }

            block = text;
        }

        var (mjml, diagnostics) = Generate(DocWith(block));

        mjml.Should().NotContain("tracker");
        diagnostics.Should().Contain(d => d.Key == RenderKeys.InvalidAttributeValue);
    }

    [Theory]
    [InlineData("16px")]
    [InlineData("1.5")]
    [InlineData("100%")]
    [InlineData("Ubuntu, Helvetica, Arial, sans-serif")]
    [InlineData("1px solid #cfcfcf")]
    [InlineData("120%")]
    public void StyleBoundAttribute_OrdinaryValues_AreStillEmitted(string value)
    {
        var block = new EmailTextBlock { Content = "hi", PaddingTop = value };

        var (mjml, diagnostics) = Generate(DocWith(block));

        mjml.Should().Contain("padding-top=");
        diagnostics.Should().NotContain(d => d.Key == RenderKeys.InvalidAttributeValue);
    }

    [Theory]
    [InlineData(".x{width:expression(alert(1))}")]
    [InlineData(".x{behavior:url(#default#time2)}")]
    [InlineData(".x{-moz-binding:url(https://tracker)}")]
    [InlineData("@import url(https://tracker);")]
    [InlineData(".x{background:url(javascript:alert(1))}")]
    public void StylePayload_TheCompilerCouldExecuteOrFetch_IsDropped(string css)
    {
        var doc = new EmailTemplateDocument();
        doc.Styles.Styles.Add(new EmailStyle { Css = css });

        var mjml = Generator.Generate(doc);

        mjml.Should().NotContain("expression(");
        mjml.Should().NotContain("behavior:");
        mjml.Should().NotContain("-moz-binding");
        mjml.Should().NotContain("@import");
        mjml.Should().NotContain("tracker");
        mjml.Should().Contain("<mj-style></mj-style>", "the block stays, only its payload is refused");
    }
}
