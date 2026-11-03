using FluentAssertions;
using Tempo.Blazor.Components.Wireframe;
using Xunit;

namespace Tempo.Blazor.Tests.Wireframe;

/// <summary>
/// Plan ff043b2e, phase 4: <c>TmFacetFilter</c> is a wireframe citizen — the palette, the MCP
/// catalogue and the component library must not drift, so the schema, the role vocabulary and the
/// declarative pack entry are pinned together. Every schema prop has to reach the drawing: a knob
/// the canvas ignores is a knob that does nothing, and a designer cannot tell what they are placing.
/// </summary>
public sealed class FacetFilterStencilTests
{
    [Fact]
    public void BuiltInSchema_ListsTheFacetFilter_WithItsRoleAndProps()
    {
        var schema = new BuiltInComponentSchemas().GetSchemas().Single(s => s.Type == "TmFacetFilter");

        schema.Category.Should().Be("Data Display");
        schema.DisplayName.Should().Be("Facet Filter");
        schema.Roles.Should().BeEquivalentTo("facet-filter");
        schema.Props.Select(prop => prop.Name).Should()
            .Equal("title", "presentation", "showSearch", "showCounts", "showChips", "collapsed");
        schema.DefaultWidth.Should().BePositive();
        schema.DefaultHeight.Should().BePositive();
    }

    [Fact]
    public void TheComponentTypeNameResolvesItsRole_SoEveryComponentHasOne()
    {
        var vocabulary = new UiRoleVocabulary([new BuiltInUiRoleVocabularySource()]);

        vocabulary.Find("TmFacetFilter")!.Slug.Should().Be("facet-filter");
    }

    [Fact]
    public void TheRoleIsSearchableByItsAliases()
    {
        var vocabulary = new UiRoleVocabulary([new BuiltInUiRoleVocabularySource()]);

        vocabulary.Find("facet filter").Should().NotBeNull();
        vocabulary.Find("filtr faset").Should().NotBeNull();
    }

    [Fact]
    public void StencilPack_RendersTheComponent_AndAgreesWithTheSchema()
    {
        var definition = new BuiltInStencilPackProvider().GetDefinitions().Single(d => d.Type == "TmFacetFilter");
        var schema = new BuiltInComponentSchemas().GetSchemas().Single(s => s.Type == "TmFacetFilter");

        definition.IsBuiltIn.Should().BeTrue();
        definition.PackId.Should().Be("tempo");
        definition.NativeType.Should().BeNull("the facet filter is a declarative stencil, not a native renderer");
        definition.Roles.Should().BeEquivalentTo(schema.Roles);
        definition.DefaultWidth.Should().Be(schema.DefaultWidth);
        definition.DefaultHeight.Should().Be(schema.DefaultHeight);
        definition.Props.Select(prop => prop.Name).Should().Equal(schema.Props.Select(prop => prop.Name));
        definition.RenderSvg.Should().NotBeNull("the declarative pack render is what the editor canvas paints");
    }

    [Fact]
    public void Pack_RendersEverySchemaPropConditionally()
    {
        var entry = Entry("TmFacetFilter");

        entry.Should().Contain("{title");
        entry.Should().Contain("{presentation", "the inline presentation has no trigger, the canvas has to show that");
        entry.Should().Contain("{showSearch");
        entry.Should().Contain("{showCounts");
        entry.Should().Contain("{showChips");
        entry.Should().Contain("{collapsed");
        entry.Should().Contain("token(", "the drawing paints with design tokens, never with raw palette steps");
    }

    private static string Entry(string type) => BuiltInStencilPackProvider.ReadPackJson()
        .Split('\n')
        .Single(line => line.Contains($"\"type\": \"{type}\""));
}
