using FluentAssertions;
using Tempo.Blazor.Components.Wireframe;
using Tempo.Blazor.Components.Wireframe.Models;

namespace Tempo.Blazor.Tests.Wireframe;

/// <summary>
/// Plan 3e96f6d8 phase 5: <c>TmContextUsage</c> is a wireframe citizen — it needs a UI role and a declarative
/// stencil, or the palette and the MCP catalogue drift away from the component library. The schema is the
/// contract the palette, the properties panel and the stencil pack share, so the pack's props must match it
/// exactly, in order, and the sketch must draw every prop it offers.
/// </summary>
public sealed class ContextUsageStencilTests
{
    private static readonly string[] UsageProps =
        ["title", "sections", "variant", "budget", "showTags", "showCompact"];

    [Fact]
    public void BuiltInSchema_ListsTheUsagePanel_WithItsRoleAndProps()
    {
        var schema = new BuiltInComponentSchemas().GetSchemas().Single(s => s.Type == "TmContextUsage");

        schema.Category.Should().Be("AI");
        schema.DisplayName.Should().Be("Context Usage");
        schema.Roles.Should().BeEquivalentTo("context-usage");
        schema.Props.Select(prop => prop.Name).Should().Equal(UsageProps);
        schema.Props.Single(prop => prop.Name == "variant").Options.Should().Equal("bar", "grid", "treemap");
        schema.DefaultWidth.Should().BePositive();
        schema.DefaultHeight.Should().BePositive();
    }

    [Fact]
    public void ThePropNamesAvoidTheStencilExpressionContext()
    {
        var schema = new BuiltInComponentSchemas().GetSchemas().Single(s => s.Type == "TmContextUsage");

        schema.Props.Should().NotContain(prop => prop.Name == "size",
            "the stencil expression context already owns `size` for the element box");
        schema.Props.Should().NotContain(prop => prop.Name == "repeat",
            "and `repeat` belongs to the repeat node");
    }

    [Fact]
    public void TheComponentsTypeNameResolvesItsRole()
    {
        var vocabulary = new UiRoleVocabulary([new BuiltInUiRoleVocabularySource()]);

        vocabulary.Find("TmContextUsage")!.Slug.Should().Be("context-usage");
        vocabulary.Find("context-usage")!.DisplayName.Should().Be("Context usage");
        vocabulary.Find("context window")!.Slug.Should().Be("context-usage");
        vocabulary.Find("vyuziti kontextu")!.Slug.Should().Be("context-usage");
    }

    [Fact]
    public void TheRoleIsInTheBuiltInVocabulary()
    {
        var vocabulary = new UiRoleVocabulary([new BuiltInUiRoleVocabularySource()]);
        var schema = new BuiltInComponentSchemas().GetSchemas().Single(s => s.Type == "TmContextUsage");

        schema.Roles!.Where(role => vocabulary.Find(role) is null).Should().BeEmpty();
    }

    [Fact]
    public void StencilPack_RendersTheUsagePanel_AndAgreesWithTheSchema()
    {
        var definition = new BuiltInStencilPackProvider().GetDefinitions()
            .Single(definition => definition.Type == "TmContextUsage");
        var schema = new BuiltInComponentSchemas().GetSchemas().Single(s => s.Type == "TmContextUsage");

        definition.IsBuiltIn.Should().BeTrue();
        definition.PackId.Should().Be("tempo");
        definition.NativeType.Should().BeNull("the usage panel is a declarative stencil, not a native renderer");
        definition.Roles.Should().BeEquivalentTo(schema.Roles);
        definition.DefaultWidth.Should().Be(schema.DefaultWidth);
        definition.DefaultHeight.Should().Be(schema.DefaultHeight);
        definition.Props.Select(prop => prop.Name).Should().Equal(UsageProps);
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
    public void TheUsagePack_DrawsEveryPropItOffers()
    {
        var entry = Entry("TmContextUsage");

        entry.Should().Contain("{title", "the panel is named by its title");
        entry.Should().Contain("{row}", "one legend row per entry of the sections prop");
        entry.Should().Contain("variant", "the presentation switch changes the sketch");
        entry.Should().Contain("{budget", "the budget is part of the readout");
        entry.Should().Contain("Required", "a section's state word is drawn when showTags is on");
        entry.Should().Contain("Compact", "the compaction action is drawn when showCompact is on");
    }

    [Fact]
    public void TheUsagePack_DrawsTheBarTheGridAndTheTreemap()
    {
        var entry = Entry("TmContextUsage");

        entry.Should().Contain("variant == \\\"grid\\\"", "the 100-cell grid has its own branch");
        entry.Should().Contain("variant == \\\"treemap\\\"", "the treemap has its own branch");
        entry.Should().Contain("token(\\\"color.primary", "the bar segments are painted in section tones");
        entry.Should().Contain("token(\\\"color.info", "a second section tone");
        entry.Should().Contain("token(\\\"color.danger", "and a third");
        entry.Should().Contain("88 %", "the readout states the share in words");
    }

    private static string Entry(string type) => BuiltInStencilPackProvider.ReadPackJson()
        .Split('\n')
        .Single(line => line.Contains($"\"type\": \"{type}\""));
}
