using FluentAssertions;
using Tempo.Blazor.Components.Wireframe;
using Xunit;

namespace Tempo.Blazor.Tests.Wireframe;

/// <summary>
/// Phase 8 of plan 643bba06 (decision EMAIL-WIREFRAME-ROLES): the public e-mail components are wireframe citizens. The editor and the
/// canvas block (the one public renderer a host places on its own), the item list and the item dialog are placeable stencils with
/// schema, role and declarative pack entry that agree with each other; every other public component of the package is part of the
/// editor chrome and resolves a role through the vocabulary (its type name is a synonym), so "every component has a role" holds.
/// </summary>
public sealed class EmailComponentsStencilTests
{
    private static readonly string[] Placeable =
    [
        "TmEmailTemplateEditor", "TmEmailCanvasBlock", "TmEmailItemList", "TmEmailItemDialog",
    ];

    /// <summary>Every public (non-abstract) razor component of the e-mail package, by the type names the stencil work covers.</summary>
    private static readonly string[] PublicComponents =
    [
        "TmEmailTemplateEditor", "TmEmailTemplateCanvas", "TmEmailCanvasBlock", "TmEmailTemplateToolbox", "TmEmailPropertyPanel",
        "TmEmailTemplatePreview", "TmEmailImportDialog", "TmEmailExportDialog", "TmEmailValidationPanel", "TmEmailVariablePicker",
        "TmEmailItemList", "TmEmailItemDialog", "TmEmailTableCellDialog", "TmEmailTableEditor", "TmEmailObjectFields",
        "TmEmailClassesEditor", "TmEmailHtmlAttributesEditor", "TmEmailKeyValueEditor", "TmEmailListEditor",
    ];

    public static TheoryData<string> PlaceableTypes()
    {
        var data = new TheoryData<string>();
        foreach (var type in Placeable) data.Add(type);
        return data;
    }

    public static TheoryData<string> AllPublicTypes()
    {
        var data = new TheoryData<string>();
        foreach (var type in PublicComponents) data.Add(type);
        return data;
    }

    [Theory]
    [MemberData(nameof(PlaceableTypes))]
    public void Placeable_HasASchemaWithRoles_AndAPackStencilThatAgrees(string type)
    {
        var schema = new BuiltInComponentSchemas().GetSchemas().Single(s => s.Type == type);
        var def = new BuiltInStencilPackProvider().GetDefinitions().Single(d => d.Type == type);

        schema.Roles.Should().NotBeNullOrEmpty();
        schema.DefaultWidth.Should().BePositive();
        schema.DefaultHeight.Should().BePositive();
        def.IsBuiltIn.Should().BeTrue();
        def.PackId.Should().Be("tempo");
        def.NativeType.Should().BeNull("the e-mail stencils are declarative, not native renderers");
        def.Roles.Should().BeEquivalentTo(schema.Roles);
        def.DefaultWidth.Should().Be(schema.DefaultWidth);
        def.DefaultHeight.Should().Be(schema.DefaultHeight);
        def.Props.Select(p => p.Name).Should().Equal(schema.Props.Select(p => p.Name));
    }

    [Theory]
    [MemberData(nameof(PlaceableTypes))]
    public void EveryRoleOfAPlaceable_IsInTheBuiltInVocabulary(string type)
    {
        var vocabulary = new UiRoleVocabulary([new BuiltInUiRoleVocabularySource()]);
        var schema = new BuiltInComponentSchemas().GetSchemas().Single(s => s.Type == type);

        schema.Roles!.Where(role => vocabulary.Find(role) is null).Should().BeEmpty();
    }

    [Theory]
    [MemberData(nameof(AllPublicTypes))]
    public void EveryPublicEmailComponent_ResolvesARoleThroughTheVocabulary(string type)
    {
        var vocabulary = new UiRoleVocabulary([new BuiltInUiRoleVocabularySource()]);

        vocabulary.Find(type).Should().NotBeNull(
            $"{type} is either a placeable stencil or editor chrome, and either way it names a role for the wireframe editor");
    }

    [Fact]
    public void TheEditorAndTheBlockCarryTheirOwnRoles()
    {
        var vocabulary = new UiRoleVocabulary([new BuiltInUiRoleVocabularySource()]);

        vocabulary.Find("TmEmailTemplateEditor")!.Slug.Should().Be("email-template-editor");
        vocabulary.Find("TmEmailTemplateToolbox")!.Slug.Should().Be("email-template-editor", "the toolbox is part of the editor");
        vocabulary.Find("TmEmailCanvasBlock")!.Slug.Should().Be("email-block");
    }

    [Fact]
    public void TheEditorStencil_DrawsPalette_Canvas_AndInspector_AndHonoursReadOnly()
    {
        var def = new BuiltInStencilPackProvider().GetDefinitions().Single(d => d.Type == "TmEmailTemplateEditor");

        def.Category.Should().Be("Editors & Apps");
        def.Props.Select(p => p.Name).Should().Contain(["title", "readOnly"]);
    }
}
