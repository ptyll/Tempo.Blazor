using FluentAssertions;
using Tempo.Blazor.Components.Wireframe;
using Tempo.Blazor.Components.Wireframe.Models;
using Xunit;

namespace Tempo.Blazor.Tests.Wireframe;

/// <summary>
/// <c>TmMemoryPanel</c> is a wireframe citizen: a designer sketching what an agent remembers needs the categories with
/// their counts, the status words and the switch that turns memory off — not a stack of identical bars. The palette and
/// the MCP catalogue therefore ship the same vocabulary the component does: a UI role, a declarative stencil and a
/// schema whose props the pack draws.
/// </summary>
public sealed class TmMemoryPanelStencilTests
{
    private static readonly string[] MemoryProps = ["title", "memories", "enabled", "filter"];

    [Fact]
    public void BuiltInSchema_ListsThePanel_WithItsRoleAndProps()
    {
        var schema = new BuiltInComponentSchemas().GetSchemas().Single(s => s.Type == "TmMemoryPanel");

        schema.Category.Should().Be("AI");
        schema.DisplayName.Should().Be("Memory Panel");
        schema.Roles.Should().BeEquivalentTo("memory-panel");
        schema.Props.Select(prop => prop.Name).Should().Equal(MemoryProps);
        schema.Props.Single(prop => prop.Name == "memories").Type.Should().Be(PropType.StringList);
        schema.Props.Single(prop => prop.Name == "enabled").Type.Should().Be(PropType.Bool);
        schema.Props.Single(prop => prop.Name == "filter").Options
            .Should().Equal("all", "active", "paused", "pending-review", "forgotten");
        schema.DefaultWidth.Should().BePositive();
        schema.DefaultHeight.Should().BePositive();
    }

    [Fact]
    public void ThePropNamesAvoidTheStencilExpressionContext()
    {
        var schema = new BuiltInComponentSchemas().GetSchemas().Single(s => s.Type == "TmMemoryPanel");

        schema.Props.Should().NotContain(prop => prop.Name == "size");
        schema.Props.Should().NotContain(prop => prop.Name == "repeat");
    }

    [Fact]
    public void TheComponentsTypeNameResolvesItsRole()
    {
        var vocabulary = new UiRoleVocabulary([new BuiltInUiRoleVocabularySource()]);

        vocabulary.Find("TmMemoryPanel")!.Slug.Should().Be("memory-panel");
        vocabulary.Find("memory-panel")!.DisplayName.Should().Be("Memory panel");
        vocabulary.Find("memory")!.Slug.Should().Be("memory-panel");
        vocabulary.Find("what the agent remembers")!.Slug.Should().Be("memory-panel");
        vocabulary.Find("pamet agenta")!.Slug.Should().Be("memory-panel");
        vocabulary.Find("pamet")!.Slug.Should().Be("memory-panel");
    }

    [Fact]
    public void TheRoleIsInTheBuiltInVocabulary()
    {
        var vocabulary = new UiRoleVocabulary([new BuiltInUiRoleVocabularySource()]);
        var schema = new BuiltInComponentSchemas().GetSchemas().Single(s => s.Type == "TmMemoryPanel");

        schema.Roles!.Where(role => vocabulary.Find(role) is null).Should().BeEmpty();
    }

    [Fact]
    public void StencilPack_RendersThePanel_AndAgreesWithTheSchema()
    {
        var definition = new BuiltInStencilPackProvider().GetDefinitions()
            .Single(definition => definition.Type == "TmMemoryPanel");
        var schema = new BuiltInComponentSchemas().GetSchemas().Single(s => s.Type == "TmMemoryPanel");

        definition.IsBuiltIn.Should().BeTrue();
        definition.PackId.Should().Be("tempo");
        definition.NativeType.Should().BeNull("the panel is a declarative stencil, not a native renderer");
        definition.Roles.Should().BeEquivalentTo(schema.Roles);
        definition.DefaultWidth.Should().Be(schema.DefaultWidth);
        definition.DefaultHeight.Should().Be(schema.DefaultHeight);
        definition.Props.Select(prop => prop.Name).Should().Equal(MemoryProps);
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
    public void TheMemoryPack_DrawsEveryPropItOffers()
    {
        var entry = Entry();

        entry.Should().Contain("{title", "the heading is the title prop");
        entry.Should().Contain("\"prop\": \"memories\"", "the rows are repeated over the memories prop");
        entry.Should().Contain("{memory}", "and each row paints its own memory");
        entry.Should().Contain("{enabled", "the master switch follows the enabled prop");
        entry.Should().Contain("{filter", "the filter chip follows the filter prop");
    }

    [Fact]
    public void TheMemoryPack_SketchesTheGroupsTheStatusesAndTheSwitch()
    {
        var entry = Entry();

        entry.Should().Contain("Preferences", "a category header is part of the sketch");
        entry.Should().Contain("Active", "the statuses are drawn as words");
        entry.Should().Contain("Paused");
        entry.Should().Contain("To review");
        entry.Should().Contain("token(\\\"color.success", "an active memory is sketched in the success tone");
        entry.Should().Contain("token(\\\"color.warning", "a paused one in the warning tone");
    }

    private static string Entry() => BuiltInStencilPackProvider.ReadPackJson()
        .Split('\n')
        .Single(line => line.Contains("\"type\": \"TmMemoryPanel\""));
}
