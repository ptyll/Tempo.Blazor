using FluentAssertions;
using Tempo.Blazor.Components.Wireframe;
using Tempo.Blazor.Components.Wireframe.Models;
using Xunit;

namespace Tempo.Blazor.Tests.Wireframe;

/// <summary>
/// <c>TmAgentGraph</c> is a wireframe citizen: a designer sketching a multi-agent run needs agents, the tools they
/// call and the connection that carries the calls — not a row of generic rectangles. The palette and the MCP catalogue
/// therefore ship the same vocabulary the component does: a UI role, a declarative stencil and a schema whose props the
/// pack draws.
/// </summary>
public sealed class TmAgentGraphStencilTests
{
    private static readonly string[] GraphProps = ["title", "nodes", "viewMode", "showZoom"];

    [Fact]
    public void BuiltInSchema_ListsTheGraph_WithItsRoleAndProps()
    {
        var schema = new BuiltInComponentSchemas().GetSchemas().Single(s => s.Type == "TmAgentGraph");

        schema.Category.Should().Be("AI");
        schema.DisplayName.Should().Be("Agent Graph");
        schema.Roles.Should().BeEquivalentTo("agent-graph");
        schema.Props.Select(prop => prop.Name).Should().Equal(GraphProps);
        schema.Props.Single(prop => prop.Name == "viewMode").Options.Should().Equal("graph", "table");
        schema.DefaultWidth.Should().BePositive();
        schema.DefaultHeight.Should().BePositive();
    }

    [Fact]
    public void ThePropNamesAvoidTheStencilExpressionContext()
    {
        var schema = new BuiltInComponentSchemas().GetSchemas().Single(s => s.Type == "TmAgentGraph");

        schema.Props.Should().NotContain(prop => prop.Name == "size",
            "the stencil expression context already owns `size` for the element box");
        schema.Props.Should().NotContain(prop => prop.Name == "repeat",
            "and `repeat` belongs to the repeat node");
    }

    [Fact]
    public void TheComponentsTypeNameResolvesItsRole()
    {
        var vocabulary = new UiRoleVocabulary([new BuiltInUiRoleVocabularySource()]);

        vocabulary.Find("TmAgentGraph")!.Slug.Should().Be("agent-graph");
        vocabulary.Find("agent-graph")!.DisplayName.Should().Be("Agent graph");
        vocabulary.Find("agent network")!.Slug.Should().Be("agent-graph");
        vocabulary.Find("sit agentu")!.Slug.Should().Be("agent-graph");
    }

    [Fact]
    public void TheRoleIsInTheBuiltInVocabulary()
    {
        var vocabulary = new UiRoleVocabulary([new BuiltInUiRoleVocabularySource()]);
        var schema = new BuiltInComponentSchemas().GetSchemas().Single(s => s.Type == "TmAgentGraph");

        schema.Roles!.Where(role => vocabulary.Find(role) is null).Should().BeEmpty();
    }

    [Fact]
    public void StencilPack_RendersTheGraph_AndAgreesWithTheSchema()
    {
        var definition = new BuiltInStencilPackProvider().GetDefinitions()
            .Single(definition => definition.Type == "TmAgentGraph");
        var schema = new BuiltInComponentSchemas().GetSchemas().Single(s => s.Type == "TmAgentGraph");

        definition.IsBuiltIn.Should().BeTrue();
        definition.PackId.Should().Be("tempo");
        definition.NativeType.Should().BeNull("the graph is a declarative stencil, not a native renderer");
        definition.Roles.Should().BeEquivalentTo(schema.Roles);
        definition.DefaultWidth.Should().Be(schema.DefaultWidth);
        definition.DefaultHeight.Should().Be(schema.DefaultHeight);
        definition.Props.Select(prop => prop.Name).Should().Equal(GraphProps);
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
    public void TheGraphPack_DrawsEveryPropItOffers()
    {
        var entry = Entry();

        entry.Should().Contain("{title", "the heading is the title prop");
        entry.Should().Contain("\"prop\": \"nodes\"", "the called nodes are the nodes prop");
        entry.Should().Contain("{node}", "and each node paints its own text");
        entry.Should().Contain("viewMode", "the view switcher follows the viewMode prop");
        entry.Should().Contain("showZoom", "the zoom controls are optional");
    }

    [Fact]
    public void TheGraphPack_SketchesTheConnectionsAndTheWaitingNode()
    {
        var entry = Entry();

        entry.Should().Contain("\"kind\": \"line\"",
            "a connection between two boxes is drawn as a line, not implied");
        entry.Should().Contain("token(\\\"color.warning",
            "a node waiting for approval is sketched in the attention tone — the state the component paints");
        entry.Should().Contain("Waiting for approval",
            "and the state is spelled out, exactly as the component spells it");
        entry.Should().Contain("calls",
            "the call count a connection carries is part of the sketch");
    }

    private static string Entry() => BuiltInStencilPackProvider.ReadPackJson()
        .Split('\n')
        .Single(line => line.Contains("\"type\": \"TmAgentGraph\""));
}
