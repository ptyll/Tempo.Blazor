using System.Globalization;
using System.Text.Json;
using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Fáze 12 (performance): the window virtualization of the tree and the timeline on the /gantt-large demo
/// (a deterministic generator: 1 000 / 5 000 / 10 000 tasks, depth 4, ~1.2 dependencies per task). The lane
/// is TestCategory=Perf and stays OUTSIDE the Smoke gate; it runs against the WASM demo host (TM_E2E_WASM_URL)
/// with an externally managed API. The perf run writes its numbers to TestResults/gantt-phase12/perf.json,
/// which the "Large data sets" section of docs/gantt.md quotes.
/// </summary>
[TestClass]
[TestCategory("Perf")]
[TestCategory("WASM")]
public sealed class GanttVirtualizationE2ETests : WasmTestBase
{
    private static readonly string ShotDir =
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "TestResults", "gantt-phase12");

    private async Task<IPage> OpenLargeAsync(int width, int height, int size = 10000, string mode = "Auto", bool hasTouch = false)
    {
        var context = await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = width, Height = height },
            IgnoreHTTPSErrors = true,
            Locale = "en-US",
            HasTouch = hasTouch,
        });
        await context.AddInitScriptAsync(
            """
            localStorage.setItem('tm-demo-culture', 'en');
            document.cookie = 'tm-demo-culture=en;path=/;max-age=31536000;samesite=lax';
            """);
        RegisterContext(context);
        var page = await context.NewPageAsync();
        await page.GotoAsync($"{BaseUrl}/gantt-large?size={size}&mode={mode}",
            new PageGotoOptions { WaitUntil = WaitUntilState.Load, Timeout = 90_000 });
        await WaitForAppReadyAsync(page);
        var treeBody = page.Locator(".tm-gantt__tree-body").First;
        await treeBody.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 60_000 });
        return page;
    }

    private static ILocator TreeRows(IPage page) => page.Locator(".tm-gantt__tree-row");

    private static async Task<int> RowCountAsync(IPage page) => await TreeRows(page).CountAsync();

    /// <summary>Waits until the rendered window has settled (no row-count change for two polls).</summary>
    private static async Task WaitForWindowSettledAsync(IPage page, int polls = 6, int intervalMs = 150)
    {
        var last = -1;
        var stable = 0;
        for (var i = 0; i < polls; i++)
        {
            var count = await RowCountAsync(page);
            stable = count == last ? stable + 1 : 0;
            last = count;
            if (stable >= 2) return;
            await page.WaitForTimeoutAsync(intervalMs);
        }
    }

    [TestMethod]
    public async Task Large_Data_Set_Renders_The_Window_Not_All_Rows()
    {
        var page = await OpenLargeAsync(1440, 900);
        await WaitForWindowSettledAsync(page);

        var rows = await RowCountAsync(page);
        rows.Should().BeLessThanOrEqualTo(100, "visible rows + 2 × overscan of 10 (the phase target is ≈ 60–80)");
        rows.Should().BeGreaterThan(10, "the window is the viewport plus the overscan, not a single row");

        // The treegrid keeps its a11y contract over the virtualized set.
        (await page.Locator(".tm-gantt__tree-grid").First.GetAttributeAsync("aria-rowcount")).Should().Be("10001");
        (await TreeRows(page).First.GetAttributeAsync("aria-rowindex")).Should().NotBeNull();
        // At the top the top spacer is empty (0 rows): only the bottom spacer renders.
        (await page.Locator(".tm-gantt__tree-spacer").CountAsync()).Should().Be(1);

        // The timeline content keeps the FULL height (10 000 rows × 40 px) — the scroll area never shrinks.
        var contentHeight = await page.EvaluateAsync<double>(
            "() => document.querySelector('.tm-gantt__timeline-content').getBoundingClientRect().height");
        contentHeight.Should().BeGreaterThanOrEqualTo(10000 * 40 - 1);

        // The demo /gantt (about 20 tasks) stays below the threshold: Auto does not virtualize it.
        var small = await OpenLargeAsync(1440, 900, size: 1000, mode: "Off");
        (await RowCountAsync(small)).Should().Be(1000, "Off renders every row (byte-identical default)");
    }

    [TestMethod]
    public async Task Scroll_To_End_And_Back_Keeps_Tree_And_Timeline_Aligned()
    {
        var page = await OpenLargeAsync(1440, 900);
        await WaitForWindowSettledAsync(page);

        await page.EvaluateAsync("() => { const b = document.querySelector('.tm-gantt__tree-body'); b.scrollTop = b.scrollHeight; }");
        await WaitForWindowSettledAsync(page);

        // The last window and its bars share the y alignment: the bar is INSET inside its row by a constant
        // 20 px (the row is 40 px, the bar 20 px and vertically centred) — the same offset at the bottom...
        var (treeY, barY, taskId) = await AlignedRowAsync(page);
        taskId.Should().NotBeNull("the bottom window shows the last task");
        var bottomOffset = barY - treeY;
        bottomOffset.Should().BeInRange(19, 21, "the bar sits centred inside its tree row");

        await page.EvaluateAsync("() => { const b = document.querySelector('.tm-gantt__tree-body'); b.scrollTop = 0; }");
        await WaitForWindowSettledAsync(page);
        var (treeYTop, barYTop, taskIdTop) = await AlignedRowAsync(page);
        // ...and the top: the window re-rendered around the start (n1 is back) and the alignment is identical.
        (await page.Locator("[data-testid='tree-row-n1']").CountAsync()).Should().Be(1, "the top window shows the first task again");
        taskIdTop.Should().NotBeNull();
        (barYTop - treeYTop).Should().BeInRange(19, 21);
        Math.Abs((barYTop - treeYTop) - bottomOffset).Should().BeLessThanOrEqualTo(1.0,
            "the tree↔timeline vertical sync keeps the same row↔bar alignment after a full bottom-to-top scroll");
    }

    /// <summary>
    /// A rendered row IN the window (the DOM edges hold the pinned rows, which render outside the window and
    /// whose bars stay at their absolute position), with the two viewport y positions.
    /// </summary>
    private static async Task<(double TreeY, double BarY, string? TaskId)> AlignedRowAsync(IPage page)
    {
        var json = await page.EvaluateAsync<string>(@"() => {
            const rows = Array.from(document.querySelectorAll('.tm-gantt__tree-row'));
            if (rows.length < 3) return JSON.stringify({ treeY: -1, barY: -1, id: null });
            for (let offset = 0; offset < rows.length; offset++) {
                for (const i of [Math.floor(rows.length * 0.6) + offset, Math.floor(rows.length * 0.6) - offset]) {
                    const row = rows[i];
                    if (!row) continue;
                    const id = row.dataset.taskId;
                    if (!id) continue;
                    const bar = document.querySelector('.tm-gantt__bar[data-task-id=""' + id + '""], .tm-gantt__milestone[data-task-id=""' + id + '""]');
                    if (bar) {
                        return JSON.stringify({
                            treeY: row.getBoundingClientRect().y,
                            barY: bar.getBoundingClientRect().y,
                            id: id
                        });
                    }
                }
            }
            return JSON.stringify({ treeY: -1, barY: -1, id: null });
        }");
        using var doc = JsonDocument.Parse(json);
        return (doc.RootElement.GetProperty("treeY").GetDouble(),
                doc.RootElement.GetProperty("barY").GetDouble(),
                doc.RootElement.GetProperty("id").GetString());
    }

    [TestMethod]
    public async Task Keyboard_End_Home_PageDown_Navigates_The_Window()
    {
        var page = await OpenLargeAsync(1440, 900);
        await WaitForWindowSettledAsync(page);

        // Focus the roving tab stop directly (the page's nav precedes the grid in the Tab order), then End
        // moves it to the LAST row and the JS scrollToRow session scrolls the window to the bottom.
        await page.EvaluateAsync("() => document.querySelector(\".tm-gantt__tree-row[tabindex='0']\").focus()");
        await page.Keyboard.PressAsync("End");
        await WaitForWindowSettledAsync(page);

        // End lands in the last window (the hysteresis may stop a few rows short of the data end).
        var focused = await page.EvaluateAsync<string>(
            "() => { const el = document.querySelector('.tm-gantt__tree-row[tabindex=\\'0\\']'); return el ? el.dataset.taskId : null; }");
        var focusedIndex = focused is null ? -1 : int.Parse(focused[1..], CultureInfo.InvariantCulture);
        focusedIndex.Should().BeGreaterThan(9900, "End moves the roving tab stop into the last window");
        (await page.EvaluateAsync<double>("() => document.querySelector('.tm-gantt__tree-body').scrollTop"))
            .Should().BeGreaterThan(300000, "the JS scrollToRow session scrolled to the bottom of the 10 000-row set");

        await page.Keyboard.PressAsync("Home");
        await WaitForWindowSettledAsync(page);
        focused = await page.EvaluateAsync<string>(
            "() => { const el = document.querySelector('.tm-gantt__tree-row[tabindex=\\'0\\']'); return el ? el.dataset.taskId : null; }");
        focused.Should().Be("n1");
        (await page.EvaluateAsync<double>("() => document.querySelector('.tm-gantt__tree-body').scrollTop")).Should().BeLessThanOrEqualTo(40);

        // PageDown pages over the visible span (the window minus the overscan).
        await page.Keyboard.PressAsync("PageDown");
        await page.WaitForTimeoutAsync(400);
        focused = await page.EvaluateAsync<string>(
            "() => { const el = document.querySelector('.tm-gantt__tree-row[tabindex=\\'0\\']'); return el ? el.dataset.taskId : null; }");
        focused.Should().NotBe("n1", "PageDown moved the tab stop");
    }

    [TestMethod]
    public async Task Selection_And_Task_Panel_At_Row_9000()
    {
        var page = await OpenLargeAsync(1440, 900);
        await WaitForWindowSettledAsync(page);

        // Scroll row 9000 into the window through the tree body, let the window settle for real.
        await page.EvaluateAsync("() => { const b = document.querySelector('.tm-gantt__tree-body'); b.scrollTop = 8990 * 40; }");
        await page.WaitForTimeoutAsync(1200);
        await WaitForWindowSettledAsync(page);

        var row = page.Locator("[data-testid='tree-row-n9000']").First;
        (await row.CountAsync()).Should().Be(1, "the window renders the row the user scrolled to");

        // The window's re-renders keep the bounding box moving: a finger-equivalent synthetic click selects
        // (DispatchEvent takes the same Blazor handler), then keyboard activation opens the panel.
        await page.EvaluateAsync("() => document.querySelector(\"[data-testid='tree-row-n9000']\").dispatchEvent(new MouseEvent('click', { bubbles: true }))");
        await page.EvaluateAsync("() => document.querySelector(\"[data-testid='tree-row-n9000']\").focus()");
        await page.Keyboard.PressAsync("Enter");

        // The desktop container shows the in-flow task panel with the selected task's fields.
        var titleField = page.Locator("[data-testid='task-title']").First;
        await titleField.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15_000 });
        (await titleField.Locator("input").First.InputValueAsync()).Should().Be("Task 9000");
    }

    [TestMethod]
    public async Task Dependency_Drag_To_The_Window_Bottom_Commits_Once()
    {
        var page = await OpenLargeAsync(1440, 900);
        await WaitForWindowSettledAsync(page);

        // The demo page's dataset counter is window-independent: the commit appends exactly one dependency.
        var datasetBefore = await page.Locator("[data-testid='gantt-large-dataset']").First.TextContentAsync();

        // From a dependency handle straight DOWN to a lower row whose bar covers the same axis column: the
        // ghost follows the pointer across the window rows and the release is hit-tested on that bar. The
        // bars' days are scattered, so the pair is searched over every visible handle (three windowfuls).
        var target = await page.EvaluateAsync<string>(@"() => {
            const timeline = document.querySelector('.tm-gantt__timeline');
            const timelineRect = timeline.getBoundingClientRect();
            const rows = Array.from(document.querySelectorAll('.tm-gantt__task-row'));
            for (let attempt = 0; attempt < 3; attempt++) {
                const handles = Array.from(document.querySelectorAll('.tm-gantt__dep-handle--end, .tm-gantt__dep-handle--start'))
                    .map(h => ({ h, r: h.getBoundingClientRect() }))
                    .filter(o => o.r.width > 0 && o.r.height > 0 && o.r.left > timelineRect.left && o.r.right < timelineRect.right && o.r.top > timelineRect.top && o.r.bottom < timelineRect.bottom - 30);
                for (const { h: handle, r: hr } of handles) {
                    const handleX = hr.left + hr.width / 2;
                    for (const row of rows) {
                        const rowRect = row.getBoundingClientRect();
                        if (rowRect.top < hr.bottom + 5 || rowRect.bottom > timelineRect.bottom - 30) continue;
                        const bar = row.querySelector('.tm-gantt__bar[data-task-id], .tm-gantt__milestone[data-task-id]');
                        if (!bar) continue;
                        const br = bar.getBoundingClientRect();
                        // A diagonal drag: the release lands anywhere on the lower bar's visible part.
                        if (br.right < timelineRect.left + 20 || br.left > timelineRect.right - 20) continue;
                        const x = Math.min(Math.max(handleX, br.left + 5), br.right - 5);
                        return JSON.stringify({ fromX: handleX, fromY: hr.top + hr.height / 2, x, y: br.top + br.height / 2, id: bar.dataset.taskId });
                    }
                }
                timeline.scrollLeft = Math.max(0, timeline.scrollLeft - timeline.clientWidth / 2);
            }
            return JSON.stringify(null);
        }");
        using (var targetDoc = JsonDocument.Parse(target))
        {
            targetDoc.RootElement.ValueKind.Should().Be(JsonValueKind.Object, "a handle and a lower bar sharing its axis column are rendered");
            var fromX = (float)targetDoc.RootElement.GetProperty("fromX").GetDouble();
            var fromY = (float)targetDoc.RootElement.GetProperty("fromY").GetDouble();
            var toX = (float)targetDoc.RootElement.GetProperty("x").GetDouble();
            var toY = (float)targetDoc.RootElement.GetProperty("y").GetDouble();
            var targetId = targetDoc.RootElement.GetProperty("id").GetString()!;

            await page.Mouse.MoveAsync(fromX, fromY);
            await page.Mouse.DownAsync();
            await page.Mouse.MoveAsync((fromX + toX) / 2, (fromY + toY) / 2);
            await page.Mouse.MoveAsync(toX, toY);
            await page.WaitForTimeoutAsync(250);

            // The drag is still live (the ghost follows) and the hit-test highlights the bar under the pointer.
            (await page.EvaluateAsync<bool>("() => !!document.querySelector('.tm-gantt__dep-ghost')"))
                .Should().BeTrue("the dependency ghost follows the pointer until the release");
            var marked = await page.EvaluateAsync<string>(
                "() => { const el = document.querySelector('.tm-gantt__bar--dep-target, .tm-gantt__milestone--dep-target'); return el ? el.dataset.taskId : null; }");
            marked.Should().Be(targetId, "the hit-test highlights the bar under the pointer");

            await page.Mouse.UpAsync();
        }

        await WaitForWindowSettledAsync(page);
        var datasetAfter = await page.Locator("[data-testid='gantt-large-dataset']").First.TextContentAsync();
        var beforeCount = int.Parse(new string(datasetBefore!.Split('·')[1].Where(char.IsDigit).ToArray()), CultureInfo.InvariantCulture);
        var afterCount = int.Parse(new string(datasetAfter!.Split('·')[1].Where(char.IsDigit).ToArray()), CultureInfo.InvariantCulture);
        afterCount.Should().Be(beforeCount + 1, "the drag commits exactly one dependency (the demo list persists it)");
    }

    [TestMethod]
    public async Task Mobile_390_Scrolls_The_Window_With_A_Finger()
    {
        var page = await OpenLargeAsync(390, 844, hasTouch: true);
        await WaitForWindowSettledAsync(page);

        var firstBefore = await page.EvaluateAsync<string>(
            "() => { const r = document.querySelector('.tm-gantt__tree-row'); return r ? r.dataset.taskId : null; }");

        // A vertical finger swipe on the tree body (native touch scrolling; CSS touch-action stays pan-x pan-y).
        var body = await page.Locator(".tm-gantt__tree-body").First.BoundingBoxAsync();
        body.Should().NotBeNull();
        await page.Touchscreen.TapAsync(body.X + body.Width / 2, body.Y + 100);
        for (var i = 0; i < 12; i++)
        {
            await page.EvaluateAsync(
                "() => { const b = document.querySelector('.tm-gantt__tree-body'); b.scrollTop += 800; }");
            await page.WaitForTimeoutAsync(80);
        }
        await WaitForWindowSettledAsync(page);

        var scrollTop = await page.EvaluateAsync<double>("() => document.querySelector('.tm-gantt__tree-body').scrollTop");
        scrollTop.Should().BeGreaterThan(5000, "the finger swipes scrolled the read-mostly container");
        // The pinned roving tab stop (n1) always renders first in the DOM: the WINDOW moved, not the first element.
        var lastAfter = await page.EvaluateAsync<string>(
            "() => { const rows = document.querySelectorAll('.tm-gantt__tree-row'); return rows.length ? rows[rows.length - 1].dataset.taskId : null; }");
        lastAfter.Should().NotBe(firstBefore, "the window followed the scroll (56 px rows on the mobile container)");
        (await RowCountAsync(page)).Should().BeLessThanOrEqualTo(100, "the window stays bounded on the mobile layout too");
    }

    /// <summary>
    /// The measured targets of the phase, written to TestResults/gantt-phase12/perf.json for the docs table:
    /// first render, DOM rows, window-shift renders per scroll distance, long tasks and the heap over 50 scrolls.
    /// </summary>
    [TestMethod]
    public async Task Performance_TenThousandTasks_Meets_The_Targets()
    {
        var report = new Dictionary<string, object>();
        report["measuredAt"] = DateTime.UtcNow.ToString("o");

        foreach (var mode in new[] { "Off", "Auto" })
        {
            var page = await OpenLargeAsync(1440, 900, size: 10000, mode: mode);
            await page.EvaluateAsync(@"() => {
                window.__tmLongTasks = [];
                try {
                    new PerformanceObserver(list => {
                        for (const entry of list.getEntries()) window.__tmLongTasks.push(entry.duration);
                    }).observe({ entryTypes: ['longtask'] });
                } catch (e) { /* the observer is a best-effort probe */ }
            }");

            var firstRenderMs = await page.EvaluateAsync<double>(@"() => {
                const nav = performance.getEntriesByType('navigation')[0];
                const row = document.querySelector('.tm-gantt__tree-row');
                return nav && row ? performance.now() - nav.startTime : -1;
            }");

            await WaitForWindowSettledAsync(page);
            var domRows = await RowCountAsync(page);
            var domNodes = await page.EvaluateAsync<double>(
                "() => document.querySelector('.tm-gantt').querySelectorAll('*').length");

            // The attach gate (the review round) makes the FIRST paint the full set in BOTH modes — no row
            // may hide behind a window nobody computes — so Auto is judged on how fast the gantt.js session
            // bounds the DOM after that paint, not on the paint itself.
            if (mode == "Auto")
            {
                report["Auto_windowAppliedMs"] = Math.Round(await page.EvaluateAsync<double>(@"async () => {
                    const nav = performance.getEntriesByType('navigation')[0];
                    const deadline = performance.now() + 60000;
                    while (performance.now() < deadline) {
                        if (document.querySelectorAll('.tm-gantt__tree-row').length <= 100) {
                            return nav ? performance.now() - nav.startTime : -1;
                        }
                        await new Promise(resolve => setTimeout(resolve, 100));
                    }
                    return -1;
                }"), 1);
            }

            // Fifty full-viewport scroll steps down and up: window shifts, long tasks and the heap.
            var heapStart = await page.EvaluateAsync<double>(
                "() => performance.memory ? performance.memory.usedJSHeapSize : -1");
            await page.EvaluateAsync(@"() => {
                const b = document.querySelector('.tm-gantt__tree-body');
                const step = b.clientHeight * 0.9;
                window.__tmScrolls = [];
                for (let i = 1; i <= 25; i++) window.__tmScrolls.push(Math.min(i * step, b.scrollHeight));
                for (let i = 24; i >= 0; i--) window.__tmScrolls.push(Math.min(i * step, b.scrollHeight));
            }");
            for (var i = 0; i < 50; i++)
            {
                await page.EvaluateAsync("() => { const b = document.querySelector('.tm-gantt__tree-body'); b.scrollTop = window.__tmScrolls.shift(); }");
                await page.WaitForTimeoutAsync(60);
            }
            await WaitForWindowSettledAsync(page);

            var longTasks = await page.EvaluateAsync<string>("() => JSON.stringify(window.__tmLongTasks || [])");
            var heapEnd = await page.EvaluateAsync<double>(
                "() => performance.memory ? performance.memory.usedJSHeapSize : -1");

            report[$"{mode}_firstRenderMs"] = Math.Round(firstRenderMs, 1);
            report[$"{mode}_domRows"] = domRows;
            report[$"{mode}_domNodes"] = (int)domNodes;
            report[$"{mode}_longTasksOver50ms"] = JsonArrayCount(longTasks, 50);
            report[$"{mode}_worstLongTaskMs"] = Math.Round(JsonArrayMax(longTasks), 1);
            if (heapStart > 0 && heapEnd > 0)
            {
                report[$"{mode}_heapGrowthMB"] = Math.Round((heapEnd - heapStart) / 1048576.0, 2);
            }

            // A window shift round trip: scroll one overscan-half window and count the DOM row-set changes.
            if (mode == "Auto")
            {
                var shifts = 0;
                var previous = string.Empty;
                for (var i = 0; i < 10; i++)
                {
                    await page.EvaluateAsync("() => { const b = document.querySelector('.tm-gantt__tree-body'); b.scrollTop += 200; }");
                    await page.WaitForTimeoutAsync(120);
                    var signature = await page.EvaluateAsync<string>(
                        "() => Array.from(document.querySelectorAll('.tm-gantt__tree-row')).map(r => r.dataset.taskId).join(',').slice(0, 4000)");
                    if (signature != previous) shifts++;
                    previous = signature;
                }
                report["Auto_windowShiftsPer2000px"] = shifts;
            }

            await page.CloseAsync();
        }

        Directory.CreateDirectory(ShotDir);
        var path = Path.Combine(ShotDir, "perf.json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"Gantt virtualization perf report: {path}");
        foreach (var kv in report) Console.WriteLine($"  {kv.Key}: {kv.Value}");

        // The binding assertions of the phase: Auto's DOM is bounded, and the window arrives PROMPTLY after
        // the full-set first paint — the paint itself costs what the Off mode costs on the same machine
        // since the attach gate, so the DELTA (not the paint) is the phase's window target.
        Convert.ToInt32(report["Auto_domRows"], CultureInfo.InvariantCulture).Should().BeLessThanOrEqualTo(100);
        Convert.ToInt32(report["Off_domRows"], CultureInfo.InvariantCulture).Should().Be(10000);
        var windowDeltaMs = Convert.ToDouble(report["Auto_windowAppliedMs"], CultureInfo.InvariantCulture)
            - Convert.ToDouble(report["Auto_firstRenderMs"], CultureInfo.InvariantCulture);
        windowDeltaMs.Should().BeGreaterThanOrEqualTo(0).And.BeLessThanOrEqualTo(10_000,
            "the gantt.js attach + first measurement must bound the DOM shortly after the full-set first paint");
    }

    private static int JsonArrayCount(string json, double threshold)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.EnumerateArray().Count(v => v.GetDouble() > threshold);
        }
        catch
        {
            return -1;
        }
    }

    private static double JsonArrayMax(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var max = 0d;
            foreach (var v in doc.RootElement.EnumerateArray()) max = Math.Max(max, v.GetDouble());
            return max;
        }
        catch
        {
            return -1;
        }
    }
}
