using FluentAssertions;
using Tempo.Blazor.Components.Wireframe;
using Tempo.Blazor.Components.Wireframe.Models;
using Xunit;

namespace Tempo.Blazor.Tests.Wireframe;

/// <summary>
/// Plan 3e96f6d8 phase 1: <c>TmSortableList</c> is a wireframe citizen — a new component needs a UI role and a
/// declarative stencil, or the palette and the MCP catalogue drift away from the component library. The schema
/// is the contract the palette, the properties panel and the stencil pack share, so the pack's props must match
/// it exactly, in order.
/// </summary>
public sealed class SortableListStencilTests
{
    private static readonly string[] ExpectedProps =
        ["title", "rows", "handleMode", "showMoveMenu", "disabled"];

    [Fact]
    public void BuiltInSchema_ListsTheSortableList_WithItsRoleAndProps()
    {
        var schema = new BuiltInComponentSchemas().GetSchemas().Single(s => s.Type == "TmSortableList");

        schema.Category.Should().Be("Data Display");
        schema.DisplayName.Should().Be("Sortable List");
        schema.Roles.Should().BeEquivalentTo("sortable-list");
        schema.Props.Select(prop => prop.Name).Should().Equal(ExpectedProps);
        schema.DefaultWidth.Should().BePositive();
        schema.DefaultHeight.Should().BePositive();
    }

    [Fact]
    public void ThePropNamesAvoidTheStencilExpressionContext()
    {
        var schema = new BuiltInComponentSchemas().GetSchemas().Single(s => s.Type == "TmSortableList");

        schema.Props.Should().NotContain(prop => prop.Name == "size",
            "the stencil expression context already owns `size` for the element box");
        schema.Props.Should().NotContain(prop => prop.Name == "repeat",
            "and `repeat` belongs to the repeat node");
    }

    [Fact]
    public void TheComponentsTypeNameResolvesItsRole()
    {
        var vocabulary = new UiRoleVocabulary([new BuiltInUiRoleVocabularySource()]);

        vocabulary.Find("TmSortableList")!.Slug.Should().Be("sortable-list");
        vocabulary.Find("sortable-list")!.DisplayName.Should().Be("Sortable list");
        vocabulary.Find("reorderable list")!.Slug.Should().Be("sortable-list");
        vocabulary.Find("raditelny seznam")!.Slug.Should().Be("sortable-list");
    }

    [Fact]
    public void StencilPack_RendersTheComponent_AndAgreesWithTheSchema()
    {
        var definition = new BuiltInStencilPackProvider().GetDefinitions().Single(d => d.Type == "TmSortableList");
        var schema = new BuiltInComponentSchemas().GetSchemas().Single(s => s.Type == "TmSortableList");

        definition.IsBuiltIn.Should().BeTrue();
        definition.PackId.Should().Be("tempo");
        definition.NativeType.Should().BeNull("the list is a declarative stencil, not a native renderer");
        definition.Roles.Should().BeEquivalentTo(schema.Roles);
        definition.DefaultWidth.Should().Be(schema.DefaultWidth);
        definition.DefaultHeight.Should().Be(schema.DefaultHeight);
        definition.Props.Select(prop => prop.Name).Should().Equal(schema.Props.Select(prop => prop.Name));
        definition.RenderSvg.Should().NotBeNull("the declarative pack render is what the editor canvas paints");
    }

    [Fact]
    public void ThePackCompilesWithoutWarnings()
    {
        var provider = new BuiltInStencilPackProvider();

        _ = provider.GetDefinitions().ToList();

        provider.ValidationWarnings.Should().BeEmpty();
    }

    [Fact]
    public void SortableListPack_DrawsEveryPropItOffers()
    {
        var entry = Entry();

        entry.Should().Contain("{title", "the card is named by its title");
        entry.Should().Contain("{row}", "one row per entry of the rows prop");
        entry.Should().Contain("handleMode", "the handle mode decides whether the grip is drawn");
        entry.Should().Contain("{showMoveMenu", "the move menu glyph is optional");
        entry.Should().Contain("{disabled ? 0.45 : 1}", "a read-only list paints lighter");
    }

    [Fact]
    public void SortableListPack_DrawsTheGrabbedStateAsASurface_NotOnlyAsAColour()
    {
        var entry = Entry();

        entry.Should().Contain("token(\\\"color.surface.muted", "the grabbed row is a raised surface");
        entry.Should().Contain("token(\\\"color.primary", "and it carries a primary edge, so the state is not hue alone");
        entry.Should().Contain("token(\\\"color.text.muted", "the secondary glyphs stay secondary");
    }

    private static string Entry() => BuiltInStencilPackProvider.ReadPackJson()
        .Split('\n')
        .Single(line => line.Contains("\"type\": \"TmSortableList\""));
}
