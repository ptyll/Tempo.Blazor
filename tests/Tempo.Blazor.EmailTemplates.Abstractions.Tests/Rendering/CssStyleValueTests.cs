using System.Xml.Linq;
using Tempo.Blazor.EmailTemplates.Abstractions.Import;
using Tempo.Blazor.EmailTemplates.Abstractions.Model;
using Tempo.Blazor.EmailTemplates.Abstractions.Model.Blocks;
using Tempo.Blazor.EmailTemplates.Abstractions.Rendering;

namespace Tempo.Blazor.EmailTemplates.Abstractions.Tests.Rendering;

/// <summary>
/// Fáze 10 security re-review (item 5, minor): a font stack may quote a family — <c>'Open Sans', Arial</c> is
/// ordinary CSS and the editor round-trips it — so a single quote is allowed for the <c>font-family</c>
/// attribute, but only in an EVEN number (an unpaired quote would still leave the attribute). Every other
/// character that could open a declaration or a function (<c>;</c>, <c>(</c>, <c>\</c>, <c>"</c>) stays
/// refused, and every other style-bound attribute keeps the quote-free charset.
/// </summary>
public class CssStyleValueTests
{
    [Theory]
    [InlineData("'Open Sans', Arial")]
    [InlineData("'Open Sans'")]
    [InlineData("Arial")]
    [InlineData("Ubuntu, Helvetica, Arial, sans-serif")]
    public void IsPlausible_FontFamilyWithAnEvenNumberOfQuotes_IsAccepted(string value)
        => CssStyleValue.IsPlausible(value, "font-family").Should().BeTrue();

    [Theory]
    [InlineData("'Open Sans, Arial")]
    [InlineData("'Open Sans', Arial'")]
    [InlineData("'a;b'")]
    [InlineData("'a(b'")]
    [InlineData("'a\\'b'")]
    [InlineData("'a\"b'")]
    public void IsPlausible_FontFamilyWithAnUnpairedQuoteOrForbiddenCharacter_IsRefused(string value)
        => CssStyleValue.IsPlausible(value, "font-family").Should().BeFalse();

    [Theory]
    [InlineData("'Open Sans', Arial")]
    [InlineData("Arial'")]
    public void IsPlausible_OtherStyleBoundAttribute_RefusesEveryQuote(string value)
    {
        CssStyleValue.IsPlausible(value, "padding").Should().BeFalse();
        CssStyleValue.IsPlausible(value).Should().BeFalse();
    }

    [Fact]
    public void FontFamily_WithAQuotedFamily_RoundTripsThroughGeneratorAndImporter()
    {
        const string family = "'Open Sans', Arial";
        var document = new EmailTemplateDocument();
        var section = new EmailSection();
        var column = new EmailColumn();
        column.Blocks.Add(new EmailTextBlock { Content = "hi", FontFamily = family });
        section.Columns.Add(column);
        document.Sections.Add(section);

        var mjml = new MjmlGenerator().Generate(document);

        // Parsing proves the value did not leave the attribute: it is the same value after XML decoding.
        XDocument.Parse(mjml).Descendants().Single(e => e.Name.LocalName == "mj-text")
            .Attribute("font-family")!.Value.Should().Be(family);

        var imported = new MjmlImporter().Import(mjml);

        imported.Errors.Should().BeEmpty();
        imported.Document!.Sections[0].Columns[0].Blocks.OfType<EmailTextBlock>().Single()
            .FontFamily.Should().Be(family);
    }

    [Fact]
    public void FontFamily_WithADeclarationBreak_IsStillDropped()
    {
        var document = new EmailTemplateDocument();
        var section = new EmailSection();
        var column = new EmailColumn();
        column.Blocks.Add(new EmailTextBlock { Content = "hi", FontFamily = "'a;b'" });
        section.Columns.Add(column);
        document.Sections.Add(section);
        var diagnostics = new List<RenderError>();

        var mjml = new MjmlGenerator().Generate(document, MjmlGeneratorOptions.Default, diagnostics);

        mjml.Should().NotContain("a;b");
        XDocument.Parse(mjml).Descendants().Single(e => e.Name.LocalName == "mj-text")
            .Attribute("font-family").Should().BeNull("the declared break keeps the value out of the export");
        diagnostics.Should().Contain(d => d.Key == RenderKeys.InvalidAttributeValue);
    }
}
