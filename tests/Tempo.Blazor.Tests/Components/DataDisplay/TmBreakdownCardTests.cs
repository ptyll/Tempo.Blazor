using System.Globalization;
using System.Text.RegularExpressions;
using Bunit;
using FluentAssertions;
using Tempo.Blazor.Components.DataDisplay;
using Tempo.Blazor.Models;
using Tempo.Blazor.Tests.Localization;
using Tempo.Blazor.Tests.Theme;

namespace Tempo.Blazor.Tests.Components.DataDisplay;

/// <summary>
/// Plan ff043b2e phase 2: <c>TmBreakdownCard</c> — a total, a proportional strip and a table of
/// parts sorted by value. The percentages come from a largest-remainder split, so the rounded shares
/// always add up to the target (100 % for the usual total = sum of the parts), and the visual strip
/// is decorative: the table is what a screen reader (and a copy/paste) gets.
/// </summary>
public class TmBreakdownCardTests : LocalizationTestBase
{
    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en-US");
    private static readonly CultureInfo Czech = CultureInfo.GetCultureInfo("cs-CZ");

    private static BreakdownPart Part(string id, string label, double value, string? color = null)
        => new() { Id = id, Label = label, Value = value, Color = color };

    private static IReadOnlyList<BreakdownPart> SampleParts() =>
    [
        Part("direct", "Direct", 40),
        Part("search", "Search", 35),
        Part("social", "Social", 25)
    ];

    [Fact]
    public void Root_RendersTheBaseClassAndTheDefaultTestId()
    {
        var cut = Render<TmBreakdownCard>(p => p.Add(c => c.Parts, SampleParts()));

        cut.Find(".tm-breakdown-card").Should().NotBeNull();
        cut.Find("[data-testid='breakdown-card']").Should().NotBeNull();
    }

    [Fact]
    public void EmptyState_RendersTheLocalizedMessage_AndNoTable()
    {
        var cut = Render<TmBreakdownCard>();

        cut.Find(".tm-breakdown-card__empty").TextContent.Trim().Should().Be("No data to show");
        cut.FindAll("table").Should().BeEmpty();
        cut.FindAll(".tm-breakdown-card__segment").Should().BeEmpty();
    }

    [Fact]
    public void LoadingState_IsBusyAndSkeletonised_AndRendersNoRows()
    {
        var cut = Render<TmBreakdownCard>(p => p
            .Add(c => c.Parts, SampleParts())
            .Add(c => c.IsLoading, true));

        var root = cut.Find(".tm-breakdown-card");
        root.GetAttribute("aria-busy").Should().Be("true");
        cut.FindAll(".tm-skeleton").Should().NotBeEmpty();
        cut.FindAll(".tm-breakdown-card__row").Should().BeEmpty();
        root.TextContent.Should().Contain("Loading breakdown");
    }

    [Fact]
    public void Rows_AreSortedByValueDescending()
    {
        var cut = Render<TmBreakdownCard>(p => p
            .Add(c => c.Parts, [Part("a", "Small", 10), Part("b", "Big", 60), Part("c", "Medium", 30)]));

        var labels = cut.FindAll(".tm-breakdown-card__row .tm-breakdown-card__label")
            .Select(cell => cell.TextContent.Trim().TrimStart('·').Trim())
            .ToList();

        labels.Should().Equal(new[] { "Big", "Medium", "Small" },
            "a breakdown reads biggest-first — the bar and the rows keep the same order");
    }

    [Fact]
    public void Total_DefaultsToTheSumOfTheParts()
    {
        var cut = Render<TmBreakdownCard>(p => p
            .Add(c => c.Parts, SampleParts())
            .Add(c => c.Culture, English));

        cut.Find(".tm-breakdown-card__total").TextContent.Should().Contain("100");
    }

    [Fact]
    public void Total_OverrideWins_AndProducesARemainderRow()
    {
        var cut = Render<TmBreakdownCard>(p => p
            .Add(c => c.Parts, [Part("a", "A", 25), Part("b", "B", 15)])
            .Add(c => c.Total, 100)
            .Add(c => c.Culture, English));

        cut.Find(".tm-breakdown-card__total").TextContent.Should().Contain("100",
            "the declared total wins over the parts' sum of 40");
        cut.FindAll(".tm-breakdown-card__row").Should().HaveCount(3,
            "the unexplained rest is a row, so the visible shares still add up to 100 %");
        cut.FindAll(".tm-breakdown-card__share").Select(share => share.TextContent.Trim())
            .Should().Equal(new[] { "25%", "15%", "60%" },
                "each part is a quarter/sixth of the declared whole and the rest is explicit");
        cut.Find(".tm-breakdown-card__row--remainder .tm-breakdown-card__label").TextContent.Trim()
            .Should().Be("Other");
        cut.FindAll(".tm-breakdown-card__segment--remainder").Should().ContainSingle();
    }

    [Fact]
    public void Total_BelowThePartsSum_IsIgnoredForTheShares()
    {
        var cut = Render<TmBreakdownCard>(p => p
            .Add(c => c.Parts, [Part("a", "A", 60), Part("b", "B", 40)])
            .Add(c => c.Total, 50)
            .Add(c => c.Culture, English));

        cut.Find(".tm-breakdown-card__total").TextContent.Should().Contain("100",
            "a total that cannot contain its parts means the host data is inconsistent — the card "
            + "falls back to the parts sum instead of rendering shares above 100 %");
        cut.FindAll(".tm-breakdown-card__row").Should().HaveCount(2);
        cut.FindAll(".tm-breakdown-card__share").Select(share => share.TextContent.Trim())
            .Should().Equal(new[] { "60%", "40%" });
    }

    [Fact]
    public void Shares_AreRoundedSoTheySumToExactlyOneHundred()
    {
        var cut = Render<TmBreakdownCard>(p => p
            .Add(c => c.Parts, [Part("a", "A", 1), Part("b", "B", 1), Part("c", "C", 1)])
            .Add(c => c.Culture, English));

        var shares = cut.FindAll(".tm-breakdown-card__share")
            .Select(share => share.TextContent.Trim())
            .ToList();

        shares.Should().Equal(new[] { "34%", "33%", "33%" },
            "the largest remainder decides the leftover point: the sum is 100 %, never 99 or 101");
    }

    [Fact]
    public void AllZeroValues_RenderZeroShares_AndNoSegments()
    {
        var cut = Render<TmBreakdownCard>(p => p
            .Add(c => c.Parts, [Part("a", "A", 0), Part("b", "B", 0)])
            .Add(c => c.Culture, English));

        cut.FindAll(".tm-breakdown-card__share").Select(share => share.TextContent.Trim())
            .Should().Equal(new[] { "0%", "0%" });
        cut.FindAll(".tm-breakdown-card__segment").Should().BeEmpty("a zero total has no proportion to paint");
        cut.FindAll(".tm-breakdown-card__row").Should().HaveCount(2, "the rows are still the data");
    }

    [Fact]
    public void NegativeValue_CountsAsZeroForTheShare_ButTheRowStillShowsTheData()
    {
        var cut = Render<TmBreakdownCard>(p => p
            .Add(c => c.Parts, [Part("a", "Returns", -40), Part("b", "Sales", 100)])
            .Add(c => c.Culture, English));

        cut.FindAll(".tm-breakdown-card__row").Select(row => row.TextContent.Trim())
            .Should().HaveCount(2, "the negative part is not silently dropped");
        cut.FindAll(".tm-breakdown-card__share").Select(share => share.TextContent.Trim())
            .Should().Equal(new[] { "100%", "0%" },
                "a negative amount contributes nothing to the whole, so the shares still sum to 100 %");
        cut.FindAll(".tm-breakdown-card__value").Select(value => value.TextContent.Trim())
            .Should().Contain("-40", "the host still sees the value that was measured");
    }

    [Fact]
    public void Shares_AreFormattedByTheCulture()
    {
        var cut = Render<TmBreakdownCard>(p => p
            .Add(c => c.Parts, [Part("a", "A", 40), Part("b", "B", 60)])
            .Add(c => c.Culture, Czech));

        cut.FindAll(".tm-breakdown-card__share").Select(share => share.TextContent)
            .Should().Equal(new[] { "60\u00A0%", "40\u00A0%" },
                "the percent symbol and its spacing follow the culture, exactly like a KPI delta");
    }

    [Fact]
    public void Bar_IsDecorative_AndCarriesOneSegmentPerVisibleShare()
    {
        var cut = Render<TmBreakdownCard>(p => p
            .Add(c => c.Parts, SampleParts())
            .Add(c => c.Culture, English));

        var bar = cut.Find(".tm-breakdown-card__bar");
        bar.GetAttribute("aria-hidden").Should().Be("true",
            "the table carries the data; a painted strip must not be announced twice");
        bar.QuerySelectorAll(".tm-breakdown-card__segment").Length.Should().Be(3);
        bar.QuerySelectorAll(".tm-breakdown-card__segment")[0].GetAttribute("style")
            .Should().Contain("flex-grow:40", "the segment widths are the shares");
    }

    [Fact]
    public void SegmentColor_WinsOverTheFallbackTone_AndAnInvalidColorIsDropped()
    {
        var custom = Render<TmBreakdownCard>(p => p
            .Add(c => c.Parts, [Part("a", "A", 100, "var(--tm-color-primary)")]));

        custom.Find(".tm-breakdown-card__segment").GetAttribute("style")
            .Should().Contain("background-color:var(--tm-color-primary)");
        custom.Find(".tm-breakdown-card__swatch").GetAttribute("style")
            .Should().Contain("background-color:var(--tm-color-primary)");

        var invalid = Render<TmBreakdownCard>(p => p
            .Add(c => c.Parts, [Part("a", "A", 100, "red; background:url(https://x/y)")]));

        invalid.Find(".tm-breakdown-card__segment").GetAttribute("style")
            .Should().NotContain("url(", "an arbitrary value must never reach the style attribute");
        invalid.Find(".tm-breakdown-card__segment").ClassList.Should()
            .Contain("tm-breakdown-card__segment--tone-1", "without a usable colour the token tone paints it");
    }

    [Fact]
    public void Table_CarriesTheColumnsAndARowHeaderPerPart()
    {
        var cut = Render<TmBreakdownCard>(p => p
            .Add(c => c.Parts, SampleParts())
            .Add(c => c.Culture, English));

        var table = cut.Find("table.tm-breakdown-card__table");
        table.Should().NotBeNull();
        cut.FindAll("table.tm-breakdown-card__table caption.tm-sr-only").Should().ContainSingle(
            "the table is the accessible presentation, so it needs a name");
        var headers = cut.FindAll("table.tm-breakdown-card__table thead th");
        headers.Select(th => th.TextContent.Trim()).Should()
            .Equal(new[] { "Item", "Value", "Share" });
        headers.Should().OnlyContain(th => th.GetAttribute("scope") == "col");
        cut.FindAll("table.tm-breakdown-card__table tbody th")
            .Should().OnlyContain(th => th.GetAttribute("scope") == "row");
    }

    [Fact]
    public void Values_UseTheCultureAndTheValueFormat()
    {
        var cut = Render<TmBreakdownCard>(p => p
            .Add(c => c.Parts, [Part("a", "A", 1234.5)])
            .Add(c => c.Total, 1234.5)
            .Add(c => c.ValueFormat, "N1")
            .Add(c => c.Culture, English));

        cut.Find(".tm-breakdown-card__value").TextContent.Trim().Should().Be("1,234.5");
        cut.Find(".tm-breakdown-card__total").TextContent.Should().Contain("1,234.5");
    }

    [Fact]
    public void PeriodOptions_RenderASegmentedSwitcher_AndRaiseTheBoundValue()
    {
        string? changed = null;
        var cut = Render<TmBreakdownCard>(p => p
            .Add(c => c.Parts, SampleParts())
            .Add(c => c.PeriodOptions, new List<SelectOption<string>>
            {
                new("month", "This month"),
                new("week", "This week")
            })
            .Add(c => c.Period, "month")
            .Add(c => c.PeriodChanged, (string? value) => changed = value));

        var period = cut.Find(".tm-breakdown-card__period");
        period.Should().NotBeNull();
        period.QuerySelector("[role='radiogroup']").Should().NotBeNull(
            "the periods are one value out of a set, so the switcher keeps radio semantics");
        cut.FindAll(".tm-breakdown-card__period [role='radio']").Should().HaveCount(2);
        cut.Find(".tm-breakdown-card__period .tm-segmented__label").TextContent.Trim().Should().Be("Period",
            "the switcher is named for a reader, not only by convention");

        cut.FindAll(".tm-breakdown-card__period [role='radio']")[1].Click();

        changed.Should().Be("week");
    }

    [Fact]
    public void WithoutPeriodOptions_NoSwitcherRenders()
    {
        var cut = Render<TmBreakdownCard>(p => p.Add(c => c.Parts, SampleParts()));

        cut.FindAll(".tm-breakdown-card__period").Should().BeEmpty();
    }

    [Fact]
    public void ShowTotalAndShowBar_AreOptOuts()
    {
        var cut = Render<TmBreakdownCard>(p => p
            .Add(c => c.Parts, SampleParts())
            .Add(c => c.ShowTotal, false)
            .Add(c => c.ShowBar, false));

        cut.FindAll(".tm-breakdown-card__total").Should().BeEmpty();
        cut.FindAll(".tm-breakdown-card__bar").Should().BeEmpty();
        cut.FindAll(".tm-breakdown-card__row").Should().HaveCount(3, "the data itself stays");
    }

    [Fact]
    public void Title_RendersAsAHeading_AndNamesTheTable()
    {
        var cut = Render<TmBreakdownCard>(p => p
            .Add(c => c.Parts, SampleParts())
            .Add(c => c.Title, "Traffic by channel"));

        var title = cut.Find(".tm-breakdown-card__title");
        title.TextContent.Trim().Should().Be("Traffic by channel");
        var caption = cut.Find("table.tm-breakdown-card__table caption");
        caption.TextContent.Trim().Should().Be("Traffic by channel");
        caption.ClassList.Should().Contain("tm-sr-only");
    }

    [Fact]
    public void AriaLabel_NamesTheTableWhenThereIsNoTitle()
    {
        var cut = Render<TmBreakdownCard>(p => p
            .Add(c => c.Parts, SampleParts())
            .Add(c => c.AriaLabel, "Revenue split"));

        cut.Find("table.tm-breakdown-card__table caption").TextContent.Trim().Should().Be("Revenue split");

        var unnamed = Render<TmBreakdownCard>(p => p.Add(c => c.Parts, SampleParts()));
        unnamed.Find("table.tm-breakdown-card__table caption").TextContent.Trim().Should().Be("Breakdown");
    }

    [Fact]
    public void CzechResources_ResolveTheEmptyStateAndTheTotalLabel()
    {
        UseCzechLocalization();

        var cut = Render<TmBreakdownCard>();

        cut.Find(".tm-breakdown-card__empty").TextContent.Trim().Should().Be("Žádná data k zobrazení");

        var withData = Render<TmBreakdownCard>(p => p
            .Add(c => c.Parts, SampleParts())
            .Add(c => c.Culture, Czech));
        withData.Find(".tm-breakdown-card__total").TextContent.Should().Contain("Celkem");
    }

    [Fact]
    public void TestIdPrefix_NamespacesTheRows()
    {
        var cut = Render<TmBreakdownCard>(p => p
            .Add(c => c.Parts, SampleParts())
            .Add(c => c.TestIdPrefix, "traffic"));

        cut.FindAll("[data-testid='traffic-breakdown-row']").Should().HaveCount(3);
    }

    // ── the row key: a host id may repeat or be missing without breaking the render ──────────

    [Fact]
    public void Rows_WithoutIds_RenderOneRowPerPart()
    {
        var cut = Render<TmBreakdownCard>(p => p.Add(c => c.Parts, new[]
        {
            new BreakdownPart { Label = "Direct", Value = 40 },
            new BreakdownPart { Label = "Search", Value = 35 },
            new BreakdownPart { Label = "Social", Value = 25 }
        }));

        cut.FindAll("tbody tr").Should().HaveCount(3,
            "Id defaults to an empty string, so three parts written with only Label/Value must still "
            + "render three rows — @key must never collapse them onto one value");
        cut.FindAll("tbody tr").Select(row => row.GetAttribute("data-part-id"))
            .Should().OnlyContain(id => id == string.Empty,
                "data-part-id stays the host's raw value, never the internal row key");
        cut.FindAll("tbody tr th.tm-breakdown-card__label").Select(cell => cell.TextContent.Trim())
            .Should().Equal(new[] { "Direct", "Search", "Social" });

        // The first render builds the tree, so the key check only runs on the DIFF after a parameter
        // change — that is where three empty ids collided and took the whole card down.
        cut.Render(p => p.Add(c => c.Parts, new[]
        {
            new BreakdownPart { Label = "Direct", Value = 10 },
            new BreakdownPart { Label = "Search", Value = 85 },
            new BreakdownPart { Label = "Social", Value = 5 }
        }));

        cut.FindAll("tbody tr").Should().HaveCount(3);
        cut.FindAll("tbody tr th.tm-breakdown-card__label").Select(cell => cell.TextContent.Trim())
            .Should().Equal(new[] { "Search", "Direct", "Social" }, "the rows re-sort without a key collision");
    }

    [Fact]
    public void Rows_WithADuplicateId_RenderAndReSortWithoutColliding()
    {
        var cut = Render<TmBreakdownCard>(p => p.Add(c => c.Parts,
            [Part("a", "Alpha", 40), Part("a", "Beta", 60)]));

        cut.FindAll("tbody tr").Should().HaveCount(2,
            "two parts sharing an Id would share a @key and throw 'More than one sibling … has the same key'");

        // The reorder is the second place a repeated id can bite: the FLIP lookup is keyed by the row
        // key, so a duplicate would keep only one index and compute a wrong shift.
        cut.Render(p => p.Add(c => c.Parts, [Part("a", "Alpha", 70), Part("a", "Beta", 60)]));

        var rows = cut.FindAll("tbody tr");
        rows.Should().HaveCount(2);
        rows[0].TextContent.Should().Contain("Alpha", "Alpha is the biggest part now");
        rows.Select(row => row.GetAttribute("data-part-id")).Should().OnlyContain(id => id == "a");
    }

    [Fact]
    public void Rows_WithDuplicateIds_SurviveAReorderThatAlsoAddsAPart()
    {
        var cut = Render<TmBreakdownCard>(p => p.Add(c => c.Parts,
            [Part("a", "Alpha", 40), Part("a", "Beta", 60)]));

        cut.Render(p => p.Add(c => c.Parts,
            [Part("a", "Alpha", 10), Part("a", "Beta", 30), Part("c", "Gamma", 20)]));

        cut.FindAll("tbody tr").Select(row => row.TextContent.Trim())
            .Should().HaveCount(3, "the duplicate stays renderable while the population changes");
    }

    // ── the table cell stays a table cell (flex rides an inner wrapper) ─────────────────────

    [Fact]
    public void RowHeader_KeepsTheTableCellAndMovesTheFlexLayoutToAnInnerWrapper()
    {
        var cut = Render<TmBreakdownCard>(p => p
            .Add(c => c.Parts, SampleParts())
            .Add(c => c.Total, 200));

        var headers = cut.FindAll("tbody th.tm-breakdown-card__label");
        headers.Should().HaveCount(4, "three parts plus the explicit remainder row of a declared total");

        foreach (var header in headers)
        {
            header.GetAttribute("scope").Should().Be("row");
            var inner = header.QuerySelector(".tm-breakdown-card__label-inner");
            inner.Should().NotBeNull(
                "display:flex on a th stops it being a table cell — WebKit/VoiceOver loses the row "
                + "header role — so the flex layout belongs to an inner wrapper");
            inner!.TextContent.Trim().Should().Be(header.TextContent.Trim(),
                "the wrapper carries the whole row header text");
        }

        headers[0].QuerySelector(".tm-breakdown-card__label-inner")!.TextContent.Trim()
            .Should().Be("Direct");
    }

    [Fact]
    public void RowSwatches_AreHiddenFromAssistiveTechnology()
    {
        var cut = Render<TmBreakdownCard>(p => p
            .Add(c => c.Parts, SampleParts())
            .Add(c => c.Total, 200));

        var swatches = cut.FindAll("tbody .tm-breakdown-card__swatch");

        swatches.Should().HaveCount(4, "every row header (parts and the remainder) paints a swatch");
        swatches.Should().OnlyContain(swatch => swatch.GetAttribute("aria-hidden") == "true",
            "the swatch is empty decoration; the label text already names the row");
    }

    // ── stylesheet guards: the table semantics and the forced-colors fallback ───────────────

    [Fact]
    public void LabelCell_DeclaresNoDisplay_SoTheBrowserKeepsItsTableCellBox()
    {
        ThemeCss.TryProperty("_breakdown-card.css", ".tm-breakdown-card__label", "display")
            .Should().BeNull(
                "a display value other than table-cell wraps the th in an anonymous cell and drops "
                + "the row header role in WebKit/VoiceOver; the layout lives on __label-inner");

        ThemeCss.Property("_breakdown-card.css", ".tm-breakdown-card__label-inner", "display")
            .Should().Be("flex");
    }

    [Fact]
    public void ForcedColors_KeepsTheStripAndTheSwatchesVisible()
    {
        var css = ThemeCss.ComponentCss("_breakdown-card.css");

        var block = Regex.Match(
            css,
            @"@media\s*\(forced-colors:\s*active\)\s*\{(?<body>(?:[^{}]|\{[^{}]*\})*)\}",
            RegexOptions.Singleline);

        block.Success.Should().BeTrue(
            "the segments and swatches are painted only with background-color, which forced-colors "
            + "removes — without a fallback the strip becomes an empty box and the swatches vanish");
        var body = ThemeCss.Normalise(block.Groups["body"].Value);
        body.Should().Contain("tm-breakdown-card__bar");
        body.Should().Contain("tm-breakdown-card__segment");
        body.Should().Contain("tm-breakdown-card__swatch");
        body.Should().Contain("forced-color-adjust: none");
        body.Should().Contain("CanvasText", "the bar keeps an edge in the system colour");
    }

    // ── the largest-remainder split and the FLIP shifts, unit-tested directly ────────────────

    [Fact]
    public void LargestRemainder_DistributesTheLeftoverPointsToTheLargestFractions()
    {
        var cut = Render<TmBreakdownCard>(p => p
            .Add(c => c.Parts, [Part("a", "A", 1), Part("b", "B", 1), Part("c", "C", 1)])
            .Add(c => c.Culture, English));

        cut.FindAll(".tm-breakdown-card__share").Select(share => share.TextContent)
            .Should().Equal(new[] { "34%", "33%", "33%" },
                "equal parts split the leftover point by position so the result stays deterministic");
    }

    [Fact]
    public void Reorder_AnnotatesEveryMovedRowForTheFlip_OnlyWhenTheOrderChanges()
    {
        var cut = Render<TmBreakdownCard>(p => p
            .Add(c => c.Parts, [Part("a", "A", 10), Part("b", "B", 20)]));

        cut.FindAll(".tm-breakdown-card__row--flip").Should().BeEmpty(
            "the first paint is not a reorder");

        cut.Render(p => p
            .Add(c => c.Parts, [Part("a", "A", 30), Part("b", "B", 20)]));

        var rows = cut.FindAll(".tm-breakdown-card__row");
        rows[0].TextContent.Should().Contain("A", "the bigger value now sorts first");
        rows.Should().HaveCount(2);
        cut.FindAll(".tm-breakdown-card__row--flip").Should().HaveCount(2,
            "both rows moved, so both carry the one-shot animation class");
        rows[0].GetAttribute("style").Should().Contain("--tm-breakdown-card-row-shift:100%",
            "a row that moved up one place starts one row BELOW its new position");
        rows[1].GetAttribute("style").Should().Contain("--tm-breakdown-card-row-shift:-100%");
    }

    [Fact]
    public void Reorder_WithoutAnOrderChange_DoesNotStartAFlip()
    {
        var cut = Render<TmBreakdownCard>(p => p
            .Add(c => c.Parts, [Part("a", "A", 10), Part("b", "B", 20)]));

        cut.Render(p => p
            .Add(c => c.Parts, [Part("a", "A", 15), Part("b", "B", 20)]));

        cut.FindAll(".tm-breakdown-card__row--flip").Should().BeEmpty(
            "a value change that keeps the order must not animate anything");
        cut.FindAll(".tm-breakdown-card__row[style]").Should().BeEmpty();
    }

    [Fact]
    public void Reorder_RestartsTheAnimationOnASecondReorder()
    {
        var cut = Render<TmBreakdownCard>(p => p
            .Add(c => c.Parts, [Part("a", "A", 10), Part("b", "B", 20)]));

        cut.Render(p => p.Add(c => c.Parts, [Part("a", "A", 30), Part("b", "B", 20)]));
        var firstShift = cut.FindAll(".tm-breakdown-card__row")[0].GetAttribute("style");

        cut.Render(p => p.Add(c => c.Parts, [Part("a", "A", 5), Part("b", "B", 20)]));
        var secondRows = cut.FindAll(".tm-breakdown-card__row");

        firstShift.Should().Contain("--tm-breakdown-card-row-shift:100%");
        secondRows[0].TextContent.Should().Contain("B", "B is the biggest part again");
        secondRows[0].GetAttribute("style").Should().Contain("--tm-breakdown-card-row-shift:100%",
            "the second reorder computes a new shift, so the animation runs again");
        secondRows[1].GetAttribute("style").Should().Contain("--tm-breakdown-card-row-shift:-100%");
        cut.FindAll(".tm-breakdown-card__row--flip").Should().HaveCount(2);
    }

    [Fact]
    public void Reorder_WithAnimateReorderOff_NeverAddsTheFlipClasses()
    {
        var cut = Render<TmBreakdownCard>(p => p
            .Add(c => c.Parts, [Part("a", "A", 10), Part("b", "B", 20)])
            .Add(c => c.AnimateReorder, false));

        cut.Render(p => p
            .Add(c => c.Parts, [Part("a", "A", 30), Part("b", "B", 20)]));

        cut.FindAll(".tm-breakdown-card__row--flip").Should().BeEmpty();
        cut.FindAll(".tm-breakdown-card__row[style]").Should().BeEmpty();
    }

    [Fact]
    public void FlipShifts_AreComputedFromThePreviousAndCurrentPositions()
    {
        var shifts = BreakdownFlip.Shifts(["a", "b", "c"], ["c", "a", "b"]);

        shifts["a"].Should().Be(-1, "a moved up by one row, so it starts one row above its new place");
        shifts["b"].Should().Be(-1);
        shifts["c"].Should().Be(2, "c came from the bottom to the top, so it starts two rows below");
    }

    [Fact]
    public void FlipShifts_IgnoreRowsThatAppearOrDisappear()
    {
        var shifts = BreakdownFlip.Shifts(["a", "b"], ["b", "c", "a"]);

        shifts.Should().NotContainKey("c", "a row with no previous position has nothing to animate from");
        shifts["b"].Should().Be(1, "b moved up one row, so its first frame starts one row below");
        shifts["a"].Should().Be(-2);
    }
}
