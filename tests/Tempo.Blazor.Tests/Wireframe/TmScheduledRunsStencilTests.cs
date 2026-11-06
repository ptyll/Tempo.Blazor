using FluentAssertions;
using Tempo.Blazor.Components.Wireframe;
using Tempo.Blazor.Components.Wireframe.Models;
using Xunit;

namespace Tempo.Blazor.Tests.Wireframe;

/// <summary>
/// <c>TmScheduledRuns</c> is a wireframe citizen: a designer sketching a schedule needs the day, the windows with their
/// capacity bar, the runs that fill them and an overloaded window called out — not a stack of identical bars. The palette
/// and the MCP catalogue therefore ship the same vocabulary the component does: a UI role, a declarative stencil and a
/// schema whose props the pack draws.
/// </summary>
public sealed class TmScheduledRunsStencilTests
{
    private static readonly string[] RunsProps = ["title", "runs", "windows", "view", "overloaded"];

    [Fact]
    public void BuiltInSchema_ListsTheRuns_WithItsRoleAndProps()
    {
        var schema = new BuiltInComponentSchemas().GetSchemas().Single(s => s.Type == "TmScheduledRuns");

        schema.Category.Should().Be("AI");
        schema.DisplayName.Should().Be("Scheduled Runs");
        schema.Roles.Should().BeEquivalentTo("scheduled-runs");
        schema.Props.Select(prop => prop.Name).Should().Equal(RunsProps);
        schema.Props.Single(prop => prop.Name == "runs").Type.Should().Be(PropType.StringList);
        schema.Props.Single(prop => prop.Name == "windows").Type.Should().Be(PropType.StringList);
        schema.Props.Single(prop => prop.Name == "view").Type.Should().Be(PropType.Enum);
        schema.DefaultWidth.Should().BePositive();
        schema.DefaultHeight.Should().BePositive();
    }

    [Fact]
    public void ThePropNamesAvoidTheStencilExpressionContext()
    {
        var schema = new BuiltInComponentSchemas().GetSchemas().Single(s => s.Type == "TmScheduledRuns");

        schema.Props.Should().NotContain(prop => prop.Name == "size");
        schema.Props.Should().NotContain(prop => prop.Name == "repeat");
    }

    [Fact]
    public void TheComponentsTypeNameResolvesItsRole()
    {
        var vocabulary = new UiRoleVocabulary([new BuiltInUiRoleVocabularySource()]);

        vocabulary.Find("TmScheduledRuns")!.Slug.Should().Be("scheduled-runs");
        vocabulary.Find("scheduled-runs")!.DisplayName.Should().Be("Scheduled runs");
        vocabulary.Find("scheduled runs")!.Slug.Should().Be("scheduled-runs");
        vocabulary.Find("naplanovane behy")!.Slug.Should().Be("scheduled-runs");
        vocabulary.Find("TmScheduledRuns")!.Slug.Should().Be("scheduled-runs");
    }

    [Fact]
    public void TheRoleIsInTheBuiltInVocabulary()
    {
        var vocabulary = new UiRoleVocabulary([new BuiltInUiRoleVocabularySource()]);
        var schema = new BuiltInComponentSchemas().GetSchemas().Single(s => s.Type == "TmScheduledRuns");

        schema.Roles!.Where(role => vocabulary.Find(role) is null).Should().BeEmpty();
    }

    [Fact]
    public void StencilPack_RendersTheRuns_AndAgreesWithTheSchema()
    {
        var definition = new BuiltInStencilPackProvider().GetDefinitions()
            .Single(definition => definition.Type == "TmScheduledRuns");
        var schema = new BuiltInComponentSchemas().GetSchemas().Single(s => s.Type == "TmScheduledRuns");

        definition.IsBuiltIn.Should().BeTrue();
        definition.PackId.Should().Be("tempo");
        definition.NativeType.Should().BeNull("the board is a declarative stencil, not a native renderer");
        definition.Roles.Should().BeEquivalentTo(schema.Roles);
        definition.DefaultWidth.Should().Be(schema.DefaultWidth);
        definition.DefaultHeight.Should().Be(schema.DefaultHeight);
        definition.Props.Select(prop => prop.Name).Should().Equal(RunsProps);
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
    public void TheRunsPack_DrawsEveryPropItOffers()
    {
        var entry = Entry();

        entry.Should().Contain("{title", "the heading is the title prop");
        entry.Should().Contain("\"prop\": \"windows\"", "one column per window, over the windows prop");
        entry.Should().Contain("{window}", "and each window paints its own range");
        entry.Should().Contain("\"prop\": \"runs\"", "the runs of a window are repeated over the runs prop");
        entry.Should().Contain("{run}", "and each card paints its own run");
        entry.Should().Contain("{view", "the view chip follows the view prop");
    }

    [Fact]
    public void TheRunsPack_SketchesTheColumnsTheFullStateAndTheAssignAction()
    {
        var entry = Entry();

        entry.Should().Contain("Monday", "a day heading is part of the sketch");
        entry.Should().Contain("Units used", "the capacity line is drawn as a word");
        entry.Should().Contain("Planned", "a run state is drawn as a word");
        entry.Should().Contain("Overloaded by", "an overloaded window is named, not merely tinted");
        entry.Should().Contain("Free:", "the remaining capacity is part of the sketch");
        entry.Should().Contain("token(\\\"color.danger", "an overloaded window is sketched in the danger tone");
    }

    private static string Entry() => BuiltInStencilPackProvider.ReadPackJson()
        .Split('\n')
        .Single(line => line.Contains("\"type\": \"TmScheduledRuns\""));
}
