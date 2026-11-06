using FluentAssertions;
using Tempo.Blazor.Components.Wireframe;
using Tempo.Blazor.Components.Wireframe.Models;
using Xunit;

namespace Tempo.Blazor.Tests.Wireframe;

/// <summary>
/// <c>TmConflictRadar</c> is a wireframe citizen: a designer sketching how two agents collide on one branch needs the
/// file × agent grid, the conflict called out in words and the settle/merge affordances — not a stack of identical
/// rectangles. The palette and the MCP catalogue therefore ship the same vocabulary the component does: a UI role, a
/// declarative stencil and a schema whose props the pack draws.
/// </summary>
public sealed class TmConflictRadarStencilTests
{
    private static readonly string[] RadarProps =
        ["title", "agents", "files", "conflicts", "resolved", "conflictsOnly"];

    [Fact]
    public void BuiltInSchema_ListsTheRadar_WithItsRoleAndProps()
    {
        var schema = new BuiltInComponentSchemas().GetSchemas().Single(s => s.Type == "TmConflictRadar");

        schema.Category.Should().Be("AI");
        schema.DisplayName.Should().Be("Conflict Radar");
        schema.Roles.Should().BeEquivalentTo("conflict-radar");
        schema.Props.Select(prop => prop.Name).Should().Equal(RadarProps);
        schema.Props.Single(prop => prop.Name == "agents").Type.Should().Be(PropType.StringList);
        schema.Props.Single(prop => prop.Name == "files").Type.Should().Be(PropType.StringList);
        schema.Props.Single(prop => prop.Name == "conflictsOnly").Type.Should().Be(PropType.Bool);
        schema.DefaultWidth.Should().BePositive();
        schema.DefaultHeight.Should().BePositive();
    }

    [Fact]
    public void ThePropNamesAvoidTheStencilExpressionContext()
    {
        var schema = new BuiltInComponentSchemas().GetSchemas().Single(s => s.Type == "TmConflictRadar");

        schema.Props.Should().NotContain(prop => prop.Name == "size");
        schema.Props.Should().NotContain(prop => prop.Name == "repeat");
    }

    [Fact]
    public void TheComponentsTypeNameResolvesItsRole()
    {
        var vocabulary = new UiRoleVocabulary([new BuiltInUiRoleVocabularySource()]);

        vocabulary.Find("TmConflictRadar")!.Slug.Should().Be("conflict-radar");
        vocabulary.Find("conflict-radar")!.DisplayName.Should().Be("Conflict radar");
        vocabulary.Find("branch conflicts")!.Slug.Should().Be("conflict-radar");
        vocabulary.Find("matice konfliktu")!.Slug.Should().Be("conflict-radar");
    }

    [Fact]
    public void TheRoleIsInTheBuiltInVocabulary()
    {
        var vocabulary = new UiRoleVocabulary([new BuiltInUiRoleVocabularySource()]);
        var schema = new BuiltInComponentSchemas().GetSchemas().Single(s => s.Type == "TmConflictRadar");

        schema.Roles!.Where(role => vocabulary.Find(role) is null).Should().BeEmpty();
    }

    [Fact]
    public void StencilPack_RendersTheRadar_AndAgreesWithTheSchema()
    {
        var definition = new BuiltInStencilPackProvider().GetDefinitions()
            .Single(definition => definition.Type == "TmConflictRadar");
        var schema = new BuiltInComponentSchemas().GetSchemas().Single(s => s.Type == "TmConflictRadar");

        definition.IsBuiltIn.Should().BeTrue();
        definition.PackId.Should().Be("tempo");
        definition.NativeType.Should().BeNull("the radar is a declarative stencil, not a native renderer");
        definition.Roles.Should().BeEquivalentTo(schema.Roles);
        definition.DefaultWidth.Should().Be(schema.DefaultWidth);
        definition.DefaultHeight.Should().Be(schema.DefaultHeight);
        definition.Props.Select(prop => prop.Name).Should().Equal(RadarProps);
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
    public void TheRadarPack_DrawsEveryPropItOffers()
    {
        var entry = Entry();

        entry.Should().Contain("{title", "the heading is the title prop");
        entry.Should().Contain("\"prop\": \"agents\"", "one column per agent, over the agents prop");
        entry.Should().Contain("{agent}", "and each column paints its own agent");
        entry.Should().Contain("\"prop\": \"files\"", "one row per changed file, over the files prop");
        entry.Should().Contain("{file}", "and each row paints its own file");
        entry.Should().Contain("{conflictsOnly", "the filter chip follows the conflictsOnly prop");
        entry.Should().Contain("{resolved", "the merge hint follows the resolved prop");
    }

    [Fact]
    public void TheRadarPack_SketchesTheGridTheConflictWordAndTheMergeAction()
    {
        var entry = Entry();

        entry.Should().Contain("File", "the file column header is part of the sketch");
        entry.Should().Contain("Modified", "a change is drawn as a word, not only a tint");
        entry.Should().Contain("Renamed");
        entry.Should().Contain("Deleted");
        entry.Should().Contain("Conflict", "a conflicting row is named, not merely coloured");
        entry.Should().Contain("Merge", "the merge action belongs in the sketch");
        entry.Should().Contain("merge by hand", "the side choice is named in words");
        entry.Should().Contain("token(\\\"color.danger", "a conflict is sketched in the danger tone");
    }

    private static string Entry() => BuiltInStencilPackProvider.ReadPackJson()
        .Split('\n')
        .Single(line => line.Contains("\"type\": \"TmConflictRadar\""));
}
