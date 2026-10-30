using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.Abstractions.Models;
using Tempo.Blazor.Components.Scheduler;
using Tempo.Blazor.Tests.Localization;
using Xunit;

namespace Tempo.Blazor.Tests.Components.Scheduler;

/// <summary>
/// Fáze 12 (performance): the window virtualization of the tree and the timeline. Default
/// <see cref="GanttVirtualizationMode.Off"/> renders every row exactly as before (the markup snapshots
/// of the other suites pin that); <c>On</c> / <c>Auto</c> render only the visible window plus the
/// overscan, keep the treegrid a11y contract (rowcount / rowindex) and pin the rows a keyboard user or
/// a gesture still needs. The window itself is computed in the browser (gantt.js); the tests drive it
/// through the <c>OnViewportRowsChanged</c> entry the JS session calls.
/// </summary>
public class TmGanttVirtualizationTests : LocalizationTestBase
{
    /// <summary>Flat tree of <paramref name="count"/> tasks (ids t1..tN) with the dates GanttHelper needs.</summary>
    private static List<TmWorkItem> Tasks(int count, int groups = 0)
    {
        var tasks = new List<TmWorkItem>(count);
        for (var i = 1; i <= count; i++)
        {
            tasks.Add(new TmWorkItem
            {
                Id = $"t{i}",
                Title = $"Task {i}",
                ParentId = groups > 0 && i > groups ? $"t{((i - 2) % groups) + 1}" : null,
                Start = new DateTime(2024, 1, 1).AddDays(i % 90),
                End = new DateTime(2024, 1, 1).AddDays(i % 90 + 3),
                PercentComplete = i % 100,
            });
        }

        return tasks;
    }

    private static IReadOnlyList<GanttDependency> Chain(params (int From, int To)[] links) =>
        links.Select(l => new GanttDependency { Id = $"t{l.From}-t{l.To}", FromId = $"t{l.From}", ToId = $"t{l.To}" }).ToList();

    private IRenderedComponent<TmGantt> RenderVirtualized(
        List<TmWorkItem> tasks,
        Action<ComponentParameterCollectionBuilder<TmGantt>>? configure = null)
    {
        var cut = Render<TmGantt>(p =>
        {
            // The window gate is mode-active && session-attached: the test plays the browser session.
            JSInterop.Setup<bool>("tmGantt.attachVirtualScroll", _ => true).SetResult(true);
            p.Add(c => c.Items, tasks);
            p.Add(c => c.Virtualization, GanttVirtualizationMode.On);
            p.Add(c => c.ShowTimelineHeader, false);
            configure?.Invoke(p);
        });
        // The attach runs in OnAfterRenderAsync, one dispatcher hop behind the render — wait for it.
        cut.WaitForState(() => cut.Instance.VirtualizationActive);
        return cut;
    }

    // ═══════════════════════════════════════════════════════════════
    // Off is the default and byte-identical (TmGanttMarkupSnapshotTests pins the markup)
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Virtualization_Off_By_Default_Renders_All_Rows()
    {
        var cut = Render<TmGantt>(p => p.Add(c => c.Items, Tasks(320)));

        cut.FindAll(".tm-gantt__tree-row").Should().HaveCount(320);
        cut.FindAll(".tm-gantt__tree-spacer").Should().BeEmpty("the default DOM carries no virtualization scaffolding");
        cut.Find(".tm-gantt__tree-grid").GetAttribute("aria-rowcount").Should().BeNull();
        cut.FindAll(".tm-gantt__tree-row[aria-rowindex]").Should().BeEmpty();
        cut.FindAll(".tm-gantt__bar, .tm-gantt__milestone").Should().HaveCount(320);
    }

    [Fact]
    public void Virtualization_On_Without_An_Attached_Session_Renders_The_Full_Set()
    {
        // No tmGantt.attachVirtualScroll setup — the loose JS runtime answers false (prerender, a host
        // without the script): no row may hide behind a window nobody will ever compute.
        var cut = Render<TmGantt>(p => p
            .Add(c => c.Items, Tasks(500))
            .Add(c => c.Virtualization, GanttVirtualizationMode.On));

        // Let the (failing) attach attempt settle before judging the state it leaves behind.
        cut.WaitForAssertion(() => JSInterop.Invocations
            .Count(i => i.Identifier == "tmGantt.attachVirtualScroll").Should().BeGreaterThanOrEqualTo(1));

        cut.Instance.VirtualizationActive.Should().BeFalse("the session never confirmed itself");
        cut.FindAll(".tm-gantt__tree-row").Should().HaveCount(500);
        cut.FindAll(".tm-gantt__tree-spacer").Should().BeEmpty();
        cut.Find(".tm-gantt__tree-grid").GetAttribute("aria-rowcount").Should().BeNull();
    }

    [Fact]
    public async Task Virtualization_Session_Reattaches_When_The_Gesture_Key_Changes()
    {
        var attach = JSInterop.Setup<bool>("tmGantt.attachVirtualScroll", _ => true).SetResult(true);
        var cut = Render<TmGantt>(p => p
            .Add(c => c.Items, Tasks(10))
            .Add(c => c.Virtualization, GanttVirtualizationMode.On));
        cut.WaitForState(() => cut.Instance.VirtualizationActive);
        attach.Invocations.Should().HaveCount(1);

        await SetParamAsync(cut, nameof(TmGantt.WorkloadPlacement), GanttWorkloadPlacement.Docked);
        cut.WaitForAssertion(() => attach.Invocations.Should().HaveCount(2, "the docked workload replaces the scrollers: the session re-attaches, never binding to detached elements"));

        await SetParamAsync(cut, nameof(TmGantt.View), GanttView.List);
        cut.WaitForAssertion(() => attach.Invocations.Should().HaveCount(3, "chart → List replaces the timeline body with the tree body"));

        await SetParamAsync(cut, nameof(TmGantt.View), GanttView.Week);
        cut.WaitForAssertion(() => attach.Invocations.Should().HaveCount(4, "List → chart re-attaches to the restored timeline body"));
    }

    /// <summary>Sets one parameter the way a host re-render would and lets the render cycle finish.</summary>
    private static async Task SetParamAsync<TValue>(IRenderedComponent<TmGantt> cut, string name, TValue value) =>
        await cut.InvokeAsync(async () => await cut.Instance.SetParametersAsync(ParameterView.FromDictionary(
            new Dictionary<string, object?> { [name] = value })));

    [Fact]
    public void Virtualization_List_View_Attaches_The_Tree_Body_As_Both_Scrollers()
    {
        var attach = JSInterop.Setup<bool>("tmGantt.attachVirtualScroll", _ => true).SetResult(true);
        var cut = Render<TmGantt>(p => p
            .Add(c => c.Items, Tasks(10))
            .Add(c => c.Virtualization, GanttVirtualizationMode.On)
            .Add(c => c.View, GanttView.List));

        cut.WaitForAssertion(() => attach.Invocations.Should().ContainSingle());
        var call = attach.Invocations.Single();
        call.Arguments[2].Should().Be(call.Arguments[1], "the List view has no timeline: the tree body is both scrollers");
    }

    [Fact]
    public void Virtualization_Inactive_Mode_Disables_The_Window_Reports_In_The_Session()
    {
        JSInterop.Setup<bool>("tmGantt.attachVirtualScroll", _ => true).SetResult(true);
        var update = JSInterop.SetupVoid("tmGantt.updateVirtualScroll", _ => true);
        var cut = Render<TmGantt>(p => p
            .Add(c => c.Items, Tasks(10))
            .Add(c => c.Virtualization, GanttVirtualizationMode.Auto));

        cut.WaitForAssertion(() => update.Invocations.Should().ContainSingle());
        var call = update.Invocations.Single();
        call.Arguments[3].Should().Be(false, "Auto under the threshold: the session owns the sync but must not report windows");
    }

    [Fact]
    public async Task Virtualization_Reports_Resume_From_The_Stored_Window_After_A_Mode_Flip()
    {
        var tasks = Tasks(500);
        var cut = RenderVirtualized(tasks);
        cut.Instance.OnViewportRowsChanged(100, 140, 20);

        await SetParamAsync(cut, nameof(TmGantt.Virtualization), GanttVirtualizationMode.Off);
        cut.FindAll(".tm-gantt__tree-row").Should().HaveCount(500, "Off renders the full set");

        await SetParamAsync(cut, nameof(TmGantt.Virtualization), GanttVirtualizationMode.On);
        cut.Instance.WindowStart.Should().Be(100, "the report is stored even while the gate was off");
        cut.Instance.WindowEnd.Should().Be(140);
    }

    [Fact]
    public async Task Virtualization_DrawVersion_Change_Asks_The_Browser_To_Remasure()
    {
        var remeasure = JSInterop.SetupVoid("tmGantt.remeasureVirtual", _ => true);
        var tasks = Tasks(20, groups: 4);
        var cut = RenderVirtualized(tasks);
        remeasure.Invocations.Should().BeEmpty("the fresh session measures itself");

        await cut.Instance.ToggleExpandAsync(tasks[0]);
        cut.Render();
        // The row count moved under the session: force a re-measure.
        cut.WaitForAssertion(() => remeasure.Invocations.Should().ContainSingle());
    }

    // ═══════════════════════════════════════════════════════════════
    // Window rendering
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Virtualization_On_Renders_Only_Window_Plus_Overscan_In_Tree_And_Timeline()
    {
        var tasks = Tasks(500);
        var cut = RenderVirtualized(tasks);

        cut.Instance.OnViewportRowsChanged(100, 140, 20);

        // The window is 40 rows; the roving tab stop (the first row, outside the window) stays pinned.
        cut.FindAll(".tm-gantt__tree-row").Should().HaveCount(41);
        cut.FindAll("[data-testid='tree-row-t101']").Should().HaveCount(1, "the first window row renders");
        cut.FindAll("[data-testid='tree-row-t99']").Should().BeEmpty("the row above the window (and not pinned) is not rendered");
        cut.FindAll("[data-testid='tree-row-t141']").Should().BeEmpty("the row below the window is not rendered");
        cut.FindAll("[data-testid='task-bar-t120']").Should().HaveCount(1, "the bars follow the same window");
        cut.FindAll("[data-testid='task-bar-t99']").Should().BeEmpty();
        cut.FindAll("[data-testid='task-bar-t141']").Should().BeEmpty();
    }

    [Fact]
    public void Virtualization_Auto_Activates_Above_Threshold()
    {
        var small = Tasks(299);
        var smallCut = Render<TmGantt>(p => p
            .Add(c => c.Items, small)
            .Add(c => c.Virtualization, GanttVirtualizationMode.Auto));

        smallCut.FindAll(".tm-gantt__tree-row").Should().HaveCount(299, "below the threshold every row renders");
        smallCut.FindAll(".tm-gantt__tree-spacer").Should().BeEmpty();

        var large = Tasks(320);
        var largeCut = Render<TmGantt>(p =>
        {
            JSInterop.Setup<bool>("tmGantt.attachVirtualScroll", _ => true).SetResult(true);
            p.Add(c => c.Items, large);
            p.Add(c => c.Virtualization, GanttVirtualizationMode.Auto);
        });

        largeCut.WaitForState(() => largeCut.Instance.VirtualizationActive);
        largeCut.Instance.OnViewportRowsChanged(0, 40, 20);
        largeCut.FindAll(".tm-gantt__tree-row").Should().HaveCount(40, "at or above the threshold the window renders");
    }

    [Fact]
    public void Virtualization_Empty_Set_Under_A_Live_Report_Is_An_Empty_Window_Not_A_Crash()
    {
        // Every task filtered away while the session is live: the next report arrives for a set of zero —
        // the window empties, the Clamp never sees an empty range.
        var cut = RenderVirtualized([]);

        cut.Instance.OnViewportRowsChanged(5, 40, 10);

        cut.Instance.WindowStart.Should().Be(0);
        cut.Instance.WindowEnd.Should().Be(0);
        cut.FindAll(".tm-gantt__tree-row").Should().BeEmpty();
    }

    [Fact]
    public void Virtualization_PageRows_Comes_From_The_Reported_Visible_Rows()
    {
        var cut = RenderVirtualized(Tasks(500));

        cut.Instance.PageRows.Should().Be(15, "no browser report yet — the fallback page");
        cut.Instance.OnViewportRowsChanged(0, 40, 17);
        cut.Instance.PageRows.Should().Be(17, "the page is what the viewport really shows, not the window span");
    }

    [Fact]
    public void Virtualization_Tree_Uses_Top_And_Bottom_Spacers_With_Exact_Height()
    {
        var cut = RenderVirtualized(Tasks(500));

        cut.Instance.OnViewportRowsChanged(100, 140, 20);

        var spacers = cut.FindAll(".tm-gantt__tree-spacer");
        spacers.Should().HaveCount(2);
        // Top spacer: exactly the 100 rows above the window (the pinned first row is absolutely positioned, it takes no flow slot).
        spacers[0].GetAttribute("style").Should().Contain("height: 4000px");
        spacers[0].GetAttribute("aria-hidden").Should().Be("true");
        // Bottom spacer: 500 - 140 rows.
        spacers[1].GetAttribute("style").Should().Contain("height: 14400px");
        // The pinned first row anchors at its true absolute offset.
        cut.Find("[data-testid='tree-row-t1']").GetAttribute("style").Should().Contain("top: 0px");
        cut.Find("[data-testid='tree-row-t1']").ClassList.Should().Contain("tm-gantt__tree-row--pinned-out");
    }

    [Fact]
    public void Virtualization_Timeline_Content_Keeps_Full_Height()
    {
        var cut = RenderVirtualized(Tasks(500));

        cut.Instance.OnViewportRowsChanged(100, 140, 20);

        cut.Find(".tm-gantt__timeline-content")
            .GetAttribute("style").Should().Contain("height: 20000px", "500 rows × 40 px — the scroll area never shrinks");
    }

    [Fact]
    public void Virtualization_Dependencies_Only_Intersecting_Window()
    {
        var tasks = Tasks(500);
        var cut = RenderVirtualized(tasks, p => p.Add(c => c.DependencyItems, Chain((10, 20), (90, 150), (100, 120), (300, 310))));

        cut.Instance.OnViewportRowsChanged(100, 140, 20);

        var lines = cut.FindAll(".tm-gantt__dependency-line").Select(l => l.GetAttribute("data-testid")).ToList();
        lines.Should().Contain("dep-line-t90-t150",
            "the line's y-range crosses the window even though both endpoints are outside it");
        lines.Should().NotContain("dep-line-t10-t20", "fully above the window");
        lines.Should().NotContain("dep-line-t300-t310", "fully below the window");
        lines.Should().Contain("dep-line-t100-t120", "an ordinary in-window dependency");
    }

    // ═══════════════════════════════════════════════════════════════
    // A11y
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Virtualization_Treegrid_Has_AriaRowcount_And_AriaRowindex()
    {
        var cut = RenderVirtualized(Tasks(500));

        cut.Instance.OnViewportRowsChanged(100, 140, 20);

        cut.Find(".tm-gantt__tree-grid").GetAttribute("aria-rowcount").Should().Be("501", "visible nodes + the header row");
        cut.Find("[data-testid='tree-row-t101']").GetAttribute("aria-rowindex").Should().Be("102");
        cut.Find("[data-testid='tree-row-t140']").GetAttribute("aria-rowindex").Should().Be("141");
        // The pinned first row keeps its absolute position.
        cut.Find("[data-testid='tree-row-t1']").GetAttribute("aria-rowindex").Should().Be("2");
    }

    // ═══════════════════════════════════════════════════════════════
    // Pinned rows
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Virtualization_Focused_Selected_Edited_And_Dragged_Rows_Stay_Pinned()
    {
        var tasks = Tasks(500);
        var cut = RenderVirtualized(tasks, p => p.Add(c => c.SelectedTask, tasks[199]));

        cut.Instance.OnViewportRowsChanged(100, 140, 20);
        cut.FindAll(".tm-gantt__tree-row").Should().HaveCount(41, "40 window rows + the selected row pinned");

        cut.Instance.StartInlineEdit("t250", GanttColumnKey.Title, "Task 250");
        cut.Instance._draggedTaskId = "t260";
        cut.Render();

        cut.FindAll(".tm-gantt__tree-row").Should().HaveCount(43, "+ the inline-edited and the dragged row");
        cut.FindAll("[data-testid='tree-row-t250']").Should().HaveCount(1);
        cut.FindAll("[data-testid='tree-row-t260']").Should().HaveCount(1);
        cut.Find("[data-testid='tree-row-t250'] .tm-gantt__inline-edit").Should().NotBeNull();
        // Pinned rows keep their absolute aria position.
        cut.Find("[data-testid='tree-row-t250']").GetAttribute("aria-rowindex").Should().Be("251");
    }

    // ═══════════════════════════════════════════════════════════════
    // Keyboard across the window
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Virtualization_ArrowDown_Past_Window_Scrolls_Then_Focuses()
    {
        var tasks = Tasks(500);
        var cut = RenderVirtualized(tasks);

        cut.Instance.OnViewportRowsChanged(0, 30, 10);
        cut.FindAll("[data-testid='tree-row-t31']").Should().BeEmpty();

        var row = cut.Find("[data-testid='tree-row-t30']");
        row.KeyDown(new KeyboardEventArgs { Key = "ArrowDown" });

        // The target row renders pinned with the roving tab stop; the real scroll is the JS scrollToRow session.
        var pinned = cut.Find("[data-testid='tree-row-t31']");
        pinned.GetAttribute("tabindex").Should().Be("0");
    }

    [Fact]
    public void Virtualization_Home_End_PageUp_PageDown()
    {
        var tasks = Tasks(500);
        var cut = RenderVirtualized(tasks);
        cut.Instance.OnViewportRowsChanged(0, 30, 10);

        void PressLast(string key)
        {
            var rows = cut.FindAll(".tm-gantt__tree-row[tabindex='0']");
            rows.Count.Should().Be(1);
            rows[0].KeyDown(new KeyboardEventArgs { Key = key });
        }

        PressLast("End");
        cut.Find("[data-testid='tree-row-t500']").GetAttribute("tabindex").Should().Be("0");

        PressLast("PageUp");
        // The page is the window's visible span (30 rows - 2 × 10 overscan = 10 rows): index 499 - 10 → t490.
        cut.Find("[data-testid='tree-row-t490']").GetAttribute("tabindex").Should().Be("0");

        PressLast("Home");
        cut.Find("[data-testid='tree-row-t1']").GetAttribute("tabindex").Should().Be("0");

        PressLast("PageDown");
        cut.Find("[data-testid='tree-row-t11']").GetAttribute("tabindex").Should().Be("0");
    }

    // ═══════════════════════════════════════════════════════════════
    // Collapse / expand
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Virtualization_Collapse_Expand_Recomputes_Window_Without_Jump()
    {
        // 100 parents with 4 children each = 500 visible rows.
        var tasks = new List<TmWorkItem>();
        for (var g = 1; g <= 100; g++)
        {
            tasks.Add(new TmWorkItem { Id = $"t{g}", Title = $"Group {g}", Start = new DateTime(2024, 1, 1), End = new DateTime(2024, 3, 30) });
            for (var c = 1; c <= 4; c++)
            {
                tasks.Add(new TmWorkItem
                {
                    Id = $"t{g}-{c}", Title = $"Child {g}.{c}", ParentId = $"t{g}",
                    Start = new DateTime(2024, 1, 1).AddDays(g + c), End = new DateTime(2024, 1, 1).AddDays(g + c + 2),
                });
            }
        }

        var cut = RenderVirtualized(tasks);
        cut.Instance.OnViewportRowsChanged(400, 440, 20);

        // Collapsing ONE parent above the window keeps the window where it was (no reset to the top);
        // the rows re-anchor to the shifted set (t81 moves one row up, out of the window).
        cut.Instance.ToggleExpandAsync(tasks[0]);
        cut.Render();
        cut.Instance.WindowStart.Should().Be(400, "the collapse above the window does not reset it");
        cut.Instance.WindowEnd.Should().Be(440);
        cut.FindAll("[data-testid='tree-row-t81-4']").Should().NotBeEmpty("the window re-anchors to the shifted set");

        // Collapsing EVERY other group root (t2..t96) shrinks the data under the window: it clamps instead of
        // crashing or jumping to 0.
        foreach (var root in tasks.Where(t => t.ParentId is null).Skip(1).Take(95))
        {
            cut.Instance.ToggleExpandAsync(root);
        }
        cut.Render();

        // 116 rows survive (100 collapsed roots + the 16 rows of groups 97–100); the window clamps to the
        // tail (index 115 = t100-4) and the first visible row stays pinned as the roving tab stop.
        cut.FindAll(".tm-gantt__tree-row").Should().HaveCount(2);
        cut.Find("[data-testid='tree-row-t1']").Should().NotBeNull("the roving tab stop stays pinned");
        cut.Find("[data-testid='tree-row-t100-4']").Should().NotBeNull("the clamped window shows the last row");

        // Expanding again restores the count; the window starts from the STORED report (row 400) clamped
        // into the smaller set — the tail stays painted until the browser's next report follows the clamped
        // scroll position.
        cut.Instance.ToggleExpandAsync(tasks[0]);
        cut.Render();
        cut.Instance.WindowStart.Should().Be(119, "the stored report clamps into the 120-row set");
        cut.Instance.WindowEnd.Should().Be(120);
        cut.Find("[data-testid='tree-row-t100-4']").Should().NotBeNull("the tail row stays painted");
    }

    // ═══════════════════════════════════════════════════════════════
    // Render gate
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Virtualization_RenderGate_Includes_Window()
    {
        var tasks = Tasks(500);
        var cut = RenderVirtualized(tasks);
        var tree = cut.FindComponent<TmGanttTree>();
        var timeline = cut.FindComponent<TmGanttTimeline>();

        cut.Instance.OnViewportRowsChanged(200, 240, 20);
        var treeRenders = tree.RenderCount;
        var timelineRenders = timeline.RenderCount;
        treeRenders.Should().BeGreaterThan(0, "the window shift is a render of both parts");
        timelineRenders.Should().BeGreaterThan(0);

        // The same window again (a scroll inside the hysteresis): neither part repaints.
        cut.Instance.OnViewportRowsChanged(200, 240, 20);
        tree.RenderCount.Should().Be(treeRenders);
        timeline.RenderCount.Should().Be(timelineRenders);
    }

    // ═══════════════════════════════════════════════════════════════
    // Layouts
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Virtualization_Mobile_56px_Rows_Window()
    {
        var cut = Render<TmGantt>(p =>
        {
            JSInterop.Setup<bool>("tmGantt.attachVirtualScroll", _ => true).SetResult(true);
            p.Add(c => c.Items, Tasks(500));
            p.Add(c => c.Virtualization, GanttVirtualizationMode.On);
            p.Add(c => c.LayoutMode, TmLayoutMode.Mobile);
        });
        cut.WaitForState(() => cut.Instance.VirtualizationActive);

        cut.Instance.OnViewportRowsChanged(10, 20, 8);

        // The pinned roving tab stop (the first row, above the window) is absolutely positioned: the top spacer is exact.
        var spacers = cut.FindAll(".tm-gantt__tree-spacer");
        spacers.Should().HaveCount(2);
        spacers[0].GetAttribute("style").Should().Contain("height: 560px", "10 rows × 56 px above the window");
        spacers[1].GetAttribute("style").Should().Contain("height: 26880px");
        cut.Find(".tm-gantt__timeline-content").GetAttribute("style").Should().Contain("height: 28000px");
    }

    // ═══════════════════════════════════════════════════════════════
    // Fingerprint cache
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void TaskFingerprint_Cache_Still_Detects_InPlace_Edit()
    {
        var tasks = Tasks(4);
        var cut = Render<TmGantt>(p => p.Add(c => c.Items, tasks));

        var computationsAfterRender = cut.Instance.FingerprintComputations;
        computationsAfterRender.Should().BeGreaterThan(0);

        // A chrome-only click re-evaluates BOTH part gates but computes the fingerprint once (the cache).
        cut.Find("[data-testid='gantt-filter-btn']").Click();
        cut.Instance.FingerprintComputations.Should().Be(computationsAfterRender + 1,
            "the tree gate and the timeline gate share one fingerprint computation per render pass");

        // An in-place edit between passes must still be detected: the next pass recomputes and repaints.
        tasks[2].Title = "Renamed in place";
        cut.Render();

        cut.Find("[data-testid='tree-cell-title-t3']").TextContent.Trim().Should().Be("Renamed in place");
        cut.Find("[data-testid='task-bar-t3']").GetAttribute("title").Should().Be("Renamed in place (3%)");
        cut.Instance.FingerprintComputations.Should().Be(computationsAfterRender + 2, "the new pass recomputes once");
    }
}
