using FluentAssertions;
using Tempo.Blazor.Components.Wireframe;
using Tempo.Blazor.Components.Wireframe.Stencil;
using Xunit;

namespace Tempo.Blazor.Tests.Wireframe;

/// <summary>
/// Plan ff043b2e, phase 4: <c>TmFacetFilter</c> is a wireframe citizen — the palette, the MCP
/// catalogue and the component library must not drift, so the schema, the role vocabulary and the
/// declarative pack entry are pinned together. Every schema prop has to reach the drawing: a knob
/// the canvas ignores is a knob that does nothing, and a designer cannot tell what they are placing.
/// </summary>
public sealed class FacetFilterStencilTests
{
    [Fact]
    public void BuiltInSchema_ListsTheFacetFilter_WithItsRoleAndProps()
    {
        var schema = new BuiltInComponentSchemas().GetSchemas().Single(s => s.Type == "TmFacetFilter");

        schema.Category.Should().Be("Data Display");
        schema.DisplayName.Should().Be("Facet Filter");
        schema.Roles.Should().BeEquivalentTo("facet-filter");
        schema.Props.Select(prop => prop.Name).Should()
            .Equal("title", "presentation", "showSearch", "showCounts", "showChips", "collapsed");
        schema.DefaultWidth.Should().BePositive();
        schema.DefaultHeight.Should().BePositive();
    }

    [Fact]
    public void TheComponentTypeNameResolvesItsRole_SoEveryComponentHasOne()
    {
        var vocabulary = new UiRoleVocabulary([new BuiltInUiRoleVocabularySource()]);

        vocabulary.Find("TmFacetFilter")!.Slug.Should().Be("facet-filter");
    }

    [Fact]
    public void TheRoleIsSearchableByItsAliases()
    {
        var vocabulary = new UiRoleVocabulary([new BuiltInUiRoleVocabularySource()]);

        vocabulary.Find("facet filter").Should().NotBeNull();
        vocabulary.Find("filtr faset").Should().NotBeNull();
    }

    [Fact]
    public void StencilPack_RendersTheComponent_AndAgreesWithTheSchema()
    {
        var definition = new BuiltInStencilPackProvider().GetDefinitions().Single(d => d.Type == "TmFacetFilter");
        var schema = new BuiltInComponentSchemas().GetSchemas().Single(s => s.Type == "TmFacetFilter");

        definition.IsBuiltIn.Should().BeTrue();
        definition.PackId.Should().Be("tempo");
        definition.NativeType.Should().BeNull("the facet filter is a declarative stencil, not a native renderer");
        definition.Roles.Should().BeEquivalentTo(schema.Roles);
        definition.DefaultWidth.Should().Be(schema.DefaultWidth);
        definition.DefaultHeight.Should().Be(schema.DefaultHeight);
        definition.Props.Select(prop => prop.Name).Should().Equal(schema.Props.Select(prop => prop.Name));
        definition.RenderSvg.Should().NotBeNull("the declarative pack render is what the editor canvas paints");
    }

    [Fact]
    public void Pack_RendersEverySchemaPropConditionally()
    {
        var entry = Entry("TmFacetFilter");

        entry.Should().Contain("{title");
        entry.Should().Contain("{presentation", "the inline presentation has no trigger, the canvas has to show that");
        entry.Should().Contain("{showSearch");
        entry.Should().Contain("{showCounts");
        entry.Should().Contain("(showChips ?? true)",
            "the chip row is opt-out and, below 220 px, also width-gated so it never paints under the footer button");
        entry.Should().Contain("{collapsed");
        entry.Should().Contain("token(", "the drawing paints with design tokens, never with raw palette steps");
    }

    [Fact]
    public void ChipBranch_ShowsTwoDistinctFilters_AndNeitherIsEllipsisedAtTheDefaultWidth()
    {
        // Phase 16 of plan 7ba95ed6: the chip row used to read "Category: Boo…" next to "Books" — the same
        // filter twice, one of them cut off. The row now carries two DIFFERENT active filters and each pill
        // is sized from its own label (5 px per glyph at the sketch's 8 px font plus 10 px padding), so
        // nothing has to be ellipsised inside a 240 px wide default element.
        using var pack = System.Text.Json.JsonDocument.Parse(BuiltInStencilPackProvider.ReadPackJson());
        var component = pack.RootElement.GetProperty("components").EnumerateArray()
            .Single(entry => entry.GetProperty("type").GetString() == "TmFacetFilter");
        var chips = component.GetProperty("render").GetProperty("children").EnumerateArray()
            .Single(node => node.TryGetProperty("when", out var when)
                            && when.GetString()!.Contains("showChips", StringComparison.Ordinal))
            .GetProperty("children").EnumerateArray()
            .ToArray();

        var pills = chips.Where(node => node.GetProperty("kind").GetString() == "rect").ToArray();
        var texts = chips.Where(node => node.GetProperty("kind").GetString() == "text").ToArray();

        pills.Should().HaveCount(2, "the sketch shows a row of active-filter chips");
        texts.Should().HaveCount(2);

        var labels = texts.Select(node => node.GetProperty("text").GetString()!).ToArray();
        labels.Should().Equal("Books", "In stock");
        labels.Should().OnlyHaveUniqueItems(
            "two chips that read almost the same thing look like one filter drawn twice");
        labels.Should().NotContain(
            label => label.Contains(':', StringComparison.Ordinal),
            "a chip names the VALUE; the facet name belongs to the group header above it");

        const double glyphWidth = 5;
        const double pillPadding = 10;
        const double defaultWidth = 240;
        const double footerButtonWidth = 104;
        const double gutter = 12;

        // The pack cannot measure text, so the pill widths (35 and 50) and the text widths (25 and 40)
        // are literals derived from THESE two labels. Editing a label without editing its pill silently
        // reintroduces the ellipsis this test is about — the assertions below are the guard that has to
        // move with it.
        for (var i = 0; i < labels.Length; i++)
        {
            var label = labels[i];
            var pillX = pills[i].GetProperty("x").GetDouble();
            var pillWidth = pills[i].GetProperty("w").GetDouble();
            var textX = texts[i].GetProperty("x").GetDouble();
            var textWidth = texts[i].GetProperty("w").GetDouble();

            textWidth.Should().BeGreaterThanOrEqualTo(
                label.Length * glyphWidth,
                $"'{label}' needs about {label.Length * glyphWidth} px at the sketch's 8 px font");
            (textX - pillX).Should().BeGreaterThan(0, "the label is inset inside its pill");
            (textX - pillX + textWidth).Should().BeLessThanOrEqualTo(
                pillWidth,
                $"the whole of '{label}' stays inside its pill, so nothing is ellipsised at {defaultWidth} px");
            pillWidth.Should().BeGreaterThanOrEqualTo(
                label.Length * glyphWidth + pillPadding,
                $"the pill for '{label}' is derived from the label length");
        }

        var firstRight = pills[0].GetProperty("x").GetDouble() + pills[0].GetProperty("w").GetDouble();
        pills[1].GetProperty("x").GetDouble().Should().BeGreaterThan(firstRight, "the two pills do not overlap");

        var lastRight = pills[1].GetProperty("x").GetDouble() + pills[1].GetProperty("w").GetDouble();
        lastRight.Should().BeLessThan(
            defaultWidth - gutter - footerButtonWidth,
            "the chip row leaves the 'Show {n} results' button its own space in the footer row");
    }

    [Fact]
    public void ChipBranch_IsHiddenOnANarrowBox_SoTheChipsNeverPaintUnderTheFooterButton()
    {
        // The pills sit at fixed x positions while the "Show {n} results" button is anchored at
        // size.w - 116, so on a box narrower than ~220 px the row would paint chips UNDER the button.
        // The pack therefore draws the row of chips only when the box can hold both (phase 16 review).
        using var pack = System.Text.Json.JsonDocument.Parse(BuiltInStencilPackProvider.ReadPackJson());
        var chipsWhen = pack.RootElement.GetProperty("components").EnumerateArray()
            .Single(entry => entry.GetProperty("type").GetString() == "TmFacetFilter")
            .GetProperty("render").GetProperty("children").EnumerateArray()
            .Single(node => node.TryGetProperty("when", out var when)
                            && when.GetString()!.Contains("showChips", StringComparison.Ordinal))
            .GetProperty("when").GetString()!;

        var evaluator = new StencilEvaluator();
        bool DrawsChips(double width) => evaluator
            .Evaluate(chipsWhen, new StencilEvalContext(new Dictionary<string, object?>(), width, 220, 0, null))
            .AsBool();

        DrawsChips(240).Should().BeTrue("the 240 px default box holds both the chips and the button");
        DrawsChips(220).Should().BeTrue("at 220 px the last pill (ending at 103) still clears the button (x 104)");
        DrawsChips(200).Should().BeFalse(
            "at 200 px the button starts at x 84, so the chip row would paint underneath it");
    }

    private static string Entry(string type) => BuiltInStencilPackProvider.ReadPackJson()
        .Split('\n')
        .Single(line => line.Contains($"\"type\": \"{type}\""));
}
