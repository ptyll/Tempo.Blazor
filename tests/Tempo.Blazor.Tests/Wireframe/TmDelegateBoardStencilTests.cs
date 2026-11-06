using FluentAssertions;
using Tempo.Blazor.Components.Wireframe;
using Tempo.Blazor.Components.Wireframe.Models;
using Xunit;

namespace Tempo.Blazor.Tests.Wireframe;

/// <summary>
/// <c>TmDelegateBoard</c> is a wireframe citizen: a designer sketching where work gets handed out needs the backlog, the
/// agent columns with their capacity, the full column and the Assign action — not a stack of identical bars. The palette
/// and the MCP catalogue therefore ship the same vocabulary the component does: a UI role, a declarative stencil and a
/// schema whose props the pack draws.
/// </summary>
public sealed class TmDelegateBoardStencilTests
{
    private static readonly string[] BoardProps = ["title", "tasks", "agents", "auto", "agentCount"];

    [Fact]
    public void BuiltInSchema_ListsTheBoard_WithItsRoleAndProps()
    {
        var schema = new BuiltInComponentSchemas().GetSchemas().Single(s => s.Type == "TmDelegateBoard");

        schema.Category.Should().Be("AI");
        schema.DisplayName.Should().Be("Delegate Board");
        schema.Roles.Should().BeEquivalentTo("delegate-board");
        schema.Props.Select(prop => prop.Name).Should().Equal(BoardProps);
        schema.Props.Single(prop => prop.Name == "tasks").Type.Should().Be(PropType.StringList);
        schema.Props.Single(prop => prop.Name == "agents").Type.Should().Be(PropType.StringList);
        schema.Props.Single(prop => prop.Name == "auto").Type.Should().Be(PropType.Bool);
        schema.DefaultWidth.Should().BePositive();
        schema.DefaultHeight.Should().BePositive();
    }

    [Fact]
    public void ThePropNamesAvoidTheStencilExpressionContext()
    {
        var schema = new BuiltInComponentSchemas().GetSchemas().Single(s => s.Type == "TmDelegateBoard");

        schema.Props.Should().NotContain(prop => prop.Name == "size");
        schema.Props.Should().NotContain(prop => prop.Name == "repeat");
    }

    [Fact]
    public void TheComponentsTypeNameResolvesItsRole()
    {
        var vocabulary = new UiRoleVocabulary([new BuiltInUiRoleVocabularySource()]);

        vocabulary.Find("TmDelegateBoard")!.Slug.Should().Be("delegate-board");
        vocabulary.Find("delegate-board")!.DisplayName.Should().Be("Delegate board");
        vocabulary.Find("assign tasks")!.Slug.Should().Be("delegate-board");
        vocabulary.Find("prirazeni ukolu")!.Slug.Should().Be("delegate-board");
        vocabulary.Find("TmDelegateBoard")!.Slug.Should().Be("delegate-board");
    }

    [Fact]
    public void TheRoleIsInTheBuiltInVocabulary()
    {
        var vocabulary = new UiRoleVocabulary([new BuiltInUiRoleVocabularySource()]);
        var schema = new BuiltInComponentSchemas().GetSchemas().Single(s => s.Type == "TmDelegateBoard");

        schema.Roles!.Where(role => vocabulary.Find(role) is null).Should().BeEmpty();
    }

    [Fact]
    public void StencilPack_RendersTheBoard_AndAgreesWithTheSchema()
    {
        var definition = new BuiltInStencilPackProvider().GetDefinitions()
            .Single(definition => definition.Type == "TmDelegateBoard");
        var schema = new BuiltInComponentSchemas().GetSchemas().Single(s => s.Type == "TmDelegateBoard");

        definition.IsBuiltIn.Should().BeTrue();
        definition.PackId.Should().Be("tempo");
        definition.NativeType.Should().BeNull("the board is a declarative stencil, not a native renderer");
        definition.Roles.Should().BeEquivalentTo(schema.Roles);
        definition.DefaultWidth.Should().Be(schema.DefaultWidth);
        definition.DefaultHeight.Should().Be(schema.DefaultHeight);
        definition.Props.Select(prop => prop.Name).Should().Equal(BoardProps);
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
    public void TheBoardPack_DrawsEveryPropItOffers()
    {
        var entry = Entry();

        entry.Should().Contain("{title", "the heading is the title prop");
        entry.Should().Contain("\"prop\": \"tasks\"", "the backlog is repeated over the tasks prop");
        entry.Should().Contain("{task}", "and each card paints its own task");
        entry.Should().Contain("\"prop\": \"agents\"", "one column per agent, over the agents prop");
        entry.Should().Contain("{agent}", "and each column paints its own agent");
        entry.Should().Contain("{auto", "the automatic switch follows the auto prop");
    }

    [Fact]
    public void TheBoardPack_SketchesTheColumnsTheFullStateAndTheAssignAction()
    {
        var entry = Entry();

        entry.Should().Contain("Backlog", "the backlog column is part of the sketch");
        entry.Should().Contain("Assign", "so is the per-card action");
        entry.Should().Contain("running", "the capacity line is drawn as a word");
        entry.Should().Contain("Full", "a full column is named, not merely tinted");
        entry.Should().Contain("Occupied until", "the occupied-until preview is part of the sketch");
        entry.Should().Contain("token(\\\"color.warning", "the full column is sketched in the warning tone");
    }

    private static string Entry() => BuiltInStencilPackProvider.ReadPackJson()
        .Split('\n')
        .Single(line => line.Contains("\"type\": \"TmDelegateBoard\""));
}
