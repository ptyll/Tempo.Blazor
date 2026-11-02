using FluentAssertions;
using Tempo.Blazor.Components.Wireframe;
using Tempo.Blazor.Components.Wireframe.Models;
using Xunit;

namespace Tempo.Blazor.Tests.Wireframe;

/// <summary>
/// Plan ff043b2e, phase 2: <c>TmTrendList</c> and <c>TmBreakdownCard</c> are wireframe citizens — a
/// new component must have a UI role and a stencil, or the palette and the MCP catalogue drift from
/// the component library. The pack render has to branch on the props the palette offers, because a
/// schema knob the drawing ignores is a knob that does nothing.
/// </summary>
public sealed class TrendListBreakdownCardStencilTests
{
    [Fact]
    public void BuiltInSchema_ListsTheTrendList_WithItsRoleAndProps()
    {
        var schema = new BuiltInComponentSchemas().GetSchemas().Single(s => s.Type == "TmTrendList");

        schema.Category.Should().Be("Data Display");
        schema.DisplayName.Should().Be("Trend List");
        schema.Roles.Should().BeEquivalentTo("trend-list");
        schema.Props.Select(prop => prop.Name).Should()
            .Equal("title", "layout", "loading", "showDelta", "showSparkline");
        schema.DefaultWidth.Should().BePositive();
        schema.DefaultHeight.Should().BePositive();
    }

    [Fact]
    public void BuiltInSchema_ListsTheBreakdownCard_WithItsRoleAndProps()
    {
        var schema = new BuiltInComponentSchemas().GetSchemas().Single(s => s.Type == "TmBreakdownCard");

        schema.Category.Should().Be("Data Display");
        schema.DisplayName.Should().Be("Breakdown Card");
        schema.Roles.Should().BeEquivalentTo("breakdown-card");
        schema.Props.Select(prop => prop.Name).Should()
            .Equal("title", "total", "showTotal", "showBar", "showPeriod", "loading");
        schema.DefaultWidth.Should().BePositive();
        schema.DefaultHeight.Should().BePositive();
    }

    [Theory]
    [InlineData("TmTrendList", "trend-list")]
    [InlineData("TmBreakdownCard", "breakdown-card")]
    public void TheComponentsTypeNameResolvesItsRole_SoEveryComponentHasOne(string type, string role)
    {
        var vocabulary = new UiRoleVocabulary([new BuiltInUiRoleVocabularySource()]);

        vocabulary.Find(type)!.Slug.Should().Be(role);
    }

    [Theory]
    [InlineData("TmTrendList")]
    [InlineData("TmBreakdownCard")]
    public void StencilPack_RendersTheComponent_AndAgreesWithTheSchema(string type)
    {
        var definition = new BuiltInStencilPackProvider().GetDefinitions().Single(d => d.Type == type);
        var schema = new BuiltInComponentSchemas().GetSchemas().Single(s => s.Type == type);

        definition.IsBuiltIn.Should().BeTrue();
        definition.PackId.Should().Be("tempo");
        definition.NativeType.Should().BeNull("both components are declarative stencils, not native renderers");
        definition.Roles.Should().BeEquivalentTo(schema.Roles);
        definition.DefaultWidth.Should().Be(schema.DefaultWidth);
        definition.DefaultHeight.Should().Be(schema.DefaultHeight);
        definition.Props.Select(prop => prop.Name).Should().Equal(schema.Props.Select(prop => prop.Name));
        definition.RenderSvg.Should().NotBeNull("the declarative pack render is what the editor canvas paints");
    }

    [Fact]
    public void TrendListPack_RendersTheNewPartsConditionally()
    {
        var entry = Entry("TmTrendList");

        entry.Should().Contain("{title");
        entry.Should().Contain("{layout ==", "the table layout has to look different on the canvas");
        entry.Should().Contain("table", "the table branch draws column headers");
        entry.Should().Contain("{showDelta}");
        entry.Should().Contain("{showSparkline}");
        entry.Should().Contain("{loading}");
    }

    [Fact]
    public void BreakdownCardPack_RendersTheNewPartsConditionally()
    {
        var entry = Entry("TmBreakdownCard");

        entry.Should().Contain("{title");
        entry.Should().Contain("{total");
        entry.Should().Contain("{showTotal}");
        entry.Should().Contain("{showBar}");
        entry.Should().Contain("{showPeriod}");
        entry.Should().Contain("{loading}");
    }

    private static string Entry(string type) => BuiltInStencilPackProvider.ReadPackJson()
        .Split('\n')
        .Single(line => line.Contains($"\"type\": \"{type}\""));
}
