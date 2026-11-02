using FluentAssertions;
using Tempo.Blazor.Components.Wireframe;
using Xunit;

namespace Tempo.Blazor.Tests.Wireframe;

/// <summary>
/// Plan 10b3966f phase 5: <c>TmPermissionRequest</c> (Tempo.Blazor.AI) is a wireframe citizen, not
/// editor chrome. It is catalogued in the built-in schemas with the <c>permission-request</c> role,
/// that role exists in the built-in vocabulary (so the MCP catalogue and the editor palette agree),
/// and the built-in stencil pack renders the request declaratively — no native renderer, because the
/// placeholder only needs the tool, its target, the risk pill and the two decisions.
/// </summary>
public sealed class PermissionRequestStencilTests
{
    [Fact]
    public void BuiltInSchema_ListsThePermissionRequest_WithItsRoleAndProps()
    {
        var schema = new BuiltInComponentSchemas().GetSchemas()
            .Single(candidate => candidate.Type == "TmPermissionRequest");

        schema.Category.Should().Be("Feedback");
        schema.DisplayName.Should().Be("Permission Request");
        schema.Roles.Should().BeEquivalentTo("permission-request");
        schema.Props.Select(prop => prop.Name)
            .Should().Equal("tool", "target", "risk", "scope", "showReason");
        schema.Props.Single(prop => prop.Name == "risk").Options
            .Should().Equal("low", "medium", "high", "critical");
        schema.Props.Single(prop => prop.Name == "scope").Options
            .Should().Equal("once", "for-duration", "session", "always");
        schema.DefaultWidth.Should().BePositive();
        schema.DefaultHeight.Should().BePositive();
    }

    [Fact]
    public void EveryRoleOfThePermissionRequest_IsInTheBuiltInVocabulary()
    {
        var vocabulary = new UiRoleVocabulary([new BuiltInUiRoleVocabularySource()]);
        var schema = new BuiltInComponentSchemas().GetSchemas()
            .Single(candidate => candidate.Type == "TmPermissionRequest");

        schema.Roles!.Where(role => vocabulary.Find(role) is null).Should().BeEmpty();
    }

    [Fact]
    public void TheTypeNameResolvesItsRole_SoEveryComponentHasOne()
    {
        var vocabulary = new UiRoleVocabulary([new BuiltInUiRoleVocabularySource()]);

        vocabulary.Find("TmPermissionRequest")!.Slug.Should().Be("permission-request");
    }

    [Fact]
    public void StencilPack_RendersThePermissionRequest_AndAgreesWithTheSchema()
    {
        var definition = new BuiltInStencilPackProvider().GetDefinitions()
            .Single(candidate => candidate.Type == "TmPermissionRequest");
        var schema = new BuiltInComponentSchemas().GetSchemas()
            .Single(candidate => candidate.Type == "TmPermissionRequest");

        definition.IsBuiltIn.Should().BeTrue();
        definition.PackId.Should().Be("tempo");
        definition.NativeType.Should().BeNull("the request is a declarative stencil, not a native renderer");
        definition.Roles.Should().BeEquivalentTo(schema.Roles);
        definition.DefaultWidth.Should().Be(schema.DefaultWidth);
        definition.DefaultHeight.Should().Be(schema.DefaultHeight);
        definition.Props.Select(prop => prop.Name).Should().Equal(schema.Props.Select(prop => prop.Name));
    }
}
