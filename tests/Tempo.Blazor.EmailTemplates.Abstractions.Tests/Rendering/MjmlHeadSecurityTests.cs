using System.Xml.Linq;
using Tempo.Blazor.EmailTemplates.Abstractions.Model;
using Tempo.Blazor.EmailTemplates.Abstractions.Model.Blocks;
using Tempo.Blazor.EmailTemplates.Abstractions.Rendering;

namespace Tempo.Blazor.EmailTemplates.Abstractions.Tests.Rendering;

/// <summary>
/// Fáze 10 review (security 1, BLOCKER): the MJML HEAD is untrusted input too. <c>mj-attributes</c> carries a
/// TAG NAME and attribute NAMES that used to be written verbatim, and <c>mj-font href</c> /
/// <c>mj-html-attribute</c> carried a raw URL and a raw name — so a crafted import could inject an element
/// (<c>&lt;mj-text&gt;&lt;script&gt;</c>), an event handler (<c>x" onclick="alert(2)=</c>) or a
/// <c>javascript:</c> URL into the exported markup. The head is re-emitted only through the same rules the
/// body uses: a known <c>mj-*</c> tag, an attribute name from that tag's allow-list, a colour of
/// <see cref="CssColourValue"/> and a URL of <see cref="EmailUrlPolicy"/>.
/// </summary>
public class MjmlHeadSecurityTests
{
    private static readonly MjmlGenerator Generator = new();

    private static EmailTemplateDocument DocWithText()
    {
        var doc = new EmailTemplateDocument();
        var section = new EmailSection();
        var column = new EmailColumn();
        column.Blocks.Add(new EmailTextBlock { Content = "hi" });
        section.Columns.Add(column);
        doc.Sections.Add(section);
        return doc;
    }

    /// <summary>Every attribute of every element of the export, with its owning tag (a parsed XML document).</summary>
    private static IEnumerable<(string Tag, string Name, string Value)> Attributes(string mjml)
    {
        var root = XDocument.Parse(mjml);
        return root.Descendants()
            .SelectMany(element => element.Attributes()
                .Select(attribute => (element.Name.LocalName, attribute.Name.LocalName, attribute.Value)));
    }

    [Fact]
    public void MjAll_AttributeNameCarryingAHandler_IsDropped()
    {
        // The raw payload is <mj-all x" onclick="alert(2)="1"> — the NAME is the injection; escaping the value
        // cannot help because the attribute name itself is what leaves the quotes.
        var doc = DocWithText();
        doc.Styles.Attributes.All["x\" onclick=\"alert(2)"] = "1";

        var mjml = Generator.Generate(doc);

        mjml.Should().NotContain("onclick");
        Attributes(mjml).Should().NotContain(a => a.Name.StartsWith("on", StringComparison.Ordinal));
        XDocument.Parse(mjml).Descendants().Should().Contain(e => e.Name.LocalName == "mj-all",
            "the harmless rest of the container still has to be emitted");
    }

    [Fact]
    public void PerTag_AttributeNameOutsideTheTagAllowList_IsDropped()
    {
        var doc = DocWithText();
        doc.Styles.Attributes.PerTag["mj-text"] = new() { ["onmouseover"] = "alert(1)", ["style"] = "x" };

        var mjml = Generator.Generate(doc);

        mjml.Should().NotContain("onmouseover");
        mjml.Should().NotContain("style=");
    }

    [Fact]
    public void PerTag_TagNameMustBeAKnownMjmlComponent()
    {
        // <mj-text><script>alert(1)</script><mj-text …></mj-text> — the TAG NAME is the injection here.
        var doc = DocWithText();
        doc.Styles.Attributes.PerTag["mj-text><script>alert(1)</script><mj-text"] = new() { ["color"] = "#111" };

        var mjml = Generator.Generate(doc);

        mjml.Should().NotContain("<script");
        XDocument.Parse(mjml).Descendants().Should().NotContain(e => e.Name.LocalName == "script");
    }

    [Fact]
    public void PerTag_ColourWithACssDeclarationBreak_IsDroppedFromTheHead()
    {
        // A colour is copied into the message's inline style by the compiler: "red;background-image:url(…)"
        // stops being a colour and becomes a tracking pixel of its own.
        var doc = DocWithText();
        doc.Styles.Attributes.PerTag["mj-text"] = new() { ["color"] = "red;background-image:url(https://tracker)" };

        var mjml = Generator.Generate(doc);

        mjml.Should().NotContain("tracker");
        mjml.Should().NotContain("background-image");
    }

    [Fact]
    public void PerTag_UrlAttributeWithABlockedScheme_IsNeverEmitted()
    {
        var doc = DocWithText();
        doc.Styles.Attributes.PerTag["mj-button"] = new() { ["href"] = "javascript:alert(1)" };

        var mjml = Generator.Generate(doc);

        mjml.Should().NotContain("javascript:");
        Attributes(mjml).Where(a => a.Name == "href")
            .Should().OnlyContain(a => a.Value == EmailUrlPolicy.BlockedUrlPlaceholder);
    }

    [Fact]
    public void HtmlAttribute_NameCarryingAHandlerStyleOrABlockedUrl_IsDropped()
    {
        var doc = DocWithText();
        var selector = new MjHtmlSelector { Path = ".promo" };
        selector.Attributes["onclick"] = "alert(1)";
        selector.Attributes["style"] = "background:url(https://tracker)";
        selector.Attributes["href"] = "javascript:alert(2)";
        selector.Attributes["data-id"] = "1";
        doc.Styles.HtmlAttributes.Add(selector);

        var mjml = Generator.Generate(doc, MjmlGeneratorOptions.ForExport);

        mjml.Should().NotContain("onclick");
        mjml.Should().NotContain("javascript:");
        mjml.Should().NotContain("tracker");
        mjml.Should().Contain("name=\"data-id\"", "an inert data attribute survives");
    }

    [Fact]
    public void FontHref_GoesThroughTheUrlAllowList()
    {
        var doc = DocWithText();
        doc.Styles.Fonts.Add(new EmailFont { Name = "Evil", Href = "javascript:alert(1)" });

        var mjml = Generator.Generate(doc);

        mjml.Should().NotContain("javascript:");
        Attributes(mjml).Should().Contain(a => a.Tag == "mj-font" && a.Name == "href"
            && a.Value == EmailUrlPolicy.BlockedUrlPlaceholder);
    }

    [Fact]
    public void HostileHead_ExportParsesAsXml_AndCarriesNoScriptHandlerOrJavascriptUrl()
    {
        var doc = DocWithText();
        doc.Styles.Attributes.All["x\" onclick=\"alert(2)"] = "1";
        doc.Styles.Attributes.PerTag["mj-text><script>alert(3)</script><mj-text"] = new()
        {
            ["color"] = "red;background-image:url(https://tracker)",
            ["href"] = "javascript:alert(4)",
            ["onerror"] = "alert(5)",
        };
        doc.Styles.Attributes.PerTag["mj-text"] = new() { ["color"] = "#111" };
        doc.Styles.Attributes.Classes["cta\" onclick=\"alert(6)"] = new() { ["background-color"] = "url(https://t)" };
        doc.Styles.Fonts.Add(new EmailFont { Name = "Evil", Href = "javascript:alert(7)" });
        var selector = new MjHtmlSelector { Path = "body\" onclick=\"alert(8)" };
        selector.Attributes["onclick"] = "alert(9)";
        selector.Attributes["href"] = "javascript:alert(10)";
        doc.Styles.HtmlAttributes.Add(selector);

        var mjml = Generator.Generate(doc, MjmlGeneratorOptions.ForExport);

        // A single unescaped quote in an attribute name or a tag name breaks the XML, so parsing the export
        // proves the head is well-formed markup and not a text blob.
        var root = XDocument.Parse(mjml);
        root.Descendants().Select(e => e.Name.LocalName)
            .Should().NotContain(new[] { "script", "iframe", "object" });

        foreach (var (tag, name, value) in Attributes(mjml))
        {
            name.Should().NotStartWith("on", $"'{tag}' must not carry a handler");
            value.Should().NotContain("javascript:");
            value.Should().NotContain("tracker");
        }

        mjml.Should().NotContain("expression(");
    }
}
