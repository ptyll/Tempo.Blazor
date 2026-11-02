using FluentAssertions;
using Tempo.Blazor.Components.Wireframe;
using Xunit;

namespace Tempo.Blazor.Tests.Wireframe;

/// <summary>
/// Plan 10b3966f phase 3: <c>TmReviewQueue</c> is a wireframe citizen, not editor chrome. It is
/// catalogued in the built-in schemas with the <c>review-queue</c> role, that role exists in the
/// built-in vocabulary (so the MCP catalogue and the editor palette agree), and the built-in stencil
/// pack renders the queue declaratively — no native renderer, because the placeholder only needs the
/// rows, the risk badges and the two decisions.
/// </summary>
public sealed class ReviewQueueStencilTests
{
    [Fact]
    public void BuiltInSchema_ListsTheQueue_WithItsRoleAndProps()
    {
        var schema = new BuiltInComponentSchemas().GetSchemas().Single(s => s.Type == "TmReviewQueue");

        schema.Category.Should().Be("Feedback");
        schema.DisplayName.Should().Be("Review Queue");
        schema.Roles.Should().BeEquivalentTo("review-queue");
        schema.Props.Select(prop => prop.Name)
            .Should().Equal("title", "mode", "itemCount", "showRisk", "showActions");
        schema.Props.Single(prop => prop.Name == "mode").Options.Should().Equal("list", "focus");
        schema.DefaultWidth.Should().BePositive();
        schema.DefaultHeight.Should().BePositive();
    }

    [Fact]
    public void EveryRoleOfTheQueue_IsInTheBuiltInVocabulary()
    {
        var vocabulary = new UiRoleVocabulary([new BuiltInUiRoleVocabularySource()]);
        var schema = new BuiltInComponentSchemas().GetSchemas().Single(s => s.Type == "TmReviewQueue");

        schema.Roles!.Where(role => vocabulary.Find(role) is null).Should().BeEmpty();
    }

    [Fact]
    public void TheQueueTypeNameResolvesItsRole_SoEveryComponentHasOne()
    {
        var vocabulary = new UiRoleVocabulary([new BuiltInUiRoleVocabularySource()]);

        vocabulary.Find("TmReviewQueue")!.Slug.Should().Be("review-queue");
    }

    [Fact]
    public void StencilPack_RendersTheQueue_AndAgreesWithTheSchema()
    {
        var def = new BuiltInStencilPackProvider().GetDefinitions().Single(d => d.Type == "TmReviewQueue");
        var schema = new BuiltInComponentSchemas().GetSchemas().Single(s => s.Type == "TmReviewQueue");

        def.IsBuiltIn.Should().BeTrue();
        def.PackId.Should().Be("tempo");
        def.NativeType.Should().BeNull("the queue is a declarative stencil, not a native renderer");
        def.Roles.Should().BeEquivalentTo(schema.Roles);
        def.DefaultWidth.Should().Be(schema.DefaultWidth);
        def.DefaultHeight.Should().Be(schema.DefaultHeight);
        def.Props.Select(prop => prop.Name).Should().Equal(schema.Props.Select(prop => prop.Name));
    }
}
