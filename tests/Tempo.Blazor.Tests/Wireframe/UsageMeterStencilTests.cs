using FluentAssertions;
using Tempo.Blazor.Components.Wireframe;
using Tempo.Blazor.Components.Wireframe.Models;
using Xunit;

namespace Tempo.Blazor.Tests.Wireframe;

/// <summary>
/// Plan ff043b2e phase 7: <c>TmUsageMeter</c> is a wireframe citizen — a new component needs a UI role
/// and a declarative stencil whose render branches on the props the palette offers, or the palette and
/// the MCP catalogue drift away from the component library. The schema is the contract the palette,
/// the properties panel and the stencil pack share, so the pack's props must match it exactly, in
/// order.
/// </summary>
public sealed class UsageMeterStencilTests
{
    private static readonly string[] ExpectedProps =
        ["title", "used", "limit", "unit", "showReset", "showDaily", "showCategories", "compact"];

    [Fact]
    public void BuiltInSchema_ListsTheUsageMeter_WithItsRoleAndProps()
    {
        var schema = new BuiltInComponentSchemas().GetSchemas().Single(s => s.Type == "TmUsageMeter");

        schema.Category.Should().Be("Data Display");
        schema.DisplayName.Should().Be("Usage Meter");
        schema.Roles.Should().BeEquivalentTo("usage-meter");
        schema.Props.Select(prop => prop.Name).Should().Equal(ExpectedProps);
        schema.DefaultWidth.Should().BePositive();
        schema.DefaultHeight.Should().BePositive();
    }

    [Fact]
    public void TheComponentsTypeNameResolvesItsRole()
    {
        var vocabulary = new UiRoleVocabulary([new BuiltInUiRoleVocabularySource()]);

        vocabulary.Find("TmUsageMeter")!.Slug.Should().Be("usage-meter");
        vocabulary.Find("usage-meter")!.DisplayName.Should().Be("Usage meter");
        vocabulary.Find("quota meter")!.Slug.Should().Be("usage-meter");
    }

    [Fact]
    public void StencilPack_RendersTheComponent_AndAgreesWithTheSchema()
    {
        var definition = new BuiltInStencilPackProvider().GetDefinitions().Single(d => d.Type == "TmUsageMeter");
        var schema = new BuiltInComponentSchemas().GetSchemas().Single(s => s.Type == "TmUsageMeter");

        definition.IsBuiltIn.Should().BeTrue();
        definition.PackId.Should().Be("tempo");
        definition.NativeType.Should().BeNull("the meter is a declarative stencil, not a native renderer");
        definition.Roles.Should().BeEquivalentTo(schema.Roles);
        definition.DefaultWidth.Should().Be(schema.DefaultWidth);
        definition.DefaultHeight.Should().Be(schema.DefaultHeight);
        definition.Props.Select(prop => prop.Name).Should().Equal(schema.Props.Select(prop => prop.Name));
        definition.RenderSvg.Should().NotBeNull("the declarative pack render is what the editor canvas paints");
    }

    [Fact]
    public void UsageMeterPack_DrawsEveryPropItOffers()
    {
        var entry = Entry();

        entry.Should().Contain("{title", "the card is named by its title");
        entry.Should().Contain("\"when\": \"{title}\"",
            "the compact presentation keeps the title (the component renders it as a visible label)");
        entry.Should().Contain("{used", "the fill width follows the used amount");
        entry.Should().Contain("used / limit", "the used amount is measured against the limit");
        entry.Should().Contain("+ unit}", "the readout carries the host's unit");
        entry.Should().Contain("showReset &&", "the reset line is a host-supplied date");
        entry.Should().Contain("showDaily &&", "the daily history bars are optional");
        entry.Should().Contain("showCategories}", "the breakdown segments and rows are optional");
        entry.Should().Contain("!compact}", "the compact presentation drops the chrome");
    }

    [Fact]
    public void UsageMeterPack_DrawsTheStateWithoutRelyingOnColourAlone()
    {
        var entry = Entry();

        entry.Should().Contain("{used >= limit * 0.8", "the warning state is derived from the thresholds");
        entry.Should().Contain("Near limit", "a state ships as TEXT as well as a tone");
        entry.Should().Contain("Limit reached", "the critical state is spelled out too");
        entry.Should().Contain("token(\\\"color.danger", "the critical fill is the destructive token");
    }

    private static string Entry() => BuiltInStencilPackProvider.ReadPackJson()
        .Split('\n')
        .Single(line => line.Contains("\"type\": \"TmUsageMeter\""));
}
