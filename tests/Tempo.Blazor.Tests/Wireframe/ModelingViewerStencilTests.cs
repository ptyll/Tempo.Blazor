using FluentAssertions;
using Tempo.Blazor.Components.Wireframe;
using Tempo.Blazor.Components.Wireframe.Models;
using Xunit;

namespace Tempo.Blazor.Tests.Wireframe;

/// <summary>
/// Plan eadbf967 phase 6: <c>TmModelingViewer</c> is a wireframe citizen — the new read-only viewer
/// needs a UI role, a component schema and a declarative stencil, or the palette and the MCP
/// catalogue drift away from the component library. The schema is the contract the palette, the
/// properties panel and the stencil pack share, so the pack's props must match it exactly, in order.
/// </summary>
public sealed class ModelingViewerStencilTests
{
    private static readonly string[] ExpectedProps =
        ["modelId", "viewId", "fitMode", "showLegend"];

    [Fact]
    public void BuiltInSchema_ListsTheModelingViewer_WithItsRoleAndProps()
    {
        var schema = new BuiltInComponentSchemas().GetSchemas().Single(s => s.Type == "TmModelingViewer");

        schema.Category.Should().Be("Editors & Apps");
        schema.DisplayName.Should().Be("Modeling Viewer");
        schema.Roles.Should().BeEquivalentTo("modeling-viewer");
        schema.Props.Select(prop => prop.Name).Should().Equal(ExpectedProps);
        schema.DefaultWidth.Should().BePositive();
        schema.DefaultHeight.Should().BePositive();
    }

    [Fact]
    public void TheComponentsTypeNameResolvesItsRole()
    {
        var vocabulary = new UiRoleVocabulary([new BuiltInUiRoleVocabularySource()]);

        vocabulary.Find("TmModelingViewer")!.Slug.Should().Be("modeling-viewer");
        vocabulary.Find("modeling-viewer")!.DisplayName.Should().Be("Modeling viewer");
    }

    [Fact]
    public void StencilPack_RendersTheComponent_AndAgreesWithTheSchema()
    {
        var definition = new BuiltInStencilPackProvider().GetDefinitions().Single(d => d.Type == "TmModelingViewer");
        var schema = new BuiltInComponentSchemas().GetSchemas().Single(s => s.Type == "TmModelingViewer");

        definition.IsBuiltIn.Should().BeTrue();
        definition.PackId.Should().Be("tempo");
        definition.NativeType.Should().BeNull("the viewer is a declarative stencil, not a native renderer");
        definition.Roles.Should().BeEquivalentTo(schema.Roles);
        definition.DefaultWidth.Should().Be(schema.DefaultWidth);
        definition.DefaultHeight.Should().Be(schema.DefaultHeight);
        definition.Props.Select(prop => prop.Name).Should().Equal(schema.Props.Select(prop => prop.Name));
        definition.RenderSvg.Should().NotBeNull("the declarative pack render is what the editor canvas paints");
    }

    [Fact]
    public void ModelingViewerPack_RendersTheViewerPartsConditionally()
    {
        var entry = Entry();

        entry.Should().Contain("{modelId", "the viewer names its model");
        entry.Should().Contain("{viewId", "the projected view is shown");
        entry.Should().Contain("{fitMode", "the fit mode is shown");
        entry.Should().Contain("{showLegend}", "the legend row is optional");
        entry.Should().Contain("Heatmap", "the overlay is part of the visual identity");
        entry.Should().Contain("Diff", "the diff state is part of the visual identity");
    }

    private static string Entry() => BuiltInStencilPackProvider.ReadPackJson()
        .Split('\n')
        .Single(line => line.Contains("\"type\": \"TmModelingViewer\""));
}
