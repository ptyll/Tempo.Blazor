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
        schema.DefaultWidth.Should().BePositive();
        schema.DefaultHeight.Should().BePositive();
    }

    [Fact]
    public void ThePropNamesAvoidTheStencilExpressionContext()
    {
        var schema = new BuiltInComponentSchemas().GetSchemas().Single(s => s.Type == "TmSegmentedControl");

        schema.Props.Should().NotContain(prop => prop.Name == "size",
            "the stencil expression context already owns `size` for the element box — a prop of that name would shadow it");
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
