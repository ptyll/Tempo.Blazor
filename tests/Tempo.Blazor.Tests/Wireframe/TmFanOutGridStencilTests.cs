using FluentAssertions;
using Tempo.Blazor.Components.Wireframe;
using Tempo.Blazor.Components.Wireframe.Models;
using Xunit;

namespace Tempo.Blazor.Tests.Wireframe;

/// <summary>
/// <c>TmFanOutGrid</c> is a wireframe citizen: a designer sketching an orchestrated run needs a grid of work tiles with
/// their states, their attempt counts and a summary above them — not four generic rectangles. The palette and the MCP
/// catalogue therefore ship the same vocabulary the component does: a UI role, a declarative stencil and a schema whose
/// props the pack draws.
/// </summary>
public sealed class TmFanOutGridStencilTests
{
    private static readonly string[] FanOutProps = ["title", "tasks", "columns", "filter", "compact"];

    [Fact]
    public void BuiltInSchema_ListsTheGrid_WithItsRoleAndProps()
    {
        var schema = new BuiltInComponentSchemas().GetSchemas().Single(s => s.Type == "TmFanOutGrid");

        schema.Category.Should().Be("AI");
        schema.DisplayName.Should().Be("Fan-out Grid");
        schema.Roles.Should().BeEquivalentTo("fan-out-grid");
        schema.Props.Select(prop => prop.Name).Should().Equal(FanOutProps);
        schema.Props.Single(prop => prop.Name == "filter").Options.Should()
            .Equal("all", "queued", "running", "approval", "succeeded", "failed", "cancelled");
        schema.DefaultWidth.Should().BePositive();
        schema.DefaultHeight.Should().BePositive();
    }

    [Fact]
    public void ThePropNamesAvoidTheStencilExpressionContext()
    {
        var schema = new BuiltInComponentSchemas().GetSchemas().Single(s => s.Type == "TmFanOutGrid");

        schema.Props.Should().NotContain(prop => prop.Name == "size",
            "the stencil expression context already owns `size` for the element box");
        schema.Props.Should().NotContain(prop => prop.Name == "repeat",
            "and `repeat` belongs to the repeat node");
    }

    [Fact]
    public void TheComponentsTypeNameResolvesItsRole()
    {
        var vocabulary = new UiRoleVocabulary([new BuiltInUiRoleVocabularySource()]);

        vocabulary.Find("TmFanOutGrid")!.Slug.Should().Be("fan-out-grid");
        vocabulary.Find("fan-out-grid")!.DisplayName.Should().Be("Fan-out grid");
        vocabulary.Find("worker grid")!.Slug.Should().Be("fan-out-grid");
        vocabulary.Find("rozvetveni prace")!.Slug.Should().Be("fan-out-grid");
        vocabulary.Find("mrizka uloh")!.Slug.Should().Be("fan-out-grid");
    }

    [Fact]
    public void TheRoleIsInTheBuiltInVocabulary()
    {
        var vocabulary = new UiRoleVocabulary([new BuiltInUiRoleVocabularySource()]);
        var schema = new BuiltInComponentSchemas().GetSchemas().Single(s => s.Type == "TmFanOutGrid");

        schema.Roles!.Where(role => vocabulary.Find(role) is null).Should().BeEmpty();
    }

    [Fact]
    public void StencilPack_RendersTheGrid_AndAgreesWithTheSchema()
    {
        var definition = new BuiltInStencilPackProvider().GetDefinitions()
            .Single(definition => definition.Type == "TmFanOutGrid");
        var schema = new BuiltInComponentSchemas().GetSchemas().Single(s => s.Type == "TmFanOutGrid");

        definition.IsBuiltIn.Should().BeTrue();
        definition.PackId.Should().Be("tempo");
        definition.NativeType.Should().BeNull("the grid is a declarative stencil, not a native renderer");
        definition.Roles.Should().BeEquivalentTo(schema.Roles);
        definition.DefaultWidth.Should().Be(schema.DefaultWidth);
        definition.DefaultHeight.Should().Be(schema.DefaultHeight);
        definition.Props.Select(prop => prop.Name).Should().Equal(FanOutProps);
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
    public void TheGridPack_DrawsEveryPropItOffers()
    {
        var entry = Entry();

        entry.Should().Contain("{title", "the heading is the title prop");
        entry.Should().Contain("\"prop\": \"tasks\"", "the work tiles are the tasks prop");
        entry.Should().Contain("{task}", "and each tile paints its own text");
        entry.Should().Contain("columns", "the grid follows the columns prop");
        entry.Should().Contain("{filter", "the filter chips follow the filter prop");
        entry.Should().Contain("{compact", "the compact hint is conditional on the compact prop");
    }

    [Fact]
    public void TheGridPack_SketchesTheSummaryTheStatesAndTheActions()
    {
        var entry = Entry();

        entry.Should().Contain("done", "the summary line states how much finished");
        entry.Should().Contain("running");
        entry.Should().Contain("failed");
        entry.Should().Contain("ends in", "the estimate is part of the sketch");
        entry.Should().Contain("2/3", "the retry badge is drawn, not implied");
        entry.Should().Contain("Retry", "the detail card's actions are part of the sketch");
        entry.Should().Contain("Cancel");
        entry.Should().Contain("token(\\\"color.danger", "a failed tile is sketched in the danger tone");
    }

    private static string Entry() => BuiltInStencilPackProvider.ReadPackJson()
        .Split('\n')
        .Single(line => line.Contains("\"type\": \"TmFanOutGrid\""));
}
