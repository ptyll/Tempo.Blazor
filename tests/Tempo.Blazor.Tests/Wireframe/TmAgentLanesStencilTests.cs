using FluentAssertions;
using Tempo.Blazor.Components.Wireframe;
using Tempo.Blazor.Components.Wireframe.Models;
using Xunit;

namespace Tempo.Blazor.Tests.Wireframe;

/// <summary>
/// <c>TmAgentLanes</c> is a wireframe citizen: a designer sketching a multi-agent run needs lanes, handoffs and the
/// "now" line, not a row of generic rectangles. The palette and the MCP catalogue therefore ship the same vocabulary
/// the component does — a UI role, a declarative stencil and a schema whose props the pack draws.
/// </summary>
public sealed class TmAgentLanesStencilTests
{
    private static readonly string[] LanesProps =
        ["title", "blocks", "viewMode", "showNowLine", "showZoom"];

    [Fact]
    public void BuiltInSchema_ListsTheLanes_WithItsRoleAndProps()
    {
        var schema = new BuiltInComponentSchemas().GetSchemas().Single(s => s.Type == "TmAgentLanes");

        schema.Category.Should().Be("AI");
        schema.DisplayName.Should().Be("Agent Lanes");
        schema.Roles.Should().BeEquivalentTo("agent-lanes");
        schema.Props.Select(prop => prop.Name).Should().Equal(LanesProps);
        schema.Props.Single(prop => prop.Name == "viewMode").Options.Should().Equal("timeline", "table");
        schema.DefaultWidth.Should().BePositive();
        schema.DefaultHeight.Should().BePositive();
    }

    [Fact]
    public void ThePropNamesAvoidTheStencilExpressionContext()
    {
        var schema = new BuiltInComponentSchemas().GetSchemas().Single(s => s.Type == "TmAgentLanes");

        schema.Props.Should().NotContain(prop => prop.Name == "size",
            "the stencil expression context already owns `size` for the element box");
        schema.Props.Should().NotContain(prop => prop.Name == "repeat",
            "and `repeat` belongs to the repeat node");
    }

    [Fact]
    public void TheComponentsTypeNameResolvesItsRole()
    {
        var vocabulary = new UiRoleVocabulary([new BuiltInUiRoleVocabularySource()]);

        vocabulary.Find("TmAgentLanes")!.Slug.Should().Be("agent-lanes");
        vocabulary.Find("agent-lanes")!.DisplayName.Should().Be("Agent lanes");
        vocabulary.Find("agent timeline")!.Slug.Should().Be("agent-lanes");
        vocabulary.Find("drahy agentu")!.Slug.Should().Be("agent-lanes");
    }

    [Fact]
    public void TheRoleIsInTheBuiltInVocabulary()
    {
        var vocabulary = new UiRoleVocabulary([new BuiltInUiRoleVocabularySource()]);
        var schema = new BuiltInComponentSchemas().GetSchemas().Single(s => s.Type == "TmAgentLanes");

        schema.Roles!.Where(role => vocabulary.Find(role) is null).Should().BeEmpty();
    }

    [Fact]
    public void StencilPack_RendersTheLanes_AndAgreesWithTheSchema()
    {
        var definition = new BuiltInStencilPackProvider().GetDefinitions()
            .Single(definition => definition.Type == "TmAgentLanes");
        var schema = new BuiltInComponentSchemas().GetSchemas().Single(s => s.Type == "TmAgentLanes");

        definition.IsBuiltIn.Should().BeTrue();
        definition.PackId.Should().Be("tempo");
        definition.NativeType.Should().BeNull("the lanes are a declarative stencil, not a native renderer");
        definition.Roles.Should().BeEquivalentTo(schema.Roles);
        definition.DefaultWidth.Should().Be(schema.DefaultWidth);
        definition.DefaultHeight.Should().Be(schema.DefaultHeight);
        definition.Props.Select(prop => prop.Name).Should().Equal(LanesProps);
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
    public void TheLanesPack_DrawsEveryPropItOffers()
    {
        var entry = Entry("TmAgentLanes");

        entry.Should().Contain("{title", "the heading is the title prop");
        entry.Should().Contain("\"prop\": \"blocks\"", "the work blocks are the blocks prop");
        entry.Should().Contain("{block}", "and each block paints its own text");
        entry.Should().Contain("viewMode", "the view switcher follows the viewMode prop");
        entry.Should().Contain("showNowLine", "the now line is optional");
        entry.Should().Contain("showZoom", "and so are the zoom controls");
    }

    [Fact]
    public void TheLanesPack_SketchesTheHandoffAndTheNowLine()
    {
        var entry = Entry("TmAgentLanes");

        entry.Should().Contain("\"kind\": \"line\"",
            "the handoff between two lanes is drawn as an elbow, not implied");
        entry.Should().Contain("Now",
            "the current moment is labelled, exactly as the component labels it");
        entry.Should().Contain("token(\\\"color.warning",
            "a block waiting for approval is sketched in the attention tone — the state the component paints");
    }

    private static string Entry(string type) => BuiltInStencilPackProvider.ReadPackJson()
        .Split('\n')
        .Single(line => line.Contains($"\"type\": \"{type}\""));
}
