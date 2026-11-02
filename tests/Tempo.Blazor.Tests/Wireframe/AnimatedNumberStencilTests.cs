using FluentAssertions;
using Tempo.Blazor.Components.Wireframe;
using Tempo.Blazor.Components.Wireframe.Models;
using Xunit;

namespace Tempo.Blazor.Tests.Wireframe;

/// <summary>
/// Plan ff043b2e, phase 1: <c>TmAnimatedNumber</c> is a wireframe citizen (a new component must have a
/// role and a stencil), and the additive <c>TmStatCard</c> KPI surface is reflected in its schema —
/// a designer placing a KPI card must be able to declare the delta, the comparison label, the
/// sparkline and the loading state, or the palette and the MCP catalogue drift from the component.
/// </summary>
public sealed class AnimatedNumberStencilTests
{
    [Fact]
    public void BuiltInSchema_ListsTheAnimatedNumber_WithItsRoleAndProps()
    {
        var schema = new BuiltInComponentSchemas().GetSchemas().Single(s => s.Type == "TmAnimatedNumber");

        schema.Category.Should().Be("Data Display");
        schema.DisplayName.Should().Be("Animated Number");
        schema.Roles.Should().BeEquivalentTo("animated-number");
        schema.Props.Select(prop => prop.Name).Should()
            .Equal("value", "format", "prefix", "suffix", "announce");
        schema.DefaultWidth.Should().BePositive();
        schema.DefaultHeight.Should().BePositive();
    }

    [Fact]
    public void TheComponentsTypeNameResolvesItsRole_SoEveryComponentHasOne()
    {
        var vocabulary = new UiRoleVocabulary([new BuiltInUiRoleVocabularySource()]);

        vocabulary.Find("TmAnimatedNumber")!.Slug.Should().Be("animated-number");
        vocabulary.Find("animated-number")!.Synonyms.Should()
            .Contain("number ticker").And.Contain("kpi value");
    }

    [Fact]
    public void StencilPack_RendersTheAnimatedNumber_AndAgreesWithTheSchema()
    {
        var definition = new BuiltInStencilPackProvider().GetDefinitions().Single(d => d.Type == "TmAnimatedNumber");
        var schema = new BuiltInComponentSchemas().GetSchemas().Single(s => s.Type == "TmAnimatedNumber");

        definition.IsBuiltIn.Should().BeTrue();
        definition.PackId.Should().Be("tempo");
        definition.NativeType.Should().BeNull("the number is a declarative stencil, not a native renderer");
        definition.Roles.Should().BeEquivalentTo(schema.Roles);
        definition.DefaultWidth.Should().Be(schema.DefaultWidth);
        definition.DefaultHeight.Should().Be(schema.DefaultHeight);
        definition.Props.Select(prop => prop.Name).Should().Equal(schema.Props.Select(prop => prop.Name));
        definition.RenderSvg.Should().NotBeNull("the declarative pack render is what the editor canvas paints");
    }

    [Fact]
    public void StatCardSchema_OffersTheKpiSurface()
    {
        var schema = new BuiltInComponentSchemas().GetSchemas().Single(s => s.Type == "TmStatCard");

        schema.Props.Select(prop => prop.Name).Should().Contain(
        [
            "delta", "deltaFormat", "comparisonLabel", "trend", "positiveIsGood",
            "icon", "sparkline", "loading", "animateValue"
        ], "a wireframe KPI card must be able to declare every 2.10.0 part");

        schema.Props.Single(prop => prop.Name == "positiveIsGood").Default.Should().Be(true);
        schema.Props.Single(prop => prop.Name == "delta").Type.Should().Be(PropType.Double);
    }

    /// <summary>
    /// The pack render must branch on the new props: a schema prop the drawing ignores is a knob that
    /// does nothing, which is exactly the drift this test exists to catch.
    /// </summary>
    [Fact]
    public void StatCardPack_RendersTheNewPartsConditionally()
    {
        var entry = BuiltInStencilPackProvider.ReadPackJson()
            .Split('\n')
            .Single(line => line.Contains("\"type\": \"TmStatCard\""));

        entry.Should().Contain("{delta}");
        entry.Should().Contain("{comparisonLabel}");
        entry.Should().Contain("{sparkline}");
        entry.Should().Contain("{loading}");
    }
}
