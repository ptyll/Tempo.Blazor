using FluentAssertions;
using Tempo.Blazor.Components.Wireframe;
using Tempo.Blazor.Components.Wireframe.Models;
using Xunit;

namespace Tempo.Blazor.Tests.Wireframe;

/// <summary>
/// Plan ff043b2e phase 6: <c>TmTraceWaterfall</c> is a wireframe citizen — a new component needs a UI
/// role and a stencil (with a render that branches on the props the palette offers), or the palette
/// and the MCP catalogue drift away from the component library. The schema is the contract the
/// palette, the properties panel and the stencil pack share, so the pack's props must match it
/// exactly, in order.
/// </summary>
public sealed class TraceWaterfallStencilTests
{
    private static readonly string[] ExpectedProps =
        ["title", "expanded", "showZoom", "showMetrics", "live"];

    [Fact]
    public void BuiltInSchema_ListsTheTraceWaterfall_WithItsRoleAndProps()
    {
        var schema = new BuiltInComponentSchemas().GetSchemas().Single(s => s.Type == "TmTraceWaterfall");

        schema.Category.Should().Be("Data Display");
        schema.DisplayName.Should().Be("Trace Waterfall");
        schema.Roles.Should().BeEquivalentTo("trace-waterfall");
        schema.Props.Select(prop => prop.Name).Should().Equal(ExpectedProps);
        schema.DefaultWidth.Should().BePositive();
        schema.DefaultHeight.Should().BePositive();
    }

    [Fact]
    public void TheComponentsTypeNameResolvesItsRole()
    {
        var vocabulary = new UiRoleVocabulary([new BuiltInUiRoleVocabularySource()]);

        vocabulary.Find("TmTraceWaterfall")!.Slug.Should().Be("trace-waterfall");
        vocabulary.Find("trace-waterfall")!.DisplayName.Should().Be("Trace waterfall");
    }

    [Fact]
    public void StencilPack_RendersTheComponent_AndAgreesWithTheSchema()
    {
        var definition = new BuiltInStencilPackProvider().GetDefinitions().Single(d => d.Type == "TmTraceWaterfall");
        var schema = new BuiltInComponentSchemas().GetSchemas().Single(s => s.Type == "TmTraceWaterfall");

        definition.IsBuiltIn.Should().BeTrue();
        definition.PackId.Should().Be("tempo");
        definition.NativeType.Should().BeNull("the waterfall is a declarative stencil, not a native renderer");
        definition.Roles.Should().BeEquivalentTo(schema.Roles);
        definition.DefaultWidth.Should().Be(schema.DefaultWidth);
        definition.DefaultHeight.Should().Be(schema.DefaultHeight);
        definition.Props.Select(prop => prop.Name).Should().Equal(schema.Props.Select(prop => prop.Name));
        definition.RenderSvg.Should().NotBeNull("the declarative pack render is what the editor canvas paints");
    }

    [Fact]
    public void TraceWaterfallPack_RendersTheNewPartsConditionally()
    {
        var entry = Entry();

        entry.Should().Contain("{title", "the card is named by its title");
        entry.Should().Contain("{expanded}", "an expanded waterfall draws the nested spans");
        entry.Should().Contain("{!expanded}", "a collapsed one draws the summary instead");
        entry.Should().Contain("{showZoom}", "the zoom controls only exist when they are offered");
        entry.Should().Contain("{showMetrics}", "the duration column is optional");
        entry.Should().Contain("{live}", "the live badge is opt-in");
        entry.Should().Contain("Error", "an error span shows text, never colour alone");
    }

    private static string Entry() => BuiltInStencilPackProvider.ReadPackJson()
        .Split('\n')
        .Single(line => line.Contains("\"type\": \"TmTraceWaterfall\""));
}
