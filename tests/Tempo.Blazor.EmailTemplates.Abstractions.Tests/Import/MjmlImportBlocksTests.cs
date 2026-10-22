using Tempo.Blazor.EmailTemplates.Abstractions.Import;
using Tempo.Blazor.EmailTemplates.Abstractions.Model.Blocks;

namespace Tempo.Blazor.EmailTemplates.Abstractions.Tests.Import;

public class MjmlImportBlocksTests
{
    private static readonly MjmlImporter Importer = new();

    private static EmailBlockBase FirstBlock(string blockMjml) => Import(blockMjml).Document!.Sections[0].Columns[0].Blocks[0];

    private static ImportResult Import(string blockMjml)
    {
        var mjml = $"<mjml><mj-body><mj-section><mj-column>{blockMjml}</mj-column></mj-section></mj-body></mjml>";
        return Importer.Import(mjml);
    }

    [Fact]
    public void ImportsButtonWithAttributes()
    {
        var b = FirstBlock("<mj-button href=\"https://a\" background-color=\"#0a0\" border-radius=\"8px\">Buy</mj-button>")
            .Should().BeOfType<EmailButtonBlock>().Subject;
        b.Text.Should().Be("Buy");
        b.Href.Should().Be("https://a");
        b.BackgroundColor.Should().Be("#0a0");
        b.BorderRadius.Should().Be("8px");
    }

    [Fact]
    public void ImportsImageTableSocialNavbarCarouselAccordion()
    {
        FirstBlock("<mj-image src=\"https://a/b.png\" alt=\"L\" />").Should().BeOfType<EmailImageBlock>()
            .Which.Alt.Should().Be("L");

        var table = FirstBlock("<mj-table><tr><th>H</th></tr><tr><td colspan=\"2\">V</td></tr></mj-table>")
            .Should().BeOfType<EmailTableBlock>().Subject;
        table.Rows.Should().HaveCount(2);
        table.Rows[0].IsHeader.Should().BeTrue();
        table.Rows[1].Cells[0].ColSpan.Should().Be(2);

        var social = FirstBlock("<mj-social mode=\"vertical\"><mj-social-element name=\"facebook\" href=\"#\">FB</mj-social-element></mj-social>")
            .Should().BeOfType<EmailSocialBlock>().Subject;
        social.Mode.Should().Be("vertical");
        social.Elements[0].Label.Should().Be("FB");

        FirstBlock("<mj-navbar><mj-navbar-link href=\"#\">Home</mj-navbar-link></mj-navbar>")
            .Should().BeOfType<EmailNavbarBlock>().Which.Links[0].Text.Should().Be("Home");

        FirstBlock("<mj-carousel><mj-carousel-image src=\"https://a/1.png\" alt=\"1\" /></mj-carousel>")
            .Should().BeOfType<EmailCarouselBlock>().Which.Images[0].Src.Should().Be("https://a/1.png");

        var acc = FirstBlock("<mj-accordion><mj-accordion-element><mj-accordion-title>Q</mj-accordion-title><mj-accordion-text><b>A</b></mj-accordion-text></mj-accordion-element></mj-accordion>")
            .Should().BeOfType<EmailAccordionBlock>().Subject;
        acc.Items[0].Title.Should().Be("Q");
        acc.Items[0].Content.Should().Contain("<b>A</b>");
    }

    [Fact]
    public void ImportsNestedHeroGroupWrapperAsBlocks()
    {
        FirstBlock("<mj-hero background-url=\"https://a/h.png\"><mj-text>H</mj-text></mj-hero>")
            .Should().BeOfType<EmailHeroBlock>().Which.Blocks.Should().ContainSingle();

        FirstBlock("<mj-group><mj-column width=\"50%\"><mj-text>g</mj-text></mj-column></mj-group>")
            .Should().BeOfType<EmailGroupBlock>().Which.Columns.Should().ContainSingle();

        FirstBlock("<mj-wrapper><mj-section><mj-column><mj-text>w</mj-text></mj-column></mj-section></mj-wrapper>")
            .Should().BeOfType<EmailWrapperBlock>().Which.Sections.Should().ContainSingle();
    }

    [Fact]
    public void UnknownElement_BecomesRawBlock_WithWarning()
    {
        var mjml = "<mjml><mj-body><mj-section><mj-column><mj-spinner foo=\"1\">x</mj-spinner></mj-column></mj-section></mj-body></mjml>";

        var result = Importer.Import(mjml);

        result.Document!.Sections[0].Columns[0].Blocks[0].Should().BeOfType<EmailRawBlock>()
            .Which.Content.Should().Contain("mj-spinner");
        result.Warnings.Should().Contain(w => w.Key == ImportKeys.UnknownElement && w.Detail == "mj-spinner");
    }

    [Fact]
    public void KnownAndInertAttributes_GoToExtraAttributes_AndAnUnknownOneIsWarnedAbout()
    {
        // Fáze 10 review (security 2): an extra attribute is RE-EMITTED by the generator, so the model may only
        // carry the ones the pipeline can write. `data-*` is inert and `sizes` is a known mj-image attribute, so
        // both survive; a name this component does not know is dropped AT IMPORT with the DroppedAttribute
        // warning, instead of surviving as a value every export silently refuses.
        var result = Import("<mj-image src=\"https://a/b.png\" data-track=\"42\" sizes=\"100vw\" some-future-attr=\"x\" />");
        var b = result.Document!.Sections[0].Columns[0].Blocks[0];
        b.ExtraAttributes.Should().ContainKey("data-track").WhoseValue.Should().Be("42");
        b.ExtraAttributes.Should().ContainKey("sizes").WhoseValue.Should().Be("100vw");
        b.ExtraAttributes.Should().NotContainKey("some-future-attr");
        result.Warnings.Should().Contain(w => w.Key == ImportKeys.DroppedAttribute && w.Detail == "mj-image some-future-attr");
    }

    [Theory]
    [InlineData("onclick=\"alert(1)\"")]
    [InlineData("srcdoc=\"&lt;script&gt;alert(2)&lt;/script&gt;\"")]
    [InlineData("style=\"background:url(https://tracker)\"")]
    [InlineData("background=\"javascript:alert(3)\"")]
    [InlineData("data-x=\"x;background:url(https://tracker)\"")]
    public void UnsafeAttribute_IsDroppedFromTheModel_AndWarnedAbout(string attribute)
    {
        var result = Import($"<mj-text {attribute}>hi</mj-text>");
        var b = result.Document!.Sections[0].Columns[0].Blocks[0];
        b.ExtraAttributes.Should().BeEmpty("the pipeline may never write it");
        result.Warnings.Should().Contain(w => w.Key == ImportKeys.DroppedAttribute);
    }

    [Fact]
    public void ColourWithADeclarationBreak_StaysInTheModel_AndIsAuditedAsAnInvalidColour()
    {
        // A MODEL colour is not an extra attribute: it survives so the editor can show and fix it, and every
        // emission path fail-closes it (the generator drops it, ImportValueAudit warns) — the same contract a
        // blocked link has.
        var result = Import("<mj-text color=\"red;background:url(https://tracker)\">hi</mj-text>");
        var b = result.Document!.Sections[0].Columns[0].Blocks[0].Should().BeOfType<EmailTextBlock>().Subject;
        b.Color.Should().Be("red;background:url(https://tracker)");
        b.ExtraAttributes.Should().BeEmpty();
        result.Warnings.Should().Contain(w => w.Key == ImportKeys.InvalidColor);
    }

    [Fact]
    public void CssClassAndMjClass_AreImported()
    {
        var b = FirstBlock("<mj-text css-class=\"promo\" mj-class=\"big small\">hi</mj-text>");
        b.CssClass.Should().Be("promo");
        b.MjClasses.Should().ContainInOrder("big", "small");
    }
}
