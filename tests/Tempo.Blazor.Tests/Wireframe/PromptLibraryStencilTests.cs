using FluentAssertions;
using Tempo.Blazor.Components.Wireframe;
using Tempo.Blazor.Components.Wireframe.Models;

namespace Tempo.Blazor.Tests.Wireframe;

/// <summary>
/// Plan 3e96f6d8 phase 6: <c>TmPromptLibrary</c> is a wireframe citizen — it needs a UI role and a declarative
/// stencil, or the palette and the MCP catalogue drift away from the component library. The schema is the contract
/// the palette, the properties panel and the stencil pack share, so the pack's props must match it exactly, in
/// order, and the sketch must draw every prop it offers.
/// </summary>
public sealed class PromptLibraryStencilTests
{
    private static readonly string[] LibraryProps =
        ["title", "templates", "categories", "showSearch", "showPreview", "showActions"];

    private static string Entry(string type) => BuiltInStencilPackProvider.ReadPackJson()
        .Split('\n')
        .Single(line => line.Contains($"\"type\": \"{type}\""));

    [Fact]
    public void BuiltInSchema_ListsThePromptLibrary_WithItsRoleAndProps()
    {
        var schema = new BuiltInComponentSchemas().GetSchemas().Single(s => s.Type == "TmPromptLibrary");

        schema.Category.Should().Be("AI");
        schema.DisplayName.Should().Be("Prompt Library");
        schema.Roles.Should().BeEquivalentTo("prompt-library");
        schema.Props.Select(prop => prop.Name).Should().Equal(LibraryProps);
        schema.DefaultWidth.Should().BePositive();
        schema.DefaultHeight.Should().BePositive();
    }

    [Fact]
    public void ThePropNamesAvoidTheStencilExpressionContext()
    {
        var schema = new BuiltInComponentSchemas().GetSchemas().Single(s => s.Type == "TmPromptLibrary");

        schema.Props.Should().NotContain(prop => prop.Name == "size",
            "the stencil expression context already owns `size` for the element box");
        schema.Props.Should().NotContain(prop => prop.Name == "repeat",
            "and `repeat` belongs to the repeat node");
    }

    [Fact]
    public void TheComponentsTypeNameResolvesItsRole()
    {
        var vocabulary = new UiRoleVocabulary([new BuiltInUiRoleVocabularySource()]);

        vocabulary.Find("TmPromptLibrary")!.Slug.Should().Be("prompt-library");
        vocabulary.Find("prompt-library")!.DisplayName.Should().Be("Prompt library");
        vocabulary.Find("prompt templates")!.Slug.Should().Be("prompt-library");
        vocabulary.Find("knihovna promptu")!.Slug.Should().Be("prompt-library");
    }

    [Fact]
    public void TheRoleIsInTheBuiltInVocabulary()
    {
        var vocabulary = new UiRoleVocabulary([new BuiltInUiRoleVocabularySource()]);
        var schema = new BuiltInComponentSchemas().GetSchemas().Single(s => s.Type == "TmPromptLibrary");

        schema.Roles!.Where(role => vocabulary.Find(role) is null).Should().BeEmpty();
    }

    [Fact]
    public void StencilPack_RendersThePromptLibrary_AndAgreesWithTheSchema()
    {
        var definition = new BuiltInStencilPackProvider().GetDefinitions()
            .Single(definition => definition.Type == "TmPromptLibrary");
        var schema = new BuiltInComponentSchemas().GetSchemas().Single(s => s.Type == "TmPromptLibrary");

        definition.IsBuiltIn.Should().BeTrue();
        definition.PackId.Should().Be("tempo");
        definition.NativeType.Should().BeNull("the prompt library is a declarative stencil, not a native renderer");
        definition.Roles.Should().BeEquivalentTo(schema.Roles);
        definition.DefaultWidth.Should().Be(schema.DefaultWidth);
        definition.DefaultHeight.Should().Be(schema.DefaultHeight);
        definition.Props.Select(prop => prop.Name).Should().Equal(LibraryProps);
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
    public void TheLibraryPack_DrawsEveryPropItOffers()
    {
        var entry = Entry("TmPromptLibrary");

        entry.Should().Contain("{title", "the card is named by its title");
        entry.Should().Contain("{row}", "one list row per entry of the templates prop");
        entry.Should().Contain("{chip}", "one category chip per entry of the categories prop");
        entry.Should().Contain("{showSearch", "the search field is drawn when showSearch is on");
        entry.Should().Contain("{showPreview", "the preview box is drawn when showPreview is on");
        entry.Should().Contain("{showActions", "the actions are drawn when showActions is on");
    }

    [Fact]
    public void TheLibraryPack_DrawsTheListAndTheDetailPane()
    {
        var entry = Entry("TmPromptLibrary");

        entry.Should().Contain("Brief a topic", "the sketch shows the picked template's title");
        entry.Should().Contain("Use prompt", "the Use action is part of the sketch");
        entry.Should().Contain("Copy", "and so is Copy");
        entry.Should().Contain("Search templates", "the search field keeps its visible placeholder");
        entry.Should().Contain("Write a brief about", "the preview shows prompt text, not a placeholder box only");
    }
}
