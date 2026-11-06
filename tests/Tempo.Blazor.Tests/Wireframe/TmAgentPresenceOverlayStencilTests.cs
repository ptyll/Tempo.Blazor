using FluentAssertions;
using Tempo.Blazor.Components.Wireframe;
using Tempo.Blazor.Components.Wireframe.Models;
using Xunit;

namespace Tempo.Blazor.Tests.Wireframe;

/// <summary>
/// <c>TmAgentPresenceOverlay</c> is a wireframe citizen: a designer sketching a collaborative document needs the labelled
/// cursors over the content and the roster that names everyone present — not a generic card. The palette and the MCP
/// catalogue therefore ship the same vocabulary the component does: a UI role, a declarative stencil and a schema whose
/// props the pack draws.
/// </summary>
public sealed class TmAgentPresenceOverlayStencilTests
{
    private static readonly string[] PresenceProps = ["title", "markers", "showRoster"];

    [Fact]
    public void BuiltInSchema_ListsTheOverlay_WithItsRoleAndProps()
    {
        var schema = new BuiltInComponentSchemas().GetSchemas().Single(s => s.Type == "TmAgentPresenceOverlay");

        schema.Category.Should().Be("AI");
        schema.DisplayName.Should().Be("Agent Presence Overlay");
        schema.Roles.Should().BeEquivalentTo("agent-presence-overlay");
        schema.Props.Select(prop => prop.Name).Should().Equal(PresenceProps);
        schema.Props.Single(prop => prop.Name == "markers").Type.Should().Be(PropType.StringList);
        schema.Props.Single(prop => prop.Name == "showRoster").Type.Should().Be(PropType.Bool);
        schema.DefaultWidth.Should().BePositive();
        schema.DefaultHeight.Should().BePositive();
    }

    [Fact]
    public void ThePropNamesAvoidTheStencilExpressionContext()
    {
        var schema = new BuiltInComponentSchemas().GetSchemas().Single(s => s.Type == "TmAgentPresenceOverlay");

        schema.Props.Should().NotContain(prop => prop.Name == "size");
        schema.Props.Should().NotContain(prop => prop.Name == "repeat");
    }

    [Fact]
    public void TheComponentsTypeNameResolvesItsRole()
    {
        var vocabulary = new UiRoleVocabulary([new BuiltInUiRoleVocabularySource()]);

        vocabulary.Find("TmAgentPresenceOverlay")!.Slug.Should().Be("agent-presence-overlay");
        vocabulary.Find("agent-presence-overlay")!.DisplayName.Should().Be("Agent presence overlay");
        vocabulary.Find("collaborator cursors")!.Slug.Should().Be("agent-presence-overlay");
        vocabulary.Find("kurzory spolupracovniku")!.Slug.Should().Be("agent-presence-overlay");
    }

    [Fact]
    public void TheRoleIsInTheBuiltInVocabulary()
    {
        var vocabulary = new UiRoleVocabulary([new BuiltInUiRoleVocabularySource()]);
        var schema = new BuiltInComponentSchemas().GetSchemas().Single(s => s.Type == "TmAgentPresenceOverlay");

        schema.Roles!.Where(role => vocabulary.Find(role) is null).Should().BeEmpty();
    }

    [Fact]
    public void StencilPack_RendersTheOverlay_AndAgreesWithTheSchema()
    {
        var definition = new BuiltInStencilPackProvider().GetDefinitions()
            .Single(definition => definition.Type == "TmAgentPresenceOverlay");
        var schema = new BuiltInComponentSchemas().GetSchemas().Single(s => s.Type == "TmAgentPresenceOverlay");

        definition.IsBuiltIn.Should().BeTrue();
        definition.PackId.Should().Be("tempo");
        definition.NativeType.Should().BeNull("the overlay is a declarative stencil, not a native renderer");
        definition.Roles.Should().BeEquivalentTo(schema.Roles);
        definition.DefaultWidth.Should().Be(schema.DefaultWidth);
        definition.DefaultHeight.Should().Be(schema.DefaultHeight);
        definition.Props.Select(prop => prop.Name).Should().Equal(PresenceProps);
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
    public void TheOverlayPack_DrawsEveryPropItOffers()
    {
        var entry = Entry();

        entry.Should().Contain("{title", "the content label is the title prop");
        entry.Should().Contain("\"prop\": \"markers\"", "one cursor per present agent, over the markers prop");
        entry.Should().Contain("{marker}", "and each cursor paints its own agent");
        entry.Should().Contain("{showRoster", "the roster line follows the showRoster prop");
    }

    [Fact]
    public void TheOverlayPack_SketchesTheCursorsOverTheContentAndTheRoster()
    {
        var entry = Entry();

        entry.Should().Contain("\"kind\": \"path\"", "a cursor is drawn as a pointer, not as a box");
        entry.Should().Contain("Typing", "the activity is drawn as a word");
        entry.Should().Contain("Viewing");
        entry.Should().Contain("Present now", "the roster is part of the sketch");
        entry.Should().Contain("token(\\\"color.primary", "the first cursor carries the agent palette");
    }

    private static string Entry() => BuiltInStencilPackProvider.ReadPackJson()
        .Split('\n')
        .Single(line => line.Contains("\"type\": \"TmAgentPresenceOverlay\""));
}
