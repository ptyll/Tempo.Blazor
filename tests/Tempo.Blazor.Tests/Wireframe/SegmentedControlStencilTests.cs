using FluentAssertions;
using Tempo.Blazor.Components.Wireframe;
using Tempo.Blazor.Components.Wireframe.Models;
using Xunit;

namespace Tempo.Blazor.Tests.Wireframe;

/// <summary>
/// Plan ff043b2e phase 8: <c>TmSegmentedControl</c> is a wireframe citizen — a new component needs a UI role
/// and a declarative stencil, or the palette and the MCP catalogue drift away from the component library.
/// The schema is the contract the palette, the properties panel and the stencil pack share, so the pack's
/// props must match it exactly, in order.
/// </summary>
public sealed class SegmentedControlStencilTests
{
    private static readonly string[] ExpectedProps =
        ["label", "options", "active", "sizeVariant", "showCount", "badge", "disabled"];

    [Fact]
    public void BuiltInSchema_ListsTheSegmentedControl_WithItsRoleAndProps()
    {
        var schema = new BuiltInComponentSchemas().GetSchemas().Single(s => s.Type == "TmSegmentedControl");

        schema.Category.Should().Be("Inputs");
        schema.DisplayName.Should().Be("Segmented Control");
        schema.Roles.Should().BeEquivalentTo("segmented-control");
        schema.Props.Select(prop => prop.Name).Should().Equal(ExpectedProps);
        schema.DefaultWidth.Should().Be(240);
        schema.DefaultHeight.Should().Be(52,
            "the sketch paints a 20 px label line ABOVE the 32 px tray, so a 36 px element box clipped "
            + "both the label and the badge that sits inside the tray (phase 16 of plan 7ba95ed6)");
    }

    [Fact]
    public void StencilPack_DeclaresTheSameDefaultSizeAsTheSchema()
    {
        using var pack = System.Text.Json.JsonDocument.Parse(BuiltInStencilPackProvider.ReadPackJson());
        var component = pack.RootElement.GetProperty("components").EnumerateArray()
            .Single(entry => entry.GetProperty("type").GetString() == "TmSegmentedControl");
        var size = component.GetProperty("defaultSize");

        size.GetProperty("width").GetDouble().Should().Be(240);
        size.GetProperty("height").GetDouble().Should().Be(52,
            "a 52 px default is what the label line plus the 32 px tray need, and the palette inserts "
            + "the pack's defaultSize — a 36 there would keep clipping the sketch");
    }

    [Fact]
    public void TheSketchFitsItsDeclaredHeight()
    {
        // The pack stacks a label line (y 10) over a tray that starts at y 20 and is 32 px tall on the Md
        // default, so the drawn height is 52. The badge baseline (y 26 inside the tray) stays inside it.
        var entry = Entry();
        var schema = new BuiltInComponentSchemas().GetSchemas().Single(s => s.Type == "TmSegmentedControl");

        entry.Should().Contain("\"y\": \"{label ? 20 : 0}\"", "the tray starts below the label line");
        entry.Should().Contain("\"y\": 26", "the badge baseline sits inside the tray, not past the box");

        schema.DefaultHeight.Should().BeGreaterThanOrEqualTo(
            52,
            "the Md tray (32 px) plus its label line (20 px) has to fit the declared default height");
    }

    [Fact]
    public void ThePropNamesAvoidTheStencilExpressionContext()
    {
        var schema = new BuiltInComponentSchemas().GetSchemas().Single(s => s.Type == "TmSegmentedControl");

        schema.Props.Should().NotContain(prop => prop.Name == "size",
            "the stencil expression context already owns `size` for the element box — a prop of that name would shadow it");
    }

    [Fact]
    public void TheLgVariantWithALabelIsTallerThanTheDefaultBox_KnownLimitation()
    {
        // Recorded, not fixed (phase 16 review): the pack stacks a 20 px label line ABOVE the tray, and
        // the Lg tray is 40 px, so an Lg control WITH a label draws 60 px inside the 52 px default box.
        // The default variant is Md (20 + 32 = 52) — that is what the palette inserts — and an Lg element
        // is made taller by its host or its resize handle. Pinned here so the overflow stays a known,
        // deliberate number and a future fix has a test to flip instead of a silent regression.
        var entry = Entry();
        var schema = new BuiltInComponentSchemas().GetSchemas().Single(s => s.Type == "TmSegmentedControl");

        entry.Should().Contain("sizeVariant == \\\"Lg\\\" ? 40", "the Lg tray is 40 px tall");
        entry.Should().Contain("sizeVariant == \\\"Sm\\\" ? 24", "the Sm tray is 24 px tall");

        const double labelLine = 20;
        const double mdTray = 32;
        const double lgTray = 40;

        (labelLine + mdTray).Should().Be(
            schema.DefaultHeight,
            "the default (Md) variant is exactly what the 52 px default box is sized for");
        (labelLine + lgTray).Should().BeGreaterThan(
            schema.DefaultHeight,
            "known limitation: Lg + label draws 60 px in a 52 px box, so the host has to make it taller");
    }

    [Fact]
    public void TheComponentsTypeNameResolvesItsRole()
    {
        var vocabulary = new UiRoleVocabulary([new BuiltInUiRoleVocabularySource()]);

        vocabulary.Find("TmSegmentedControl")!.Slug.Should().Be("segmented-control");
        vocabulary.Find("segmented-control")!.DisplayName.Should().Be("Segmented control");
        vocabulary.Find("segmented buttons")!.Slug.Should().Be("segmented-control");
        vocabulary.Find("prepinac segmentu")!.Slug.Should().Be("segmented-control");
    }

    [Fact]
    public void StencilPack_RendersTheComponent_AndAgreesWithTheSchema()
    {
        var definition = new BuiltInStencilPackProvider().GetDefinitions().Single(d => d.Type == "TmSegmentedControl");
        var schema = new BuiltInComponentSchemas().GetSchemas().Single(s => s.Type == "TmSegmentedControl");

        definition.IsBuiltIn.Should().BeTrue();
        definition.PackId.Should().Be("tempo");
        definition.NativeType.Should().BeNull("the control is a declarative stencil, not a native renderer");
        definition.Roles.Should().BeEquivalentTo(schema.Roles);
        definition.DefaultWidth.Should().Be(schema.DefaultWidth);
        definition.DefaultHeight.Should().Be(schema.DefaultHeight);
        definition.Props.Select(prop => prop.Name).Should().Equal(schema.Props.Select(prop => prop.Name));
        definition.RenderSvg.Should().NotBeNull("the declarative pack render is what the editor canvas paints");
    }

    [Fact]
    public void SegmentedControlPack_DrawsEveryPropItOffers()
    {
        var entry = Entry();

        entry.Should().Contain("{label", "the tray is named by its label");
        entry.Should().Contain("{option}", "one segment per option");
        entry.Should().Contain("repeat.index == (active", "the selected segment is the one the host marks");
        entry.Should().Contain("sizeVariant == \\\"Lg\\\"", "the size variant changes the segment height");
        entry.Should().Contain("{showCount", "the count suffix is optional");
        entry.Should().Contain("{badge", "the badge word is optional");
        entry.Should().Contain("{disabled ? 0.45 : 1}", "a disabled control paints lighter");
    }

    [Fact]
    public void SegmentedControlPack_DrawsTheSelectionAsASurface_NotOnlyAsAColour()
    {
        var entry = Entry();

        entry.Should().Contain("token(\\\"color.surface", "the selected segment is a raised surface");
        entry.Should().Contain("token(\\\"color.border", "and it carries an edge, so the difference is not hue alone");
        entry.Should().Contain("token(\\\"color.text.muted", "the unselected labels stay secondary");
    }

    private static string Entry() => BuiltInStencilPackProvider.ReadPackJson()
        .Split('\n')
        .Single(line => line.Contains("\"type\": \"TmSegmentedControl\""));
}
