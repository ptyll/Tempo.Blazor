using FluentAssertions;
using Tempo.Blazor.Components.Wireframe;
using Tempo.Blazor.Components.Wireframe.Models;
using Xunit;

namespace Tempo.Blazor.Tests.Wireframe;

/// <summary>
/// Plan 3e96f6d8 phase 3: <c>TmAgentRunTree</c> and <c>TmAgentMonitor</c> are wireframe citizens — each new
/// component needs a UI role and a declarative stencil, or the palette and the MCP catalogue drift away from
/// the component library. The schema is the contract the palette, the properties panel and the stencil pack
/// share, so the pack's props must match it exactly, in order.
/// </summary>
public sealed class AgentRunStencilTests
{
    private static readonly string[] TreeProps =
        ["title", "nodes", "filter", "showModel", "showTime", "showCost", "showTokens"];

    private static readonly string[] MonitorProps =
        ["title", "steps", "state", "showTimeline", "showCost", "collapsed"];

    [Fact]
    public void BuiltInSchema_ListsTheRunTree_WithItsRoleAndProps()
    {
        var schema = new BuiltInComponentSchemas().GetSchemas().Single(s => s.Type == "TmAgentRunTree");

        schema.Category.Should().Be("AI");
        schema.DisplayName.Should().Be("Agent Run Tree");
        schema.Roles.Should().BeEquivalentTo("agent-run-tree");
        schema.Props.Select(prop => prop.Name).Should().Equal(TreeProps);
        schema.Props.Single(prop => prop.Name == "filter").Options.Should().Equal("all", "running", "failed");
        schema.DefaultWidth.Should().BePositive();
        schema.DefaultHeight.Should().BePositive();
    }

    [Fact]
    public void BuiltInSchema_ListsTheMonitor_WithItsRoleAndProps()
    {
        var schema = new BuiltInComponentSchemas().GetSchemas().Single(s => s.Type == "TmAgentMonitor");

        schema.Category.Should().Be("AI");
        schema.DisplayName.Should().Be("Agent Monitor");
        schema.Roles.Should().BeEquivalentTo("agent-monitor");
        schema.Props.Select(prop => prop.Name).Should().Equal(MonitorProps);
        schema.Props.Single(prop => prop.Name == "state").Options.Should()
            .Equal("running", "done", "failed", "pending");
        schema.DefaultWidth.Should().BePositive();
        schema.DefaultHeight.Should().BePositive();
    }

    [Fact]
    public void ThePropNamesAvoidTheStencilExpressionContext()
    {
        var schemas = new BuiltInComponentSchemas().GetSchemas()
            .Where(schema => schema.Type is "TmAgentRunTree" or "TmAgentMonitor");

        foreach (var schema in schemas)
        {
            schema.Props.Should().NotContain(prop => prop.Name == "size",
                "the stencil expression context already owns `size` for the element box");
            schema.Props.Should().NotContain(prop => prop.Name == "repeat",
                "and `repeat` belongs to the repeat node");
        }
    }

    [Fact]
    public void TheComponentsTypeNameResolvesItsRole()
    {
        var vocabulary = new UiRoleVocabulary([new BuiltInUiRoleVocabularySource()]);

        vocabulary.Find("TmAgentRunTree")!.Slug.Should().Be("agent-run-tree");
        vocabulary.Find("agent-run-tree")!.DisplayName.Should().Be("Agent run tree");
        vocabulary.Find("run tree")!.Slug.Should().Be("agent-run-tree");

        vocabulary.Find("TmAgentMonitor")!.Slug.Should().Be("agent-monitor");
        vocabulary.Find("agent-monitor")!.DisplayName.Should().Be("Agent monitor");
        vocabulary.Find("run monitor")!.Slug.Should().Be("agent-monitor");
    }

    [Fact]
    public void EveryRoleOfBothComponents_IsInTheBuiltInVocabulary()
    {
        var vocabulary = new UiRoleVocabulary([new BuiltInUiRoleVocabularySource()]);
        var schemas = new BuiltInComponentSchemas().GetSchemas()
            .Where(schema => schema.Type is "TmAgentRunTree" or "TmAgentMonitor");

        schemas.SelectMany(schema => schema.Roles!).Where(role => vocabulary.Find(role) is null)
            .Should().BeEmpty();
    }

    [Fact]
    public void StencilPack_RendersBothComponents_AndAgreesWithTheSchema()
    {
        var provider = new BuiltInStencilPackProvider();

        foreach (var (type, props) in new[]
                 {
                     ("TmAgentRunTree", TreeProps),
                     ("TmAgentMonitor", MonitorProps),
                 })
        {
            var definition = provider.GetDefinitions().Single(d => d.Type == type);
            var schema = new BuiltInComponentSchemas().GetSchemas().Single(s => s.Type == type);

            definition.IsBuiltIn.Should().BeTrue();
            definition.PackId.Should().Be("tempo");
            definition.NativeType.Should().BeNull($"{type} is a declarative stencil, not a native renderer");
            definition.Roles.Should().BeEquivalentTo(schema.Roles);
            definition.DefaultWidth.Should().Be(schema.DefaultWidth);
            definition.DefaultHeight.Should().Be(schema.DefaultHeight);
            definition.Props.Select(prop => prop.Name).Should().Equal(props);
            definition.RenderSvg.Should().NotBeNull("the declarative pack render is what the editor canvas paints");
        }
    }

    [Fact]
    public void ThePackCompilesWithoutWarnings()
    {
        var provider = new BuiltInStencilPackProvider();

        _ = provider.GetDefinitions().ToList();

        provider.ValidationWarnings.Should().BeEmpty();
    }

    [Fact]
    public void RunTreePack_DrawsEveryPropItOffers()
    {
        var entry = Entry("TmAgentRunTree");

        entry.Should().Contain("{title", "the card is named by its title");
        entry.Should().Contain("{row}", "one row per entry of the nodes prop");
        entry.Should().Contain("filter == \\\"failed\\\"", "the filter changes the state glyphs and the footer");
        entry.Should().Contain("showModel", "the model column is optional");
        entry.Should().Contain("showCost", "the cost column is optional");
        entry.Should().Contain("showTokens", "the tokens column is optional");
    }

    [Fact]
    public void MonitorPack_DrawsEveryPropItOffers()
    {
        var entry = Entry("TmAgentMonitor");

        entry.Should().Contain("{title", "the bar is named by its title");
        entry.Should().Contain("{row}", "one line per entry of the steps prop");
        entry.Should().Contain("state == \\\"failed\\\"", "the state decides the glyph and the word");
        entry.Should().Contain("showTimeline", "the timeline is optional");
        entry.Should().Contain("showCost", "the cost metric is optional");
        entry.Should().Contain("collapsed", "the strip draws its folded bar");
    }

    [Fact]
    public void BothPacks_DrawTheStateAsGlyphAndWord_NotAsColourOnly()
    {
        Entry("TmAgentRunTree").Should().Contain("token(\\\"color.success",
            "a completed node carries the success tone");
        Entry("TmAgentRunTree").Should().Contain("token(\\\"color.danger",
            "a failed node carries the danger tone");
        Entry("TmAgentMonitor").Should().Contain("token(\\\"color.success");
        Entry("TmAgentMonitor").Should().Contain("token(\\\"color.danger");
        Entry("TmAgentMonitor").Should().Contain("Failed",
            "the state is a WORD in the bar, not only a coloured dot");
    }

    private static string Entry(string type) => BuiltInStencilPackProvider.ReadPackJson()
        .Split('\n')
        .Single(line => line.Contains($"\"type\": \"{type}\""));
}
