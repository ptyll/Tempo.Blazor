using System.Globalization;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Microsoft.JSInterop;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.Abstractions.Models;
using Tempo.Blazor.Components.Scheduler;
using Tempo.Blazor.Localization;
using Tempo.Blazor.Tests.Localization;
using Xunit;

namespace Tempo.Blazor.Tests.Components.Scheduler;

/// <summary>
/// TmGantt redesign, phase 11 (the shared portfolio axis): opt-in <c>AxisMode.Shared</c> computes the union
/// range of all projects, renders ONE sticky axis header above the cards and hands every card the same
/// range, zoom and tree width, so the same date lands at the same x in every card and the cards scroll in
/// step (<c>tmGantt.linkScrollGroup</c> in the browser). <c>PerProject</c> (default) is today's behaviour.
/// Culture and clock are pinned; bUnit runs no container queries, so layouts are forced.
/// </summary>
public class TmGanttPortfolioAxisTests : LocalizationTestBase
{
    public TmGanttPortfolioAxisTests()
    {
        Services.AddSingleton<TimeProvider>(new FakeTimeProvider(new DateTimeOffset(2024, 6, 12, 10, 0, 0, TimeSpan.Zero)));
        Services.AddSingleton<ITmUserTimeZone>(new StubTmUserTimeZone(TimeZoneInfo.Utc));
        JSInterop.Setup<bool>("tmGantt.attachTimeline", _ => true).SetResult(true);
        JSInterop.Setup<bool>("tmGantt.attachTree", _ => true).SetResult(true);
        JSInterop.Setup<bool>("tmGantt.linkScrollGroup", _ => true).SetResult(true);
    }

    private static List<GanttPortfolioProject> Projects() =>
    [
        new()
        {
            Id = "p1", Name = "Alpha",
            Tasks =
            [
                new() { Id = "a1", Title = "Early", Start = new DateTime(2024, 4, 1), End = new DateTime(2024, 4, 11), PercentComplete = 100, Status = TmWorkItemStatus.Done },
                new() { Id = "a2", Title = "Spring", Start = new DateTime(2024, 4, 11), End = new DateTime(2024, 5, 31), PercentComplete = 0 },
            ],
        },
        new()
        {
            Id = "p2", Name = "Beta",
            Tasks =
            [
                new() { Id = "b1", Title = "June", Start = new DateTime(2024, 6, 1), End = new DateTime(2024, 6, 5), PercentComplete = 40 },
            ],
        },
    ];

    private IRenderedComponent<TmGanttPortfolio> RenderPortfolio(
        Action<ComponentParameterCollectionBuilder<TmGanttPortfolio>>? configure = null,
        List<GanttPortfolioProject>? projects = null) =>
        Render<TmGanttPortfolio>(p =>
        {
            p.Add(c => c.Projects, projects ?? Projects());
            configure?.Invoke(p);
        });

    // ── PerProject is the default and unchanged ────────────────────────────────────────────────

    [Fact]
    public void Portfolio_AxisMode_PerProject_Is_Default_And_Unchanged()
    {
        var cut = RenderPortfolio();

        cut.Instance.AxisMode.Should().Be(GanttPortfolioAxisMode.PerProject);
        cut.FindAll("[data-testid='gantt-portfolio-axis']").Should().BeEmpty("no shared header in the default mode");
        cut.FindAll(".tm-gantt__timeline-header").Should().HaveCount(2, "every card keeps its own header rows");
        cut.Instance.TreeWidth.Should().BeNull("the portfolio pins no tree width by default");
    }

    // ── The shared axis ────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Portfolio_Shared_Axis_Uses_Union_Range_Of_All_Projects()
    {
        var cut = RenderPortfolio(p =>
        {
            p.Add(c => c.AxisMode, GanttPortfolioAxisMode.Shared);
            p.Add(c => c.ZoomPreset, GanttZoomPreset.Months);
        });

        cut.FindAll("[data-testid='gantt-portfolio-axis']").Should().HaveCount(1);
        var nested = cut.FindComponents<TmGantt>().Select(g => g.Instance).ToList();
        nested.Should().HaveCount(2);

        // The union of the two projects' ranges: April 1 – June 5. Both cards lay out against it,
        // so both timeline widths are identical and "June 1" lands left of the right edge in card 2.
        nested.Select(g => g._totalTimelineWidth).Should().OnlyContain(w => w > 0);
        nested.Select(g => g._totalTimelineWidth).Distinct().Should().HaveCount(1);

        // Card 2's only bar starts on the union range's 61st of 65 days (~94 %): its left offset must
        // sit near the right edge, not at 0 as the card's own range (June 1–5) would place it.
        var bar = cut.Find("[data-testid$='task-bar-b1']");
        var left = decimal.Parse(ExtractStyleValue(bar.GetAttribute("style")!, "left"), CultureInfo.InvariantCulture);
        left.Should().BeGreaterThan(80m, "the bar is placed against the union range, not the project's own");
    }

    [Fact]
    public void Portfolio_Shared_Axis_Renders_One_Sticky_Header_And_Hides_Card_Headers()
    {
        var cut = RenderPortfolio(p => p.Add(c => c.AxisMode, GanttPortfolioAxisMode.Shared));

        cut.FindAll(".tm-gantt-portfolio__axis").Should().HaveCount(1, "exactly one axis header above the cards");
        cut.Find(".tm-gantt-portfolio__axis").ClassList.Should().Contain("tm-gantt-portfolio__axis--sticky");
        cut.FindAll(".tm-gantt__timeline-header:not(.tm-gantt__timeline-header--carrier)")
            .Should().BeEmpty("the cards render ShowTimelineHeader=false (only the invisible scroll carrier remains)");
        cut.FindAll(".tm-gantt__grid").Should().NotBeEmpty("the grid lines stay — only the header rows hide");
    }

    [Fact]
    public void Portfolio_Shared_Axis_Passes_Same_Zoom_And_TreeWidth_To_Every_Card()
    {
        var cut = RenderPortfolio(p =>
        {
            p.Add(c => c.AxisMode, GanttPortfolioAxisMode.Shared);
            p.Add(c => c.ZoomPreset, GanttZoomPreset.Months);
            p.Add(c => c.TreeWidth, "200px");
        });

        var nested = cut.FindComponents<TmGantt>().Select(g => g.Instance).ToList();
        nested.Select(g => g.GetEffectiveZoomPreset()).Should().OnlyContain(z => z == GanttZoomPreset.Months);
        cut.FindAll(".tm-gantt__tree-grid")
            .Select(grid => grid.GetAttribute("style"))
            .Should().OnlyContain(style => style!.Contains("width: 200px"), "the portfolio pins one tree width");
    }

    [Fact]
    public void Portfolio_Shared_Axis_Mobile_MiniGantt_Uses_Shared_Range()
    {
        var cut = RenderPortfolio(p =>
        {
            p.Add(c => c.AxisMode, GanttPortfolioAxisMode.Shared);
            p.Add(c => c.LayoutMode, TmLayoutMode.Mobile);
        });

        // One shared monthly band above the cards; the per-card month rows are gone.
        cut.FindAll(".tm-gantt-portfolio__axis").Should().HaveCount(1);
        cut.FindAll(".tm-gantt-portfolio__mini-months").Should().BeEmpty();

        // Project 2's only bar spans June 1–5 inside the union April–June: a narrow band near the right
        // edge, not the full width the project's own range would give it.
        var bar = cut.Find("[data-testid='portfolio-project-p2'] .tm-gantt-portfolio__mini-bar");
        var width = decimal.Parse(ExtractStyleValue(bar.GetAttribute("style")!, "width"), CultureInfo.InvariantCulture);
        width.Should().BeLessThan(15m, "the mini bar is a share of the union range");
    }

    [Fact]
    public void Portfolio_Shared_Axis_Today_Marker_Aligned()
    {
        var cut = RenderPortfolio(p => p.Add(c => c.AxisMode, GanttPortfolioAxisMode.Shared));

        var offsets = cut.FindAll("[data-testid$='today-marker']")
            .Select(marker => ExtractStyleValue(marker.GetAttribute("style")!, "left"))
            .ToList();
        offsets.Should().HaveCount(2).And.OnlyContain(o => o == offsets[0], "the same date draws at the same x in every card");
    }

    [Fact]
    public void Portfolio_Collapsed_Card_Does_Not_Break_Alignment()
    {
        var cut = RenderPortfolio(p => p.Add(c => c.AxisMode, GanttPortfolioAxisMode.Shared));

        var barBefore = cut.Find("[data-testid$='task-bar-b1']").GetAttribute("style");
        var axisBefore = cut.Find(".tm-gantt-portfolio__axis").TextContent;

        cut.Find("[data-testid='portfolio-toggle-p1']").Click();

        cut.FindAll("[data-testid='gantt-portfolio-axis']").Should().HaveCount(1, "the shared axis survives a collapsed card");
        cut.Find(".tm-gantt-portfolio__axis").TextContent.Should().Be(axisBefore, "the union range never shrinks with a collapsed card");
        cut.Find("[data-testid$='task-bar-b1']").GetAttribute("style").Should().Be(barBefore,
            "the remaining card is not re-laid out: collapsing another project must not shift its bars");
    }

    // ── The TmGantt overrides the portfolio (and any host) drives it with ─────────────────────

    [Fact]
    public void TmGantt_TimelineStart_End_Override_GetTimeRange()
    {
        // The data spans June 1–30; the host pins a wider axis: March 1 – September 30.
        var cut = Render<TmGantt>(p =>
        {
            p.Add(c => c.Items, new List<TmWorkItem>
            {
                new() { Id = "1", Title = "Inside", Start = new DateTime(2024, 6, 1), End = new DateTime(2024, 6, 30) },
                new() { Id = "2", Title = "Before", Start = new DateTime(2024, 1, 1), End = new DateTime(2024, 2, 15) },
            });
            p.Add(c => c.TimelineStart, new DateTime(2024, 3, 1));
            p.Add(c => c.TimelineEnd, new DateTime(2024, 9, 30));
        });

        var instance = cut.Instance;
        // 213 days at the Weeks preset's 24 px/day (zoom 100).
        instance._totalTimelineWidth.Should().BeApproximately(
            (new DateTime(2024, 9, 30) - new DateTime(2024, 3, 1)).TotalDays * instance.GetPixelPerDay(), 0.5,
            "the override replaces GanttHelper.GetTimeRange(Data)");

        // "Before" ends 2024-02-15, before the axis: clipped away entirely (no ghost element, no sliver).
        var bars = cut.FindAll("[data-testid^='task-bar-']");
        bars.Should().HaveCount(1, "the out-of-range bar renders nothing");
        var inside = bars.Single();
        inside.GetAttribute("data-testid")!.Should().EndWith("task-bar-1");
        var left = double.Parse(ExtractStyleValue(inside.GetAttribute("style")!, "left"), CultureInfo.InvariantCulture);
        var width = double.Parse(ExtractStyleValue(inside.GetAttribute("style")!, "width"), CultureInfo.InvariantCulture);
        (left + width).Should().BeLessThanOrEqualTo(instance._totalTimelineWidth);
    }

    [Fact]
    public async Task TmGantt_TimelineStart_End_Override_FitToScreen_Respects_It()
    {
        var cut = Render<TmGantt>(p =>
        {
            p.Add(c => c.Items, new List<TmWorkItem>
            {
                new() { Id = "1", Title = "One", Start = new DateTime(2024, 6, 1), End = new DateTime(2024, 6, 30) },
            });
            p.Add(c => c.TimelineStart, new DateTime(2024, 1, 1));
            p.Add(c => c.TimelineEnd, new DateTime(2024, 12, 31));
        });

        var zoomBefore = cut.Instance._zoomLevel;
        await cut.InvokeAsync(() => cut.Instance.ApplyFitToScreenAsync(400));

        cut.Instance._zoomLevel.Should().BeLessThan(zoomBefore,
            "Fit to screen measures the overridden axis, which is wider than the data's own range");
    }

    [Fact]
    public void TmGantt_ShowTimelineHeader_False_Hides_Header_Rows()
    {
        var shown = Render<TmGantt>(p => p.Add(c => c.Items,
            new List<TmWorkItem> { new() { Id = "1", Title = "One", Start = DateTime.Today, End = DateTime.Today.AddDays(3) } }));
        shown.FindAll(".tm-gantt__timeline-header").Should().HaveCount(1, "the header rows render by default");

        var hidden = Render<TmGantt>(p =>
        {
            p.Add(c => c.Items, new List<TmWorkItem> { new() { Id = "1", Title = "One", Start = DateTime.Today, End = DateTime.Today.AddDays(3) } });
            p.Add(c => c.ShowTimelineHeader, false);
        });
        hidden.FindAll(".tm-gantt__timeline-header:not(.tm-gantt__timeline-header--carrier)").Should().BeEmpty();
        hidden.FindAll(".tm-gantt__grid").Should().NotBeEmpty("only the header rows hide");
    }

    [Fact]
    public void TmGantt_ShowTimelineHeader_False_VisuallyHides_The_Tree_Header_And_Keeps_Its_Roles()
    {
        // The collapsed timeline header is zero-height: the tree header must leave the flow too, or the
        // first tree row starts lower than the first bar. It stays in the DOM for assistive tech.
        var hidden = Render<TmGantt>(p =>
        {
            p.Add(c => c.Items, new List<TmWorkItem> { new() { Id = "1", Title = "One", Start = DateTime.Today, End = DateTime.Today.AddDays(3) } });
            p.Add(c => c.ShowTimelineHeader, false);
        });

        var header = hidden.Find(".tm-gantt__tree-header");
        header.ClassList.Should().Contain("tm-gantt__tree-header--vh", "the header is clipped out of sight");
        header.GetAttribute("role").Should().Be("row", "the treegrid keeps its header row for screen readers");
        header.QuerySelectorAll("[role='columnheader']").Should().NotBeEmpty("the column headers stay named");

        var shown = Render<TmGantt>(p => p.Add(c => c.Items,
            new List<TmWorkItem> { new() { Id = "1", Title = "One", Start = DateTime.Today, End = DateTime.Today.AddDays(3) } }));
        shown.Find(".tm-gantt__tree-header").ClassList.Should().NotContain("tm-gantt__tree-header--vh", "the default DOM is unchanged");
    }

    // ── The card scale lock (the shared axis pins the zoom) ────────────────────────────────────

    [Fact]
    public void Portfolio_Shared_Axis_Pins_The_Desktop_Layout_On_Every_Card()
    {
        var cut = RenderPortfolio(p => p.Add(c => c.AxisMode, GanttPortfolioAxisMode.Shared));

        cut.FindComponents<TmGantt>().Select(g => g.Instance.LayoutMode).Should().OnlyContain(
            mode => mode == TmLayoutMode.Desktop,
            "a card that classified as mobile would swap its zoom to Days and break the shared alignment");
    }

    [Fact]
    public void Portfolio_PerProject_Keeps_The_Auto_Layout_On_The_Cards()
    {
        var cut = RenderPortfolio();

        cut.FindComponents<TmGantt>().Select(g => g.Instance.LayoutMode).Should().OnlyContain(
            mode => mode == TmLayoutMode.Auto, "the default mode is unchanged");
    }

    [Fact]
    public async Task TmGantt_Pinned_Axis_Locks_The_Zoom_Preset_And_Scale()
    {
        var cut = Render<TmGantt>(p =>
        {
            p.Add(c => c.Items, new List<TmWorkItem> { new() { Id = "1", Title = "One", Start = new DateTime(2024, 6, 1), End = new DateTime(2024, 6, 30) } });
            p.Add(c => c.TimelineStart, new DateTime(2024, 3, 1));
            p.Add(c => c.TimelineEnd, new DateTime(2024, 9, 30));
            p.Add(c => c.ZoomPreset, GanttZoomPreset.Months);
        });

        // The header above the cards draws at the portfolio's preset: the pinned card follows it, not a
        // per-session override.
        cut.Instance.GetEffectiveZoomPreset().Should().Be(GanttZoomPreset.Months);
        await cut.InvokeAsync(() => cut.Instance.SetViewAsync(GanttView.Day));
        cut.Instance.GetEffectiveZoomPreset().Should().Be(GanttZoomPreset.Months,
            "the Day|Week|Month override cannot move a pinned axis");

        var pixelsPerDay = cut.Instance.GetPixelPerDay();
        await cut.InvokeAsync(() => cut.Instance.ZoomInAsync());
        cut.Instance.GetPixelPerDay().Should().Be(pixelsPerDay,
            "the shared header draws at zoom 100 — the pinned card must lay out with the same pixels per day");
    }

    [Fact]
    public async Task TmGantt_Pinned_Axis_Ignores_Ctrl_Wheel_Zoom()
    {
        var pinned = Render<TmGantt>(p =>
        {
            p.Add(c => c.Items, new List<TmWorkItem> { new() { Id = "1", Title = "One", Start = new DateTime(2024, 6, 1), End = new DateTime(2024, 6, 30) } });
            p.Add(c => c.TimelineStart, new DateTime(2024, 3, 1));
            p.Add(c => c.TimelineEnd, new DateTime(2024, 9, 30));
        });
        var zoomBefore = pinned.Instance._zoomLevel;
        await pinned.InvokeAsync(() => pinned.Instance.OnTimelineWheel(new WheelEventArgs { CtrlKey = true, DeltaY = -100 }));
        pinned.Instance._zoomLevel.Should().Be(zoomBefore, "Ctrl+wheel zoom would desync the card from the shared header");

        var free = Render<TmGantt>(p => p.Add(c => c.Items,
            new List<TmWorkItem> { new() { Id = "1", Title = "One", Start = new DateTime(2024, 6, 1), End = new DateTime(2024, 6, 30) } }));
        var freeZoomBefore = free.Instance._zoomLevel;
        await free.InvokeAsync(() => free.Instance.OnTimelineWheel(new WheelEventArgs { CtrlKey = true, DeltaY = -100 }));
        free.Instance._zoomLevel.Should().Be(freeZoomBefore + 25, "an unpinned chart keeps the Ctrl+wheel zoom");
    }

    // ── Dependencies and baselines under a pinned axis ─────────────────────────────────────────

    [Fact]
    public void TmGantt_Pinned_Axis_Dependency_With_A_Clippled_Endpoint_Renders_No_Line()
    {
        var cut = Render<TmGantt>(p =>
        {
            p.Add(c => c.Items, new List<TmWorkItem>
            {
                new() { Id = "1", Title = "Inside", Start = new DateTime(2024, 6, 1), End = new DateTime(2024, 6, 30) },
                new() { Id = "2", Title = "Before", Start = new DateTime(2024, 1, 1), End = new DateTime(2024, 2, 15) },
            });
            p.Add(c => c.DependencyItems, new List<GanttDependency>
            {
                new() { Id = "d1", FromId = "2", ToId = "1" },
            });
            p.Add(c => c.TimelineStart, new DateTime(2024, 3, 1));
            p.Add(c => c.TimelineEnd, new DateTime(2024, 9, 30));
        });

        cut.FindAll("[data-testid$='dep-line-d1']").Should().BeEmpty(
            "a fully clipped bar has no anchor — the line would start at the axis edge");
    }

    [Fact]
    public void TmGantt_Pinned_Axis_Clips_Baselines_Like_The_Bars()
    {
        var cut = Render<TmGantt>(p =>
        {
            p.Add(c => c.Items, new List<TmWorkItem>
            {
                new() { Id = "1", Title = "Inside", Start = new DateTime(2024, 6, 5), End = new DateTime(2024, 6, 15) },
                new() { Id = "2", Title = "Before", Start = new DateTime(2024, 1, 1), End = new DateTime(2024, 2, 15) },
            });
            p.Add(c => c.Baselines, new List<GanttBaseline>
            {
                new()
                {
                    Id = "b1",
                    Tasks =
                    [
                        new GanttBaselineTask("1", new DateTime(2024, 5, 20), new DateTime(2024, 6, 10)),
                        new GanttBaselineTask("2", new DateTime(2024, 1, 1), new DateTime(2024, 2, 15)),
                    ],
                },
            });
            p.Add(c => c.ActiveBaselineId, "b1");
            p.Add(c => c.TimelineStart, new DateTime(2024, 6, 1));
            p.Add(c => c.TimelineEnd, new DateTime(2024, 6, 30));
        });

        var ghosts = cut.FindAll("[data-testid$='ghost-bar-2']");
        ghosts.Should().BeEmpty("a baseline fully outside the pinned axis renders nothing");

        var inside = cut.Find("[data-testid$='ghost-bar-1']");
        var left = double.Parse(ExtractStyleValue(inside.GetAttribute("style")!, "left"), CultureInfo.InvariantCulture);
        left.Should().Be(0, "the baseline starts before the axis and clips to its edge, like the bars do");
    }

    // ── The shared header's leading cell + ARIA ────────────────────────────────────────────────

    [Fact]
    public void Portfolio_Shared_Axis_Header_Has_A_Leading_Cell_As_Wide_As_The_Tree()
    {
        var cut = RenderPortfolio(p =>
        {
            p.Add(c => c.AxisMode, GanttPortfolioAxisMode.Shared);
            p.Add(c => c.TreeWidth, "200px");
        });

        var axis = cut.Find(".tm-gantt-portfolio__axis");
        axis.GetAttribute("style").Should().Contain("--tm-gantt-portfolio-tree-width: 200px",
            "the leading cell and the cards' tree share one width so the axis cells start where the timelines start");
        var lead = cut.Find(".tm-gantt-portfolio__axis-lead");
        lead.TextContent.Should().Contain("Shared timeline", "the leading cell carries the localized label");
        cut.Find(".tm-gantt-portfolio__axis-scroll").Should().NotBeNull("the band itself stays the scrollable member");
    }

    [Fact]
    public void Portfolio_Shared_Axis_Aria_Lives_On_The_Scroll_Region()
    {
        var cut = RenderPortfolio(p => p.Add(c => c.AxisMode, GanttPortfolioAxisMode.Shared));

        var wrapper = cut.Find("[data-testid='gantt-portfolio-axis']");
        wrapper.GetAttribute("role").Should().BeNull("the wrapper is layout, not a landmark");
        wrapper.GetAttribute("aria-label").Should().BeNull();

        var region = cut.Find(".tm-gantt-portfolio__axis-scroll");
        region.GetAttribute("role").Should().Be("region");
        region.GetAttribute("aria-label").Should().Be("Shared timeline");
        region.GetAttribute("tabindex").Should().Be("0", "the scroll region stays keyboard-reachable");
    }

    [Fact]
    public void Portfolio_Shared_Axis_Mobile_Band_Is_Aria_Hidden()
    {
        var cut = RenderPortfolio(p =>
        {
            p.Add(c => c.AxisMode, GanttPortfolioAxisMode.Shared);
            p.Add(c => c.LayoutMode, TmLayoutMode.Mobile);
        });

        var wrapper = cut.Find("[data-testid='gantt-portfolio-axis']");
        wrapper.GetAttribute("aria-hidden").Should().Be("true", "the decorative monthly band is hidden from assistive tech");
        wrapper.GetAttribute("role").Should().BeNull();
    }

    // ── The axis is computed once per parameter change, never in render ────────────────────────

    [Fact]
    public void Portfolio_Shared_Axis_Computes_The_Axis_Once_Per_Parameter_Change()
    {
        var cut = RenderPortfolio(p =>
        {
            p.Add(c => c.AxisMode, GanttPortfolioAxisMode.Shared);
            p.Add(c => c.ZoomPreset, GanttZoomPreset.Months);
        });

        cut.Instance._unionRange.Should().NotBeNull();
        cut.Instance._axisTotalWidth.Should().BeGreaterThan(0);
        cut.Instance._axisRows.Upper.Should().NotBeEmpty();

        var rowsBefore = cut.Instance._axisRows.Upper;
        cut.Render();

        cut.Instance._axisRows.Upper.Should().BeSameAs(rowsBefore,
            "a re-render without a parameter change must not rebuild the header rows");
    }

    private static string ExtractStyleValue(string style, string property)
    {
        var match = System.Text.RegularExpressions.Regex.Match(
            style, $@"(?:^|;)\s*{System.Text.RegularExpressions.Regex.Escape(property)}\s*:\s*([^;]+)");
        return match.Success ? match.Groups[1].Value.Trim().TrimEnd('p', 'x', '%') : string.Empty;
    }
}
