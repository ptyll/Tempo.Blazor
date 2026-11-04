using FluentAssertions;
using Tempo.Blazor.Components.Wireframe;
using Tempo.Blazor.Components.Wireframe.Models;
using Xunit;

namespace Tempo.Blazor.Tests.Wireframe;

/// <summary>
/// Plan 3e96f6d8 phase 2: <c>TmAgentPlan</c> is a wireframe citizen — a new component needs a UI role and a
/// declarative stencil, or the palette and the MCP catalogue drift away from the component library. The
/// schema is the contract the palette, the properties panel and the stencil pack share, so the pack's props
/// must match it exactly, in order.
/// </summary>
public sealed class AgentPlanStencilTests
{
    private static readonly string[] ExpectedProps =
        ["title", "steps", "mode", "showSummary", "showAddStep"];

    [Fact]
    public void BuiltInSchema_ListsTheAgentPlan_WithItsRoleAndProps()
    {
        var schema = new BuiltInComponentSchemas().GetSchemas().Single(s => s.Type == "TmAgentPlan");

        schema.Category.Should().Be("AI");
        schema.DisplayName.Should().Be("Agent Plan");
        schema.Roles.Should().BeEquivalentTo("agent-plan");
        schema.Props.Select(prop => prop.Name).Should().Equal(ExpectedProps);
        schema.Props.Single(prop => prop.Name == "mode").Options.Should().Equal("review", "running");
        schema.DefaultWidth.Should().BePositive();
        schema.DefaultHeight.Should().BePositive();
    }

    [Fact]
    public void ThePropNamesAvoidTheStencilExpressionContext()
    {
        var schema = new BuiltInComponentSchemas().GetSchemas().Single(s => s.Type == "TmAgentPlan");

        schema.Props.Should().NotContain(prop => prop.Name == "size",
            "the stencil expression context already owns `size` for the element box");
        schema.Props.Should().NotContain(prop => prop.Name == "repeat",
            "and `repeat` belongs to the repeat node");
    }

    [Fact]
    public void TheComponentsTypeNameResolvesItsRole()
    {
        var vocabulary = new UiRoleVocabulary([new BuiltInUiRoleVocabularySource()]);

        vocabulary.Find("TmAgentPlan")!.Slug.Should().Be("agent-plan");
        vocabulary.Find("agent-plan")!.DisplayName.Should().Be("Agent plan");
        vocabulary.Find("step plan")!.Slug.Should().Be("agent-plan");
        vocabulary.Find("plan kroku")!.Slug.Should().Be("agent-plan");
    }

    [Fact]
    public void EveryRoleOfTheAgentPlan_IsInTheBuiltInVocabulary()
    {
        var vocabulary = new UiRoleVocabulary([new BuiltInUiRoleVocabularySource()]);
        var schema = new BuiltInComponentSchemas().GetSchemas().Single(s => s.Type == "TmAgentPlan");

        schema.Roles!.Where(role => vocabulary.Find(role) is null).Should().BeEmpty();
    }

    [Fact]
    public void StencilPack_RendersTheComponent_AndAgreesWithTheSchema()
    {
        var definition = new BuiltInStencilPackProvider().GetDefinitions().Single(d => d.Type == "TmAgentPlan");
        var schema = new BuiltInComponentSchemas().GetSchemas().Single(s => s.Type == "TmAgentPlan");

        definition.IsBuiltIn.Should().BeTrue();
        definition.PackId.Should().Be("tempo");
        definition.NativeType.Should().BeNull("the plan is a declarative stencil, not a native renderer");
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
    public void AgentPlanPack_DrawsEveryPropItOffers()
    {
        var entry = Entry();

        entry.Should().Contain("{title", "the card is named by its title");
        entry.Should().Contain("{row}", "one row per entry of the steps prop");
        entry.Should().Contain("mode == \\\"running\\\"", "the running plan draws its own summary state");
        entry.Should().Contain("showSummary", "the summary line is optional");
        entry.Should().Contain("showAddStep", "the add row belongs to review mode only");
    }

    [Fact]
    public void AgentPlanPack_DrawsTheStateAsGlyphAndWord_NotAsColourOnly()
    {
        var entry = Entry();

        // Review draws an empty circle and a dark word, running a filled dot, failed a cross — the sketch
        // must not lean on the row colour alone.
        entry.Should().Contain("token(\\\"color.success", "a done step carries the success tone");
        entry.Should().Contain("token(\\\"color.danger", "a failed step carries the danger tone");
        entry.Should().Contain("token(\\\"color.text.muted", "the pending glyphs stay secondary");
    }

    private static string Entry() => BuiltInStencilPackProvider.ReadPackJson()
        .Split('\n')
        .Single(line => line.Contains("\"type\": \"TmAgentPlan\""));
}
