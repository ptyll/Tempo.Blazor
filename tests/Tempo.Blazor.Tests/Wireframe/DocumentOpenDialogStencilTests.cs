using FluentAssertions;
using Tempo.Blazor.Components.Wireframe;
using Tempo.Blazor.Components.Wireframe.Models;
using Xunit;

namespace Tempo.Blazor.Tests.Wireframe;

/// <summary>
/// Phase 3 of plan 643bba06: the shared <c>TmDocumentOpenDialog</c> is a placeable wireframe component (decision: stencil + role,
/// not editor chrome). It carries the roles <c>dialog</c> and <c>list-view</c>, is catalogued in the built-in schemas, and the
/// built-in stencil pack renders it declaratively (no native renderer) - so the MCP catalogue and the editor palette agree.
/// </summary>
public sealed class DocumentOpenDialogStencilTests
{
    [Fact]
    public void BuiltInSchema_ListsTheDialog_WithRolesAndTheShowRecentProp()
    {
        var schema = new BuiltInComponentSchemas().GetSchemas().Single(s => s.Type == "TmDocumentOpenDialog");

        schema.Category.Should().Be("Files");
        schema.DisplayName.Should().Be("Document Open Dialog");
        schema.Roles.Should().BeEquivalentTo("dialog", "list-view");
        schema.Props.Select(p => p.Name).Should().Equal("showRecent");
        schema.DefaultWidth.Should().BePositive();
        schema.DefaultHeight.Should().BePositive();
    }

    [Fact]
    public void EveryRoleOfTheDialog_IsInTheBuiltInVocabulary()
    {
        var vocabulary = new UiRoleVocabulary([new BuiltInUiRoleVocabularySource()]);
        var schema = new BuiltInComponentSchemas().GetSchemas().Single(s => s.Type == "TmDocumentOpenDialog");

        schema.Roles!.Where(role => vocabulary.Find(role) is null).Should().BeEmpty();
    }

    [Fact]
    public void StencilPack_RendersTheDialog_AndAgreesWithTheSchema()
    {
        var def = new BuiltInStencilPackProvider().GetDefinitions().Single(d => d.Type == "TmDocumentOpenDialog");
        var schema = new BuiltInComponentSchemas().GetSchemas().Single(s => s.Type == "TmDocumentOpenDialog");

        def.IsBuiltIn.Should().BeTrue();
        def.PackId.Should().Be("tempo");
        def.NativeType.Should().BeNull("the dialog is a declarative stencil, not a native renderer");
        def.Roles.Should().BeEquivalentTo(schema.Roles);
        def.DefaultWidth.Should().Be(schema.DefaultWidth);
        def.DefaultHeight.Should().Be(schema.DefaultHeight);
        def.Props.Select(p => p.Name).Should().Equal(schema.Props.Select(p => p.Name));
    }
}
