using FluentAssertions;
using Tempo.Blazor.Components.Wireframe;
using Tempo.Blazor.Components.Wireframe.Models;
using Xunit;

namespace Tempo.Blazor.Tests.Wireframe;

/// <summary>
/// Plan 3e96f6d8 phase 4: <c>TmModelPicker</c> is a wireframe citizen — it needs a UI role and a declarative
/// stencil, or the palette and the MCP catalogue drift away from the component library. The schema is the
/// contract the palette, the properties panel and the stencil pack share, so the pack's props must match it
/// exactly, in order, and the sketch must draw every prop it offers.
/// </summary>
public sealed class ModelPickerStencilTests
{
    private static readonly string[] PickerProps =
        ["title", "models", "groupBy", "showSearch", "showMetrics", "showTags"];

    [Fact]
    public void BuiltInSchema_ListsThePicker_WithItsRoleAndProps()
    {
        var schema = new BuiltInComponentSchemas().GetSchemas().Single(s => s.Type == "TmModelPicker");

        schema.Category.Should().Be("AI");
        schema.DisplayName.Should().Be("Model Picker");
        schema.Roles.Should().BeEquivalentTo("model-picker");
        schema.Props.Select(prop => prop.Name).Should().Equal(PickerProps);
        schema.Props.Single(prop => prop.Name == "groupBy").Options.Should()
            .Equal("provider", "family", "none");
        schema.DefaultWidth.Should().BePositive();
        schema.DefaultHeight.Should().BePositive();
    }

    [Fact]
    public void ThePropNamesAvoidTheStencilExpressionContext()
    {
        var schema = new BuiltInComponentSchemas().GetSchemas().Single(s => s.Type == "TmModelPicker");

        schema.Props.Should().NotContain(prop => prop.Name == "size",
            "the stencil expression context already owns `size` for the element box");
        schema.Props.Should().NotContain(prop => prop.Name == "repeat",
            "and `repeat` belongs to the repeat node");
    }

    [Fact]
    public void TheComponentsTypeNameResolvesItsRole()
    {
        var vocabulary = new UiRoleVocabulary([new BuiltInUiRoleVocabularySource()]);

        vocabulary.Find("TmModelPicker")!.Slug.Should().Be("model-picker");
        vocabulary.Find("model-picker")!.DisplayName.Should().Be("Model picker");
        vocabulary.Find("model selector")!.Slug.Should().Be("model-picker");
        vocabulary.Find("vyber modelu")!.Slug.Should().Be("model-picker");
    }

    [Fact]
    public void TheRoleIsInTheBuiltInVocabulary()
    {
        var vocabulary = new UiRoleVocabulary([new BuiltInUiRoleVocabularySource()]);
        var schema = new BuiltInComponentSchemas().GetSchemas().Single(s => s.Type == "TmModelPicker");

        schema.Roles!.Where(role => vocabulary.Find(role) is null).Should().BeEmpty();
    }

    [Fact]
    public void StencilPack_RendersThePicker_AndAgreesWithTheSchema()
    {
        var definition = new BuiltInStencilPackProvider().GetDefinitions()
            .Single(definition => definition.Type == "TmModelPicker");
        var schema = new BuiltInComponentSchemas().GetSchemas().Single(s => s.Type == "TmModelPicker");

        definition.IsBuiltIn.Should().BeTrue();
        definition.PackId.Should().Be("tempo");
        definition.NativeType.Should().BeNull("the picker is a declarative stencil, not a native renderer");
        definition.Roles.Should().BeEquivalentTo(schema.Roles);
        definition.DefaultWidth.Should().Be(schema.DefaultWidth);
        definition.DefaultHeight.Should().Be(schema.DefaultHeight);
        definition.Props.Select(prop => prop.Name).Should().Equal(PickerProps);
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
    public void ThePickerPack_DrawsEveryPropItOffers()
    {
        var entry = Entry("TmModelPicker");

        entry.Should().Contain("{title", "the popup is named by its title");
        entry.Should().Contain("{row}", "one row per entry of the models prop");
        entry.Should().Contain("groupBy", "the grouping changes the header line");
        entry.Should().Contain("showSearch", "the search field is optional");
        entry.Should().Contain("showMetrics", "the comparison bars are optional");
        entry.Should().Contain("showTags", "the state tags are optional");
    }

    [Fact]
    public void ThePickerPack_DrawsTheComparisonAsBarsAndWords()
    {
        var entry = Entry("TmModelPicker");

        entry.Should().Contain("token(\\\"color.primary", "the speed bar is drawn");
        entry.Should().Contain("token(\\\"color.success", "the quality bar is drawn");
        entry.Should().Contain("token(\\\"color.warning", "the price bar is drawn");
        entry.Should().Contain("128k", "the context window is a WORD next to the bars");
        entry.Should().Contain("vision", "a capability is a labelled chip, not a dot");
        entry.Should().Contain("Thinking", "the trigger's state tag is a word");
    }

    private static string Entry(string type) => BuiltInStencilPackProvider.ReadPackJson()
        .Split('\n')
        .Single(line => line.Contains($"\"type\": \"{type}\""));
}
