using System.Globalization;
using Bunit;
using FluentAssertions;
using Tempo.Blazor.Components.DataDisplay;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.DataDisplay;

/// <summary>
/// Plan ff043b2e phase 2: <c>TmTrendList</c> is the compact "metric + change" list — one row per
/// <see cref="TrendItem"/>, rendered as a semantic list (default) or a table with real headers, with
/// a culture-formatted delta whose direction travels as an arrow, a word and a tone (never colour
/// alone), an optional per-row sparkline, an empty state and a loading state.
/// </summary>
public class TmTrendListTests : LocalizationTestBase
{
    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en-US");
    private static readonly CultureInfo Czech = CultureInfo.GetCultureInfo("cs-CZ");

    private static TrendItem Item(
        string id,
        string label,
        string? value = null,
        double? numeric = null,
        double? delta = null,
        bool positiveIsGood = true,
        double[]? sparkline = null,
        string? href = null) => new()
        {
            Id = id,
            Label = label,
            Value = value,
            NumericValue = numeric,
            Delta = delta,
            PositiveIsGood = positiveIsGood,
            Sparkline = sparkline,
            Href = href
        };

    private static IReadOnlyList<TrendItem> SampleItems() =>
    [
        Item("revenue", "Revenue", numeric: 12450, delta: 12.5),
        Item("churn", "Churn", value: "2.1 %", delta: -3, positiveIsGood: false),
        Item("tickets", "Open tickets", value: "42", delta: 0)
    ];

    [Fact]
    public void Root_RendersTheBaseClassAndTheDefaultTestId()
    {
        var cut = Render<TmTrendList>(p => p.Add(c => c.Items, SampleItems()));

        cut.Find(".tm-trend-list").Should().NotBeNull();
        cut.Find("[data-testid='trend-list']").Should().NotBeNull();
    }

    [Fact]
    public void DefaultLayout_IsASemanticList_WithOneItemPerRow()
    {
        var cut = Render<TmTrendList>(p => p.Add(c => c.Items, SampleItems()));

        var list = cut.Find("ul.tm-trend-list__items");
        list.Should().NotBeNull("a trend list is a list of metrics, not a pile of divs");
        cut.FindAll("ul.tm-trend-list__items > li.tm-trend-list__item").Should().HaveCount(3);
        cut.FindAll("table").Should().BeEmpty("the default layout is the compact list");
    }

    [Fact]
    public void Row_RendersLabelAndValue()
    {
        var cut = Render<TmTrendList>(p => p
            .Add(c => c.Items, SampleItems())
            .Add(c => c.Culture, English));

        var rows = cut.FindAll(".tm-trend-list__item");
        rows[0].QuerySelector(".tm-trend-list__label")!.TextContent.Trim().Should().Be("Revenue");
        rows[0].QuerySelector(".tm-trend-list__value")!.TextContent.Trim().Should().Be("12,450");
        rows[1].QuerySelector(".tm-trend-list__value")!.TextContent.Trim().Should().Be("2.1 %",
            "a string Value is host-formatted and rendered verbatim");
    }

    [Fact]
    public void NumericValue_WinsOverTheStringValue()
    {
        var cut = Render<TmTrendList>(p => p
            .Add(c => c.Items, [Item("a", "A", value: "ignored", numeric: 1234.5)])
            .Add(c => c.ValueFormat, "N1")
            .Add(c => c.Culture, English));

        cut.Find(".tm-trend-list__value").TextContent.Trim().Should().Be("1,234.5");
    }

    [Fact]
    public void EmptyState_RendersTheLocalizedMessage_AndNoList()
    {
        var cut = Render<TmTrendList>();

        cut.Find(".tm-trend-list__empty").TextContent.Trim().Should().Be("No data to show");
        cut.FindAll("ul").Should().BeEmpty();
        cut.FindAll("table").Should().BeEmpty();
    }

    [Fact]
    public void LoadingState_IsBusyAndSkeletonised_AndRendersNoRows()
    {
        var cut = Render<TmTrendList>(p => p
            .Add(c => c.Items, SampleItems())
            .Add(c => c.IsLoading, true));

        var root = cut.Find(".tm-trend-list");
        root.GetAttribute("aria-busy").Should().Be("true");
        cut.FindAll(".tm-skeleton").Should().NotBeEmpty("the placeholder has to show the shape of the data");
        cut.FindAll(".tm-trend-list__item").Should().BeEmpty("no stale row stays on screen");
        root.TextContent.Should().Contain("Loading values", "the state is named for a reader, not only painted");
    }

    [Theory]
    [InlineData(12.5, "Increase")]
    [InlineData(-12.5, "Decrease")]
    [InlineData(0.0, "No change")]
    public void Delta_CarriesAVisuallyHiddenDirectionWord(double delta, string expected)
    {
        var cut = Render<TmTrendList>(p => p
            .Add(c => c.Items, [Item("a", "A", value: "1", delta: delta)])
            .Add(c => c.Culture, English));

        var hidden = cut.FindAll(".tm-trend-list__trend .tm-sr-only")
            .Should().ContainSingle("one hidden word per row — colour is never the only carrier").Subject;
        hidden.TextContent.Should().Be(expected);
    }

    [Fact]
    public void Delta_IsFormattedByTheCulture_AndDeltaFormatOverridesIt()
    {
        var czech = Render<TmTrendList>(p => p
            .Add(c => c.Items, [Item("a", "A", value: "1", delta: 12.5)])
            .Add(c => c.Culture, Czech));
        czech.Find(".tm-trend-list__delta").TextContent.Should().Be("+12,5\u00A0%");

        var custom = Render<TmTrendList>(p => p
            .Add(c => c.Items, [Item("a", "A", value: "1", delta: 1.5)])
            .Add(c => c.DeltaFormat, "+0.00")
            .Add(c => c.Culture, English));
        custom.Find(".tm-trend-list__delta").TextContent.Should().Be("+1.50");
    }

    [Fact]
    public void Trend_ToneIsTheOutcome_NotTheDirection()
    {
        var cut = Render<TmTrendList>(p => p
            .Add(c => c.Items,
            [
                Item("revenue", "Revenue", value: "1", delta: 12.5),
                Item("churn", "Churn", value: "1", delta: -3, positiveIsGood: false),
                Item("tickets", "Open tickets", value: "1", delta: 0)
            ])
            .Add(c => c.Culture, English));

        var trends = cut.FindAll(".tm-trend-list__trend");
        trends[0].ClassList.Should().Contain("tm-trend-list__trend--positive");
        trends[1].ClassList.Should().Contain("tm-trend-list__trend--positive",
            "a falling churn rate is good news, so the tone follows the outcome");
        trends[1].QuerySelector(".tm-sr-only")!.TextContent.Should().Be("Decrease",
            "the direction word still says Decrease — the tone never rewrites the fact");
        trends[2].ClassList.Should().Contain("tm-trend-list__trend--neutral");
    }

    [Fact]
    public void WithoutDelta_NoTrendMarkerRenders()
    {
        var cut = Render<TmTrendList>(p => p
            .Add(c => c.Items, [Item("a", "A", value: "1")]));

        cut.FindAll(".tm-trend-list__trend").Should().BeEmpty();
        cut.FindAll(".tm-trend-list__delta").Should().BeEmpty();
    }

    [Fact]
    public void Trend_UsesTheArrowIconAsASecondCue_AndKeepsItOutOfTheAccessibilityTree()
    {
        var cut = Render<TmTrendList>(p => p
            .Add(c => c.Items, [Item("a", "A", value: "1", delta: 5)]));

        var svg = cut.Find(".tm-trend-list__trend svg");
        svg.GetAttribute("aria-hidden").Should().Be("true");
    }

    [Fact]
    public void Sparkline_RendersOnlyForRowsThatProvideOne()
    {
        var cut = Render<TmTrendList>(p => p
            .Add(c => c.Items,
            [
                Item("with", "With", value: "1", sparkline: [1, 2, 3, 4]),
                Item("without", "Without", value: "2")
            ]));

        cut.FindAll(".tm-trend-list__sparkline").Should().ContainSingle();
        cut.Find(".tm-trend-list__sparkline .tm-sparkline").Should().NotBeNull();
    }

    [Fact]
    public void SafeHref_TurnsTheLabelIntoOneNativeLink()
    {
        var cut = Render<TmTrendList>(p => p
            .Add(c => c.Items, [Item("a", "A", value: "1", href: "reports/a")]));

        var link = cut.Find("a.tm-trend-list__link");
        link.GetAttribute("href").Should().Be("reports/a");
        link.TextContent.Trim().Should().Be("A");
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/html,<b>x</b>")]
    [InlineData("//elsewhere.example")]
    [InlineData(" java\tscript:alert(1)")]
    public void RefusedHref_RendersNoTargetAtAll(string href)
    {
        var cut = Render<TmTrendList>(p => p
            .Add(c => c.Items, [Item("a", "A", value: "1", href: href)]));

        cut.FindAll("a").Should().BeEmpty("a refused href means no target, never a rewritten one");
        cut.Find(".tm-trend-list__label").TextContent.Trim().Should().Be("A");
    }

    [Fact]
    public void TableLayout_RendersRealHeadersRowsAndACaption()
    {
        var cut = Render<TmTrendList>(p => p
            .Add(c => c.Items, SampleItems())
            .Add(c => c.Layout, TrendListLayout.Table)
            .Add(c => c.Culture, English));

        cut.Find(".tm-trend-list--table").Should().NotBeNull();
        var table = cut.Find("table.tm-trend-list__table");
        table.Should().NotBeNull();
        cut.FindAll("table.tm-trend-list__table caption.tm-sr-only").Should().ContainSingle(
            "a table needs an accessible name, and the caption is the one a reader gets");

        var headers = cut.FindAll("table.tm-trend-list__table thead th");
        headers.Select(th => th.TextContent.Trim()).Should()
            .Equal(new[] { "Item", "Value", "Change" }, "the headers are localized and real column headers");
        headers.Should().OnlyContain(th => th.GetAttribute("scope") == "col");

        cut.FindAll("table.tm-trend-list__table tbody tr").Should().HaveCount(3);
    }

    [Fact]
    public void TableLayout_HeaderParametersOverrideTheLocalizedDefaults()
    {
        var cut = Render<TmTrendList>(p => p
            .Add(c => c.Items, SampleItems())
            .Add(c => c.Layout, TrendListLayout.Table)
            .Add(c => c.LabelHeader, "Metric")
            .Add(c => c.ValueHeader, "Amount")
            .Add(c => c.ChangeHeader, "Delta"));

        cut.FindAll("table.tm-trend-list__table thead th").Select(th => th.TextContent.Trim()).Should()
            .Equal(new[] { "Metric", "Amount", "Delta" });
    }

    [Fact]
    public void Title_RendersAsAHeading_AndNamesTheList()
    {
        var cut = Render<TmTrendList>(p => p
            .Add(c => c.Items, SampleItems())
            .Add(c => c.Title, "Key metrics"));

        var title = cut.Find(".tm-trend-list__title");
        title.TextContent.Trim().Should().Be("Key metrics");
        var list = cut.Find("ul.tm-trend-list__items");
        list.GetAttribute("aria-labelledby").Should().Be(title.Id,
            "the heading names the list instead of an unexplained group");
        list.GetAttribute("aria-label").Should().BeNull();
    }

    [Fact]
    public void WithoutTitle_TheListCarriesALocalizedDefaultName()
    {
        var cut = Render<TmTrendList>(p => p
            .Add(c => c.Items, SampleItems())
            .Add(c => c.AriaLabel, "Revenue by region"));

        cut.Find("ul.tm-trend-list__items").GetAttribute("aria-label").Should().Be("Revenue by region");

        var unnamed = Render<TmTrendList>(p => p.Add(c => c.Items, SampleItems()));
        unnamed.Find("ul.tm-trend-list__items").GetAttribute("aria-label").Should().Be("Trend list");
    }

    [Fact]
    public void CzechResources_ResolveTheDirectionWordAndTheEmptyState()
    {
        UseCzechLocalization();

        var cut = Render<TmTrendList>(p => p
            .Add(c => c.Items, [Item("a", "A", value: "1", delta: 5)])
            .Add(c => c.Culture, Czech));

        cut.Find(".tm-trend-list__trend .tm-sr-only").TextContent.Should().Be("Nárůst");

        var empty = Render<TmTrendList>();
        empty.Find(".tm-trend-list__empty").TextContent.Trim().Should().Be("Žádná data k zobrazení");
    }

    [Fact]
    public void TestIdPrefix_NamespacesTheRows()
    {
        var cut = Render<TmTrendList>(p => p
            .Add(c => c.Items, SampleItems())
            .Add(c => c.TestIdPrefix, "kpi"));

        cut.FindAll("[data-testid='kpi-trend-item']").Should().HaveCount(3);
    }

    [Fact]
    public void RowsExposeTheHostIdAsDataAttribute_NotAsAnElementId()
    {
        var cut = Render<TmTrendList>(p => p
            .Add(c => c.Items, [Item("a.b", "A", value: "1")]));

        var row = cut.Find(".tm-trend-list__item");
        row.GetAttribute("data-item-id").Should().Be("a.b");
        row.Id.Should().BeNullOrEmpty("a host value must never become a DOM id — a slug is lossy");
    }

    [Fact]
    public void NoLiveRegionIsRenderedByDefault()
    {
        var cut = Render<TmTrendList>(p => p.Add(c => c.Items, SampleItems()));

        cut.FindAll("[aria-live]").Should().BeEmpty(
            "a list that re-renders on every poll must not become a chorus");
    }
}
