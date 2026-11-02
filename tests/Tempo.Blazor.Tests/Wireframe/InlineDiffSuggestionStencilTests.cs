using FluentAssertions;
using Tempo.Blazor.Components.Wireframe;
using Xunit;

namespace Tempo.Blazor.Tests.Wireframe;

/// <summary>
/// Plan 10b3966f phase 4: <c>TmInlineDiff</c> and <c>TmSuggestionCard</c> are wireframe citizens, not
/// editor chrome. Both are catalogued in the built-in schemas with their own role, those roles exist in
/// the built-in vocabulary (so the MCP catalogue and the editor palette agree), and the built-in
/// stencil pack renders them declaratively — no native renderer, because the placeholder only needs
/// the two sides of a change (or the confidence, rationale and actions of a suggestion).
/// </summary>
public sealed class InlineDiffSuggestionStencilTests
{
    [Theory]
    [InlineData("TmInlineDiff", "inline-diff", "Feedback", "Inline Diff")]
    [InlineData("TmSuggestionCard", "suggestion-card", "Feedback", "Suggestion Card")]
    public void BuiltInSchema_ListsTheComponent_WithItsRoleAndProps(
        string type, string role, string category, string displayName)
    {
        var schema = new BuiltInComponentSchemas().GetSchemas().Single(candidate => candidate.Type == type);

        schema.Category.Should().Be(category);
        schema.DisplayName.Should().Be(displayName);
        schema.Roles.Should().BeEquivalentTo(role);
        schema.Props.Should().NotBeEmpty();
        schema.DefaultWidth.Should().BePositive();
        schema.DefaultHeight.Should().BePositive();
    }

    [Fact]
    public void TheInlineDiffSchema_ExposesTheModeAndTheGranularity()
    {
        var schema = new BuiltInComponentSchemas().GetSchemas().Single(candidate => candidate.Type == "TmInlineDiff");

        schema.Props.Select(prop => prop.Name).Should().Equal("mode", "granularity", "showCount");
        schema.Props.Single(prop => prop.Name == "mode").Options.Should().Equal("display", "review");
        schema.Props.Single(prop => prop.Name == "granularity").Options.Should().Equal("words", "lines");
    }

    [Fact]
    public void TheSuggestionSchema_ExposesTheConfidenceBandsAndTheAlternatives()
    {
        var schema = new BuiltInComponentSchemas().GetSchemas().Single(candidate => candidate.Type == "TmSuggestionCard");

        schema.Props.Select(prop => prop.Name)
            .Should().Equal("title", "text", "confidence", "showRationale", "alternatives");
        schema.Props.Single(prop => prop.Name == "confidence").Options
            .Should().Equal("none", "low", "medium", "high");
    }

    [Theory]
    [InlineData("TmInlineDiff")]
    [InlineData("TmSuggestionCard")]
    public void EveryRole_IsInTheBuiltInVocabulary_AndTheTypeNameResolvesIt(string type)
    {
        var vocabulary = new UiRoleVocabulary([new BuiltInUiRoleVocabularySource()]);
        var schema = new BuiltInComponentSchemas().GetSchemas().Single(candidate => candidate.Type == type);

        schema.Roles!.Where(role => vocabulary.Find(role) is null).Should().BeEmpty();
        vocabulary.Find(type)!.Slug.Should().Be(schema.Roles![0]);
    }

    [Theory]
    [InlineData("TmInlineDiff")]
    [InlineData("TmSuggestionCard")]
    public void StencilPack_RendersTheComponent_AndAgreesWithTheSchema(string type)
    {
        var def = new BuiltInStencilPackProvider().GetDefinitions().Single(candidate => candidate.Type == type);
        var schema = new BuiltInComponentSchemas().GetSchemas().Single(candidate => candidate.Type == type);

        def.IsBuiltIn.Should().BeTrue();
        def.PackId.Should().Be("tempo");
        def.NativeType.Should().BeNull("both components are declarative stencils, not native renderers");
        def.Roles.Should().BeEquivalentTo(schema.Roles);
        def.DefaultWidth.Should().Be(schema.DefaultWidth);
        def.DefaultHeight.Should().Be(schema.DefaultHeight);
        def.Props.Select(prop => prop.Name).Should().Equal(schema.Props.Select(prop => prop.Name));
    }
}
