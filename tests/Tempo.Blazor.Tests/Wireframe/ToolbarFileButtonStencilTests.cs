using FluentAssertions;
using Tempo.Blazor.Components.Wireframe;
using Xunit;

namespace Tempo.Blazor.Tests.Wireframe;

/// <summary>
/// Decision TOOLBAR-FILE-BUTTON: <c>TmToolbarFileButton</c> is a placeable wireframe component with the role
/// <c>toolbar-button</c> - catalogued in the built-in schemas and drawn declaratively by the built-in stencil pack, so the editor
/// palette and the MCP catalogue (<c>wireframe_list_components</c>) agree.
/// </summary>
public sealed class ToolbarFileButtonStencilTests
{
    [Fact]
    public void BuiltInSchema_ListsTheFileButton_WithTheToolbarButtonRole()
    {
        var schema = new BuiltInComponentSchemas().GetSchemas().Single(s => s.Type == "TmToolbarFileButton");

        schema.Category.Should().Be("Toolbar");
        schema.Roles.Should().Equal("toolbar-button");
        schema.Props.Select(p => p.Name).Should().Equal("label", "icon", "accept", "multiple", "disabled");
        schema.DefaultWidth.Should().BePositive();
        schema.DefaultHeight.Should().BePositive();
    }

    [Fact]
    public void TheRole_IsInTheBuiltInVocabulary_AndResolvesTheComponentName()
    {
        var vocabulary = new UiRoleVocabulary([new BuiltInUiRoleVocabularySource()]);

        vocabulary.Find("toolbar-button")!.Slug.Should().Be("toolbar-button");
        vocabulary.Find("TmToolbarFileButton")!.Slug.Should().Be("toolbar-button");
    }

    [Fact]
    public void StencilPack_RendersTheFileButton_AndAgreesWithTheSchema()
    {
        var def = new BuiltInStencilPackProvider().GetDefinitions().Single(d => d.Type == "TmToolbarFileButton");
        var schema = new BuiltInComponentSchemas().GetSchemas().Single(s => s.Type == "TmToolbarFileButton");

        def.IsBuiltIn.Should().BeTrue();
        def.PackId.Should().Be("tempo");
        def.NativeType.Should().BeNull("the file button is a declarative stencil, not a native renderer");
        def.Roles.Should().BeEquivalentTo(schema.Roles);
        def.DefaultWidth.Should().Be(schema.DefaultWidth);
        def.DefaultHeight.Should().Be(schema.DefaultHeight);
        def.Props.Select(p => p.Name).Should().Equal(schema.Props.Select(p => p.Name));
    }
}
