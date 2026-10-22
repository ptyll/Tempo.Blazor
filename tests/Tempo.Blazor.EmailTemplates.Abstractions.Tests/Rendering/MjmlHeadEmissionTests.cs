using Tempo.Blazor.EmailTemplates.Abstractions.Model;
using Tempo.Blazor.EmailTemplates.Abstractions.Rendering;

namespace Tempo.Blazor.EmailTemplates.Abstractions.Tests.Rendering;

public class MjmlHeadEmissionTests
{
    private static readonly MjmlGenerator Generator = new();

    [Fact]
    public void Head_EmitsFontsBreakpointAndStyles()
    {
        var doc = new EmailTemplateDocument();
        doc.Styles.Breakpoint = "600px";
        doc.Styles.Fonts.Add(new EmailFont { Name = "Roboto", Href = "https://f/r.css" });
        doc.Styles.Styles.Add(new EmailStyle { Css = ".x{color:red}" });
        doc.Styles.Styles.Add(new EmailStyle { Css = ".y{color:blue}", Inline = true });

        var mjml = Generator.Generate(doc);

        mjml.Should().Contain("<mj-breakpoint width=\"600px\" />");
        mjml.Should().Contain("<mj-font name=\"Roboto\" href=\"https://f/r.css\" />");
        mjml.Should().Contain("<mj-style>.x{color:red}</mj-style>");
        mjml.Should().Contain("<mj-style inline=\"inline\">.y{color:blue}</mj-style>");
    }

    [Theory]
    [InlineData("", "https://f/r.css")]
    [InlineData("x\" onclick=\"alert(1)", "https://f/r.css")]
    [InlineData("Roboto", "")]
    public void Head_EmitsAFontOnlyWhenBothNameAndHrefAreValid(string name, string href)
    {
        // Fáze 10 security re-review (item 6): the old guard only asked whether an href survived, so a font
        // without a name was emitted as <mj-font href="…" /> — an element the compiler rejects. A name that is
        // dropped by the attribute buffer counts as no name.
        var doc = new EmailTemplateDocument();
        doc.Styles.Fonts.Add(new EmailFont { Name = name, Href = href });

        var mjml = Generator.Generate(doc);

        mjml.Should().NotContain("<mj-font");
        mjml.Should().NotContain("onclick");
    }

    [Fact]
    public void Head_EmitsMjAttributes_WithExplicitCloseTags()
    {
        var doc = new EmailTemplateDocument();
        doc.Styles.Attributes.All["font-family"] = "Arial";
        doc.Styles.Attributes.PerTag["mj-text"] = new() { ["color"] = "#222" };
        doc.Styles.Attributes.Classes["cta"] = new() { ["background-color"] = "#f00" };

        var mjml = Generator.Generate(doc);

        mjml.Should().Contain("<mj-attributes>");
        // E0.9: children MUST use explicit close tags, never self-closing.
        mjml.Should().Contain("<mj-all font-family=\"Arial\"></mj-all>");
        mjml.Should().Contain("<mj-text color=\"#222\"></mj-text>");
        mjml.Should().Contain("<mj-class name=\"cta\" background-color=\"#f00\"></mj-class>");
        mjml.Should().NotContain("<mj-all font-family=\"Arial\" />");
    }

    [Fact]
    public void Head_HtmlAttributes_OmittedByDefault_EmittedForExport()
    {
        var doc = new EmailTemplateDocument();
        var selector = new MjHtmlSelector { Path = ".promo" };
        selector.Attributes["data-id"] = "1";
        doc.Styles.HtmlAttributes.Add(selector);

        Generator.Generate(doc).Should().NotContain("mj-html-attributes");
        Generator.Generate(doc, MjmlGeneratorOptions.ForExport).Should().Contain("<mj-html-attributes>");
    }

    [Fact]
    public void Head_AppliesGlobalFontFamilyAsMjAll_WhenNotOverridden()
    {
        var doc = new EmailTemplateDocument();
        doc.Styles.FontFamily = "Verdana, sans-serif";

        var mjml = Generator.Generate(doc);

        mjml.Should().Contain("<mj-all font-family=\"Verdana, sans-serif\"></mj-all>");
    }
}
