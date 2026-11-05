using FluentAssertions;
using Tempo.Blazor.Components.Wireframe;
using Tempo.Blazor.Components.Wireframe.Models;
using Xunit;

namespace Tempo.Blazor.Tests.Wireframe;

/// <summary>
/// <c>TmAgentHandoff</c> is a wireframe citizen: a designer sketching a multi-agent run needs the handoff chain with its
/// steps, its acceptance state and its two lists — not two generic rectangles. The palette and the MCP catalogue
/// therefore ship the same vocabulary the component does: a UI role, a declarative stencil and a schema whose props the
/// pack draws.
/// </summary>
public sealed class TmAgentHandoffStencilTests
{
    private static readonly string[] HandoffProps = ["title", "steps", "orientation", "viewMode", "showSummary"];

    [Fact]
    public void BuiltInSchema_ListsTheChain_WithItsRoleAndProps()
    {
        var schema = new BuiltInComponentSchemas().GetSchemas().Single(s => s.Type == "TmAgentHandoff");

        schema.Category.Should().Be("AI");
        schema.DisplayName.Should().Be("Agent Handoff");
        schema.Roles.Should().BeEquivalentTo("agent-handoff");
        schema.Props.Select(prop => prop.Name).Should().Equal(HandoffProps);
        schema.Props.Single(prop => prop.Name == "orientation").Options.Should().Equal("vertical", "horizontal");
        schema.Props.Single(prop => prop.Name == "viewMode").Options.Should().Equal("chain", "table");
        schema.DefaultWidth.Should().BePositive();
        schema.DefaultHeight.Should().BePositive();
    }

    [Fact]
    public void ThePropNamesAvoidTheStencilExpressionContext()
    {
        var schema = new BuiltInComponentSchemas().GetSchemas().Single(s => s.Type == "TmAgentHandoff");

        schema.Props.Should().NotContain(prop => prop.Name == "size",
            "the stencil expression context already owns `size` for the element box");
        schema.Props.Should().NotContain(prop => prop.Name == "repeat",
            "and `repeat` belongs to the repeat node");
    }

    [Fact]
    public void TheComponentsTypeNameResolvesItsRole()
    {
        var vocabulary = new UiRoleVocabulary([new BuiltInUiRoleVocabularySource()]);

        vocabulary.Find("TmAgentHandoff")!.Slug.Should().Be("agent-handoff");
        vocabulary.Find("agent-handoff")!.DisplayName.Should().Be("Agent handoff");
        vocabulary.Find("handoff chain")!.Slug.Should().Be("agent-handoff");
        vocabulary.Find("predani agenta")!.Slug.Should().Be("agent-handoff");
        vocabulary.Find("retez predani")!.Slug.Should().Be("agent-handoff");
    }

    [Fact]
    public void TheRoleIsInTheBuiltInVocabulary()
    {
        var vocabulary = new UiRoleVocabulary([new BuiltInUiRoleVocabularySource()]);
        var schema = new BuiltInComponentSchemas().GetSchemas().Single(s => s.Type == "TmAgentHandoff");

        schema.Roles!.Where(role => vocabulary.Find(role) is null).Should().BeEmpty();
    }

    [Fact]
    public void StencilPack_RendersTheChain_AndAgreesWithTheSchema()
    {
        var definition = new BuiltInStencilPackProvider().GetDefinitions()
            .Single(definition => definition.Type == "TmAgentHandoff");
        var schema = new BuiltInComponentSchemas().GetSchemas().Single(s => s.Type == "TmAgentHandoff");

        definition.IsBuiltIn.Should().BeTrue();
        definition.PackId.Should().Be("tempo");
        definition.NativeType.Should().BeNull("the chain is a declarative stencil, not a native renderer");
        definition.Roles.Should().BeEquivalentTo(schema.Roles);
        definition.DefaultWidth.Should().Be(schema.DefaultWidth);
        definition.DefaultHeight.Should().Be(schema.DefaultHeight);
        definition.Props.Select(prop => prop.Name).Should().Equal(HandoffProps);
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
    public void TheChainPack_DrawsEveryPropItOffers()
    {
        var entry = Entry();

        entry.Should().Contain("{title", "the heading is the title prop");
        entry.Should().Contain("\"prop\": \"steps\"", "the chain is repeated over the steps prop");
        entry.Should().Contain("{step}", "and each step paints its own text");
        entry.Should().Contain("{viewMode", "the view switcher follows the viewMode prop");
    }

    [Fact]
    public void TheChainPack_SketchesTheSummaryTheRouteTheStateAndTheTwoLists()
    {
        var entry = Entry();

        entry.Should().Contain("handoffs", "the summary line counts the handoffs");
        entry.Should().Contain("passed on", "and sums what travelled");
        entry.Should().Contain("lost");
        entry.Should().Contain("→", "a step's route is drawn");
        entry.Should().Contain("Accepted", "the acceptance state is drawn as a word");
        entry.Should().Contain("PASSED ON", "the passed list is part of the sketch");
        entry.Should().Contain("LOST", "and so is the lost list");
        entry.Should().Contain("context window was full", "a lost item keeps its reason");
        entry.Should().Contain("Details", "the details toggle is drawn, not implied");
        entry.Should().Contain("token(\\\"color.danger", "a loss is sketched in the danger tone");
        entry.Should().Contain("token(\\\"color.success", "an accepted step in the success tone");
    }

    private static string Entry() => BuiltInStencilPackProvider.ReadPackJson()
        .Split('\n')
        .Single(line => line.Contains("\"type\": \"TmAgentHandoff\""));
}
