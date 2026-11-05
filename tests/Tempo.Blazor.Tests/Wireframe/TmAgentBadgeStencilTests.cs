using FluentAssertions;
using Tempo.Blazor.Components.Wireframe;
using Tempo.Blazor.Components.Wireframe.Models;
using Xunit;

namespace Tempo.Blazor.Tests.Wireframe;

/// <summary>
/// <c>TmAgentBadge</c> is a wireframe citizen: an agent lane, a graph sketch or a fan-out mock is drawn by dropping
/// agent badges onto the page, so the palette and the MCP catalogue need the same vocabulary the component library
/// ships — a UI role, a declarative stencil and a schema whose props the pack draws. Without this entry a designer
/// would have to fake an agent with a generic avatar plus a badge and lose the palette/state meaning the component
/// carries.
/// </summary>
public sealed class TmAgentBadgeStencilTests
{
    private static readonly string[] BadgeProps =
        ["initials", "name", "status", "showName", "showStatus"];

    [Fact]
    public void BuiltInSchema_ListsTheBadge_WithItsRoleAndProps()
    {
        var schema = new BuiltInComponentSchemas().GetSchemas().Single(s => s.Type == "TmAgentBadge");

        schema.Category.Should().Be("AI");
        schema.DisplayName.Should().Be("Agent Badge");
        schema.Roles.Should().BeEquivalentTo("agent-badge");
        schema.Props.Select(prop => prop.Name).Should().Equal(BadgeProps);
        schema.Props.Single(prop => prop.Name == "status").Options.Should()
            .Equal("idle", "queued", "running", "approval", "succeeded", "failed", "cancelled");
        schema.DefaultWidth.Should().BePositive();
        schema.DefaultHeight.Should().BePositive();
    }

    [Fact]
    public void ThePropNamesAvoidTheStencilExpressionContext()
    {
        var schema = new BuiltInComponentSchemas().GetSchemas().Single(s => s.Type == "TmAgentBadge");

        schema.Props.Should().NotContain(prop => prop.Name == "size",
            "the stencil expression context already owns `size` for the element box");
        schema.Props.Should().NotContain(prop => prop.Name == "repeat",
            "and `repeat` belongs to the repeat node");
    }

    [Fact]
    public void TheComponentsTypeNameResolvesItsRole()
    {
        var vocabulary = new UiRoleVocabulary([new BuiltInUiRoleVocabularySource()]);

        vocabulary.Find("TmAgentBadge")!.Slug.Should().Be("agent-badge");
        vocabulary.Find("agent-badge")!.DisplayName.Should().Be("Agent badge");
        vocabulary.Find("agent status")!.Slug.Should().Be("agent-badge");
        vocabulary.Find("stitek agenta")!.Slug.Should().Be("agent-badge");
    }

    [Fact]
    public void TheRoleIsInTheBuiltInVocabulary()
    {
        var vocabulary = new UiRoleVocabulary([new BuiltInUiRoleVocabularySource()]);
        var schema = new BuiltInComponentSchemas().GetSchemas().Single(s => s.Type == "TmAgentBadge");

        schema.Roles!.Where(role => vocabulary.Find(role) is null).Should().BeEmpty();
    }

    [Fact]
    public void StencilPack_RendersTheBadge_AndAgreesWithTheSchema()
    {
        var definition = new BuiltInStencilPackProvider().GetDefinitions()
            .Single(definition => definition.Type == "TmAgentBadge");
        var schema = new BuiltInComponentSchemas().GetSchemas().Single(s => s.Type == "TmAgentBadge");

        definition.IsBuiltIn.Should().BeTrue();
        definition.PackId.Should().Be("tempo");
        definition.NativeType.Should().BeNull("the badge is a declarative stencil, not a native renderer");
        definition.Roles.Should().BeEquivalentTo(schema.Roles);
        definition.DefaultWidth.Should().Be(schema.DefaultWidth);
        definition.DefaultHeight.Should().Be(schema.DefaultHeight);
        definition.Props.Select(prop => prop.Name).Should().Equal(BadgeProps);
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
    public void TheBadgePack_DrawsEveryPropItOffers()
    {
        var entry = Entry("TmAgentBadge");

        entry.Should().Contain("{initials", "the avatar glyph is the initials prop");
        entry.Should().Contain("{name", "the label is the name prop");
        entry.Should().Contain("{status", "the chip is named by its state");
        entry.Should().Contain("showName", "the label is optional");
        entry.Should().Contain("showStatus", "the state chip is optional");
    }

    [Fact]
    public void TheBadgePack_PaintsEveryStateTone()
    {
        var entry = Entry("TmAgentBadge");

        foreach (var tone in new[] { "idle", "queued", "running", "approval", "succeeded", "failed", "cancelled" })
        {
            entry.Should().Contain($"{tone}=token(",
                "the sketch has to show the tone of the {0} state, exactly as the component paints it", tone);
        }
    }

    private static string Entry(string type) => BuiltInStencilPackProvider.ReadPackJson()
        .Split('\n')
        .Single(line => line.Contains($"\"type\": \"{type}\""));
}
