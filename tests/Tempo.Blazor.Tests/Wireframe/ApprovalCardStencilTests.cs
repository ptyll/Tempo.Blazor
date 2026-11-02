using FluentAssertions;
using Tempo.Blazor.Components.Wireframe;
using Xunit;

namespace Tempo.Blazor.Tests.Wireframe;

/// <summary>
/// Plan 10b3966f phase 2: <c>TmApprovalCard</c> is a wireframe citizen, not editor chrome. It is
/// catalogued in the built-in schemas with the <c>approval-card</c> role, that role exists in the
/// built-in vocabulary, and the built-in stencil pack renders the card declaratively (no native
/// renderer) so the MCP catalogue and the editor palette agree.
/// </summary>
public sealed class ApprovalCardStencilTests
{
    [Fact]
    public void BuiltInSchema_ListsTheCard_WithItsRoleAndProps()
    {
        var schema = new BuiltInComponentSchemas().GetSchemas().Single(s => s.Type == "TmApprovalCard");

        schema.Category.Should().Be("Feedback");
        schema.DisplayName.Should().Be("Approval Card");
        schema.Roles.Should().BeEquivalentTo("approval-card");
        schema.Props.Select(prop => prop.Name).Should().Equal("title", "description", "risk", "showCountdown");
        schema.DefaultWidth.Should().BePositive();
        schema.DefaultHeight.Should().BePositive();
    }

    [Fact]
    public void EveryRoleOfTheCard_IsInTheBuiltInVocabulary()
    {
        var vocabulary = new UiRoleVocabulary([new BuiltInUiRoleVocabularySource()]);
        var schema = new BuiltInComponentSchemas().GetSchemas().Single(s => s.Type == "TmApprovalCard");

        schema.Roles!.Where(role => vocabulary.Find(role) is null).Should().BeEmpty();
    }

    [Fact]
    public void TheCardsTypeNameResolvesItsRole_SoEveryComponentHasOne()
    {
        var vocabulary = new UiRoleVocabulary([new BuiltInUiRoleVocabularySource()]);

        vocabulary.Find("TmApprovalCard")!.Slug.Should().Be("approval-card");
    }

    [Fact]
    public void StencilPack_RendersTheCard_AndAgreesWithTheSchema()
    {
        var def = new BuiltInStencilPackProvider().GetDefinitions().Single(d => d.Type == "TmApprovalCard");
        var schema = new BuiltInComponentSchemas().GetSchemas().Single(s => s.Type == "TmApprovalCard");

        def.IsBuiltIn.Should().BeTrue();
        def.PackId.Should().Be("tempo");
        def.NativeType.Should().BeNull("the card is a declarative stencil, not a native renderer");
        def.Roles.Should().BeEquivalentTo(schema.Roles);
        def.DefaultWidth.Should().Be(schema.DefaultWidth);
        def.DefaultHeight.Should().Be(schema.DefaultHeight);
        def.Props.Select(prop => prop.Name).Should().Equal(schema.Props.Select(prop => prop.Name));
    }
}
