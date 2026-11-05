using FluentAssertions;
using Tempo.Blazor.Components.Wireframe;
using Tempo.Blazor.Components.Wireframe.Models;
using Xunit;

namespace Tempo.Blazor.Tests.Wireframe;

/// <summary>
/// <c>TmParallelAttempts</c> is a wireframe citizen: a designer sketching a comparison of attempts needs the columns,
/// the best mark, the confirmation and the comparison table — not three identical boxes. The palette and the MCP
/// catalogue therefore ship the same vocabulary the component does.
/// </summary>
public sealed class TmParallelAttemptsStencilTests
{
    private static readonly string[] AttemptProps =
        ["title", "attempts", "bestAttemptId", "confirmSelection", "showComparison"];

    [Fact]
    public void BuiltInSchema_ListsTheAttempts_WithTheirRoleAndProps()
    {
        var schema = new BuiltInComponentSchemas().GetSchemas().Single(s => s.Type == "TmParallelAttempts");

        schema.Category.Should().Be("AI");
        schema.DisplayName.Should().Be("Parallel Attempts");
        schema.Roles.Should().BeEquivalentTo("parallel-attempts");
        schema.Props.Select(prop => prop.Name).Should().Equal(AttemptProps);
        schema.DefaultWidth.Should().BePositive();
        schema.DefaultHeight.Should().BePositive();
    }

    [Fact]
    public void TheComponentsTypeNameResolvesItsRole()
    {
        var vocabulary = new UiRoleVocabulary([new BuiltInUiRoleVocabularySource()]);

        vocabulary.Find("TmParallelAttempts")!.Slug.Should().Be("parallel-attempts");
        vocabulary.Find("parallel-attempts")!.DisplayName.Should().Be("Parallel attempts");
        vocabulary.Find("attempt comparison")!.Slug.Should().Be("parallel-attempts");
        vocabulary.Find("paralelni pokusy")!.Slug.Should().Be("parallel-attempts");
    }

    [Fact]
    public void TheRoleIsInTheBuiltInVocabulary()
    {
        var vocabulary = new UiRoleVocabulary([new BuiltInUiRoleVocabularySource()]);
        var schema = new BuiltInComponentSchemas().GetSchemas().Single(s => s.Type == "TmParallelAttempts");

        schema.Roles!.Where(role => vocabulary.Find(role) is null).Should().BeEmpty();
    }

    [Fact]
    public void StencilPack_RendersTheAttempts_AndAgreesWithTheSchema()
    {
        var definition = new BuiltInStencilPackProvider().GetDefinitions()
            .Single(definition => definition.Type == "TmParallelAttempts");
        var schema = new BuiltInComponentSchemas().GetSchemas().Single(s => s.Type == "TmParallelAttempts");

        definition.IsBuiltIn.Should().BeTrue();
        definition.PackId.Should().Be("tempo");
        definition.NativeType.Should().BeNull("the comparison is a declarative stencil, not a native renderer");
        definition.Roles.Should().BeEquivalentTo(schema.Roles);
        definition.DefaultWidth.Should().Be(schema.DefaultWidth);
        definition.DefaultHeight.Should().Be(schema.DefaultHeight);
        definition.Props.Select(prop => prop.Name).Should().Equal(AttemptProps);
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
    public void TheAttemptsPack_DrawsEveryPropItOffers()
    {
        var entry = Entry();

        entry.Should().Contain("{title", "the heading is the title prop");
        entry.Should().Contain("\"prop\": \"attempts\"", "the columns come from the attempts prop");
        entry.Should().Contain("{attempt}", "and each column paints its own text");
        entry.Should().Contain("bestAttemptId", "the best attempt prop reaches the sketch");
        entry.Should().Contain("{confirmSelection", "the confirmation bar is conditional on its prop");
        entry.Should().Contain("{showComparison", "and so is the comparison table");
    }

    [Fact]
    public void TheAttemptsPack_SketchesTheBestMarkTheVotesAndTheTable()
    {
        var entry = Entry();

        entry.Should().Contain("Best", "the best attempt is marked with a word, not only a fill");
        entry.Should().Contain("token(\\\"color.success", "in the success tone the component paints");
        entry.Should().Contain("Votes (2)", "the votes the column collected are part of the sketch");
        entry.Should().Contain("Approved", "and so is a vote's outcome");
        entry.Should().Contain("Attempts compared", "the comparison table is drawn");
        entry.Should().Contain("Use this attempt", "the confirmation action is part of the sketch");
    }

    private static string Entry() => BuiltInStencilPackProvider.ReadPackJson()
        .Split('\n')
        .Single(line => line.Contains("\"type\": \"TmParallelAttempts\""));
}
