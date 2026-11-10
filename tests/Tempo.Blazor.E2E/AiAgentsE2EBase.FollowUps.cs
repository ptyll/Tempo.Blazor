using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Plan 7ba95ed6, Fáze 5 — the follow-ups of the two agent surfaces that are pure browser behaviour, so only a real
/// browser can prove them:
/// <list type="bullet">
/// <item>the autonomy dial's six keys belong to the slider and must not scroll the page (the guard is one ES module —
/// Blazor cannot preventDefault per key), while every OTHER key keeps its browser behaviour;</item>
/// <item>the memory panel keeps an undo row PER pending removal inside its ten-second window, so the middle row of two
/// can still be taken back and the undone removal never reaches the page;</item>
/// <item>the editor carries the CATEGORY beside the text, and the committed change regroups the row;</item>
/// <item>a 375 px card shows the status filter as a real select instead of the five-segment tray.</item>
/// </list>
/// </summary>
public abstract partial class AiAgentsE2EBase : PlaywrightTestBase
{
    /// <summary>
    /// The four arrows (and Home/End) are the dial's own keys. The module preventDefaults them on the focused slider —
    /// a plain keydown listener on the document (in its bubble phase, so it runs after the slider's own) reports what the
    /// browser was told — and the page does not move. A key the dial does NOT own stays unprevented, which is what proves
    /// the guard is conditional rather than a blanket `@onkeydown:preventDefault`.
    /// </summary>
    [TestMethod]
    public async Task TheAutonomyDialKeys_AreSwallowed_SoThePageNeverScrolls()
    {
        var page = await OpenAsync();
        await ScrollToSection(page, AutonomySection);

        var slider = page.Locator("[data-testid='ai-agents-autonomy'] [data-testid='autonomy-dial-track']");
        await slider.FocusAsync();
        await page.EvaluateAsync(
            "() => { window.__tmDialKeys = [];"
            + " document.addEventListener('keydown', event =>"
            + " window.__tmDialKeys.push({ key: event.key, prevented: event.defaultPrevented }), false); }");

        var before = await page.EvaluateAsync<double>("() => window.scrollY");
        Assert.AreEqual("1", await slider.GetAttributeAsync("aria-valuenow"));

        await page.Keyboard.PressAsync("ArrowDown");
        await page.WaitForTimeoutAsync(150);
        Assert.AreEqual("0", await slider.GetAttributeAsync("aria-valuenow"),
            "the arrow still steps the dial: the guard swallows the scroll, not the key");

        // The edge case: at the first grade ArrowDown changes nothing — and still must not scroll.
        await page.Keyboard.PressAsync("ArrowDown");
        await page.WaitForTimeoutAsync(150);
        Assert.AreEqual("0", await slider.GetAttributeAsync("aria-valuenow"),
            "an arrow past the end of the range changes nothing");

        await page.Keyboard.PressAsync("ArrowUp");
        await page.WaitForTimeoutAsync(150);
        Assert.AreEqual("1", await slider.GetAttributeAsync("aria-valuenow"));

        var after = await page.EvaluateAsync<double>("() => window.scrollY");
        Assert.AreEqual(before, after, "not one of the dial's keys scrolled the page");

        var keys = await page.EvaluateAsync<string[]>(
            "() => window.__tmDialKeys.map(entry => entry.key + (entry.prevented ? ':prevented' : ':default'))");
        CollectionAssert.AreEqual(
            new[] { "ArrowDown:prevented", "ArrowDown:prevented", "ArrowUp:prevented" },
            keys,
            "every owned key had its default action swallowed by the module");

        await ShootAsync(page, "ai-agents-autonomy-keys-light-1440");

        // The guard is conditional: Tab is not the dial's key, so it keeps moving the focus.
        await page.EvaluateAsync("() => { window.__tmDialKeys = []; }");
        await slider.FocusAsync();
        await page.Keyboard.PressAsync("Tab");
        await page.WaitForTimeoutAsync(150);
        var afterTab = await page.EvaluateAsync<string[]>(
            "() => window.__tmDialKeys.map(entry => entry.key + (entry.prevented ? ':prevented' : ':default'))");
        CollectionAssert.AreEqual(new[] { "Tab:default" }, afterTab,
            "a key the dial does not own must keep its browser behaviour");

        // Edge case: the same guarantee inside a 375 px phone viewport, where the page is much taller.
        var phone = await OpenAsync(375, 812, coarsePointer: true);
        await ScrollToSection(phone, AutonomySection);
        var phoneSlider = phone.Locator("[data-testid='ai-agents-autonomy'] [data-testid='autonomy-dial-track']");
        await phoneSlider.FocusAsync();
        var phoneBefore = await phone.EvaluateAsync<double>("() => window.scrollY");
        await phone.Keyboard.PressAsync("ArrowDown");
        await phone.Keyboard.PressAsync("ArrowDown");
        await phone.WaitForTimeoutAsync(200);
        Assert.AreEqual(phoneBefore, await phone.EvaluateAsync<double>("() => window.scrollY"),
            "a phone keeps the section in place too");
        Assert.AreEqual("0", await phoneSlider.GetAttributeAsync("aria-valuenow"));
        await ShootAsync(phone, "ai-agents-autonomy-keys-phone-375");
    }

    /// <summary>
    /// Two removals inside the one undo window are two independent promises: the panel keeps two rows with their own
    /// countdowns, the FIRST row can be taken back on its own (and the page never hears about it), and the editor that
    /// runs beside the text carries the category — a committed change regroups the row.
    /// </summary>
    [TestMethod]
    public async Task TheMemoryPanelQueuesTwoUndoRows_AndEditsTheCategory()
    {
        var page = await OpenAsync();
        await ScrollToSection(page, MemorySection);

        var panel = page.Locator("[data-testid='ai-agents-memory']");
        var units = "[data-testid='ai-agents-memory'] [data-testid='memory-item'][data-item-id='mem-units']";
        var tone = "[data-testid='ai-agents-memory'] [data-testid='memory-item'][data-item-id='mem-tone']";

        Assert.AreEqual("queue", await panel.GetAttributeAsync("data-undo-mode"),
            "the demo runs the queue so a reader can take back more than the newest removal");
        Assert.AreEqual("text category", await panel.GetAttributeAsync("data-editable"));

        await page.Locator(units + " [data-testid='memory-item-forget']").ClickAsync();
        await panel.Locator("[data-testid='memory-undo-row']").First
            .WaitForAsync(new LocatorWaitForOptions { Timeout = 10000 });
        await page.Locator(tone + " [data-testid='memory-item-forget']").ClickAsync();
        await page.WaitForFunctionAsync(
            "() => document.querySelectorAll(\"[data-testid='ai-agents-memory'] [data-testid='memory-undo-row']\").length === 2",
            new PageWaitForFunctionOptions { Timeout = 10000 });

        var rows = await panel.Locator("[data-testid='memory-undo-row']")
            .EvaluateAllAsync<string[]>("rows => rows.map(row => row.dataset.itemId)");
        CollectionAssert.AreEqual(new[] { "mem-units", "mem-tone" }, rows,
            "one undo row per pending removal, in the order they were removed");

        await ShootAsync(page, "ai-agents-memory-undo-queue-light-1440");

        // The FIRST row, not merely the newest one: the queue exists so any pending removal can be taken back.
        await panel.Locator("[data-testid='memory-undo-row'][data-item-id='mem-units'] [data-testid='memory-undo-action']")
            .ClickAsync();
        await page.WaitForFunctionAsync(
            "() => document.querySelectorAll(\"[data-testid='ai-agents-memory'] [data-testid='memory-undo-row']\").length === 1"
            + " && !!document.querySelector(\"[data-testid='ai-agents-memory'] [data-item-id='mem-units']\")",
            new PageWaitForFunctionOptions { Timeout = 10000 });
        Assert.IsFalse((await TextAsync(page, "ai-agents-memory-log")).Contains("Forget", StringComparison.Ordinal),
            "an undone removal is never reported to the page");

        // The editor carries the category beside the text, and the commit regroups the row.
        await page.Locator(units + " [data-testid='memory-item-edit']").ClickAsync();
        var category = page.Locator(units + " [data-testid='memory-item-edit-category']");
        await category.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        await TypeIntoBoundFieldAsync(page, category, "People");
        await page.Keyboard.PressAsync("Enter");
        await page.WaitForFunctionAsync(
            "() => document.querySelector(\"[data-testid='ai-agents-memory'] [data-item-id='mem-units']\")?.dataset.category === 'People'",
            new PageWaitForFunctionOptions { Timeout = 10000 });
        Assert.AreEqual(1, await page.Locator(
                "[data-testid='ai-agents-memory'] [data-testid='memory-group'][data-category='People'] "
                + "[data-testid='memory-item'][data-item-id='mem-units']").CountAsync(),
            "the row moved into the People group");
        StringAssert.Contains(await TextAsync(page, "ai-agents-memory-log"), "People",
            "the field-aware edit reached the page as one change");

        await ShootAsync(page, "ai-agents-memory-edit-category-light-1440");
        await AssertNoSeriousAxeViolationsAsync(page, "ai-agents-memory-section");

        await ToggleThemeAsync(page);
        await ScrollToSection(page, MemorySection);
        await ShootAsync(page, "ai-agents-memory-edit-category-dark-1440");

        // A 375 px card cannot hold five segments: the filter becomes a real select that feeds the same view.
        var phone = await OpenAsync(375, 812, coarsePointer: true);
        await ScrollToSection(phone, MemorySection);
        var phonePanel = phone.Locator("[data-testid='ai-agents-memory']");
        var select = phonePanel.Locator("[data-testid='memory-status-filter-select'] select");
        await select.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        Assert.IsTrue(await select.IsVisibleAsync(), "the narrow card shows the filter as a select");
        Assert.IsFalse(await phonePanel.Locator("[data-testid='memory-status-filter-segmented']").IsVisibleAsync(),
            "the scrolling tray is not on screen at 375 px");

        await select.SelectOptionAsync("paused");
        await phone.WaitForFunctionAsync(
            "() => document.querySelectorAll(\"[data-testid='ai-agents-memory'] [data-testid='memory-item']\").length === 1",
            new PageWaitForFunctionOptions { Timeout = 10000 });
        Assert.AreEqual("mem-zone", await phone.Locator(
                "[data-testid='ai-agents-memory'] [data-testid='memory-item']").GetAttributeAsync("data-item-id"),
            "the select narrows the same view the tray does");

        // The filtered view is shorter than the full one, so the card must be framed again before the capture.
        await ScrollToSection(phone, MemorySection);
        await ShootAsync(phone, "ai-agents-memory-filter-select-phone-375");

        // A wide card keeps the segmented tray (the default presentation).
        Assert.IsTrue(await page.Locator(
                "[data-testid='ai-agents-memory'] [data-testid='memory-status-filter-segmented']").IsVisibleAsync(),
            "the tray stays the wide-card presentation");
        Assert.IsFalse(await page.Locator(
                "[data-testid='ai-agents-memory'] [data-testid='memory-status-filter-select']").IsVisibleAsync(),
            "the select is only the narrow-card fallback");
    }

    /// <summary>
    /// Plan 7ba95ed6, Fáze 6 — the delegate board projects the landing column while a card is being dragged, and every
    /// outcome of the gesture returns the board to its resting state:
    /// <list type="bullet">
    /// <item>a real pointer drag from the backlog onto an agent column marks that column (<c>data-drag-over</c>) and
    /// projects the card's estimate into its availability line, while the SOURCE column shows its own load WITHOUT the
    /// lifted card;</item>
    /// <item>the live preview is a preview: the card only moves when the pointer is released;</item>
    /// <item>a FULL column is marked but promises nothing, and the drop is refused with the one announcement;</item>
    /// <item>Escape cancels the drag and every column goes back to its resting state.</item>
    /// </list>
    /// </summary>
    [TestMethod]
    public async Task TheDelegateBoardProjectsTheLandingColumn_DuringARealPointerDrag()
    {
        var page = await OpenAsync();
        await ScrollToSection(page, DelegateSection);

        var board = page.Locator("[data-testid='ai-agents-delegate']");
        var backlog = board.Locator("[data-column='backlog']");
        var planner = board.Locator("[data-column='planner']");

        Assert.AreEqual("Nothing received yet.", await TextAsync(page, "ai-agents-delegate-log"));

        await DragCardAsync(page, board, "task-summary", planner);
        await page.WaitForFunctionAsync(
            "() => document.querySelector(\"[data-testid='ai-agents-delegate'] [data-column='planner']\")?.dataset.dragOver === 'true'",
            new PageWaitForFunctionOptions { Timeout = 10000 });

        var preview = planner.Locator("[data-testid='delegate-agent-preview']");
        Assert.AreEqual("true", await preview.GetAttributeAsync("data-estimate"),
            "the projection adds the dragged card's estimate to the target");
        StringAssert.Contains(await preview.InnerTextAsync(), "with the card being moved",
            "the target states what it would be occupied until");
        Assert.AreEqual("false", await backlog.GetAttributeAsync("data-drag-over"),
            "the source column is never its own drop target");
        Assert.AreEqual(0, await planner.Locator("[data-testid='delegate-task'][data-task-id='task-summary']").CountAsync(),
            "a live preview must not move the card yet");

        // NOT ShootAsync: it moves the mouse, and the pointer is down (the card would drag to the corner).
        await ShootRawAsync(page, "ai-agents-delegate-drag-preview-light-1440");

        await page.Mouse.UpAsync();
        await page.WaitForFunctionAsync(
            "() => document.querySelector(\"[data-testid='ai-agents-delegate'] [data-column='planner'] "
            + "[data-task-id='task-summary']\") !== null",
            new PageWaitForFunctionOptions { Timeout = 10000 });
        await page.WaitForFunctionAsync(
            "() => document.querySelector(\"[data-testid='ai-agents-delegate'] [data-column='planner']\")?.dataset.dragOver === 'false'",
            new PageWaitForFunctionOptions { Timeout = 10000 });
        StringAssert.Contains(await TextAsync(page, "ai-agents-delegate-log"), "assigned to Planner");

        // The theme is switched BEFORE the next gesture: ToggleThemeAsync clicks with the mouse, and a click while the
        // drag button is still down would release the drag instead of switching the theme.
        await ToggleThemeAsync(page);
        await ScrollToSection(page, DelegateSection);

        // Edge case: the FULL column. It is marked as the target, but it must not claim a time for a card it refuses.
        var reviewer = board.Locator("[data-column='reviewer']");
        Assert.AreEqual("true", await reviewer.GetAttributeAsync("data-full"));
        await DragCardAsync(page, board, "task-invoices", reviewer);
        await page.WaitForFunctionAsync(
            "() => document.querySelector(\"[data-testid='ai-agents-delegate'] [data-column='reviewer']\")?.dataset.dragOver === 'true'",
            new PageWaitForFunctionOptions { Timeout = 10000 });
        Assert.IsFalse((await reviewer.Locator("[data-testid='delegate-agent-preview']").InnerTextAsync())
                .Contains("with the card being moved", StringComparison.Ordinal),
            "a full column never promises a time for a card it is going to refuse");
        StringAssert.Contains(await reviewer.InnerTextAsync(), "Full");

        await ShootRawAsync(page, "ai-agents-delegate-drag-full-dark-1440");

        await page.Mouse.UpAsync();
        await page.WaitForFunctionAsync(
            "() => !!document.querySelector(\"[data-testid='ai-agents-delegate'] [data-testid='delegate-notice']\")",
            new PageWaitForFunctionOptions { Timeout = 10000 });
        StringAssert.Contains(await TextAsync(page, "delegate-notice"), "not moved",
            "the refusal is painted where a reader who missed the announcement can see it");
        Assert.AreEqual(1, await board.Locator("[data-column='backlog'] [data-task-id='task-invoices']").CountAsync(),
            "a refused drop leaves the card where it was");

        // Escape cancels the gesture: the preview disappears and nothing moves.
        await DragCardAsync(page, board, "task-notes", planner);
        await page.WaitForFunctionAsync(
            "() => document.querySelector(\"[data-testid='ai-agents-delegate'] [data-column='planner']\")?.dataset.dragOver === 'true'",
            new PageWaitForFunctionOptions { Timeout = 10000 });
        await page.Keyboard.PressAsync("Escape");
        await page.WaitForFunctionAsync(
            "() => document.querySelector(\"[data-testid='ai-agents-delegate'] [data-column='planner']\")?.dataset.dragOver === 'false'",
            new PageWaitForFunctionOptions { Timeout = 10000 });
        await page.Mouse.UpAsync();
        Assert.AreEqual(1, await board.Locator("[data-column='backlog'] [data-task-id='task-notes']").CountAsync(),
            "a cancelled drag changes nothing");
        Assert.AreEqual("false", await planner.GetAttributeAsync("data-drag-over"));

        // Tablet: the same live projection at 768 px, where the columns still sit side by side.
        var tablet = await OpenAsync(768, 900);
        await ScrollToSection(tablet, DelegateSection);
        var tabletBoard = tablet.Locator("[data-testid='ai-agents-delegate']");
        await DragCardAsync(tablet, tabletBoard, "task-summary", tabletBoard.Locator("[data-column='planner']"));
        await tablet.WaitForFunctionAsync(
            "() => document.querySelector(\"[data-testid='ai-agents-delegate'] [data-column='planner']\")?.dataset.dragOver === 'true'",
            new PageWaitForFunctionOptions { Timeout = 10000 });
        StringAssert.Contains(
            await tabletBoard.Locator("[data-column='planner'] [data-testid='delegate-agent-preview']").InnerTextAsync(),
            "with the card being moved");
        await ShootRawAsync(tablet, "ai-agents-delegate-drag-preview-tablet-768");
        await tablet.Mouse.UpAsync();
    }

    /// <summary>
    /// Focuses a card's drag handle and moves the pointer into the target column's row list, WITHOUT releasing. The
    /// pointer drag needs a real <c>pointerdown</c>/<c>pointermove</c> sequence (the module owns an inline transform, not
    /// an HTML5 DataTransfer), so the button stays down here and the caller decides how the gesture ends.
    /// </summary>
    private static async Task DragCardAsync(IPage page, ILocator board, string taskId, ILocator column)
    {
        // The grip is a SIBLING of the host's card inside the row, not a descendant of it.
        var handle = board.Locator(
            $"[data-tm-sortable-item]:has([data-testid='delegate-task'][data-task-id='{taskId}']) [data-tm-sortable-handle]");
        var from = await handle.BoundingBoxAsync();
        var targetList = column.Locator("ul[data-tm-sortable-root]");
        var to = await targetList.BoundingBoxAsync();
        Assert.IsNotNull(from, $"the card {taskId} has a drag handle on screen");
        Assert.IsNotNull(to, "the target column has a row list on screen");

        // The column can be taller than the viewport (a 375 px phone stacks them), so the target is framed first.
        await targetList.ScrollIntoViewIfNeededAsync();
        to = await targetList.BoundingBoxAsync();
        from = await handle.BoundingBoxAsync();
        Assert.IsNotNull(from);
        Assert.IsNotNull(to);

        await page.Mouse.MoveAsync(from!.X + from.Width / 2, from.Y + from.Height / 2);
        await page.Mouse.DownAsync();
        // Past the 4 px threshold, still inside the source list: the module starts the gesture (and reports the source).
        await page.Mouse.MoveAsync(from.X + from.Width / 2 + 10, from.Y + from.Height / 2 + 12,
            new MouseMoveOptions { Steps = 4 });
        await page.WaitForTimeoutAsync(120);
        await page.Mouse.MoveAsync(to!.X + to.Width / 2, to.Y + Math.Min(to.Height / 2, 40),
            new MouseMoveOptions { Steps = 12 });
        await page.WaitForTimeoutAsync(120);
    }

    /// <summary>
    /// The Alt+arrow chord of <c>TmScheduledRuns</c>: it moves the focused run one window / one day, it is announced on
    /// the row, the browser's own Back/Forward shortcut never fires, and the edges of the range are announced instead of
    /// silently ignored. A full window refuses the move exactly like a drop.
    /// </summary>
    [TestMethod]
    public async Task TheScheduledRunsMoveWithTheAltArrowChord_AndTheBrowserNeverNavigates()
    {
        var page = await OpenAsync();
        await ScrollToSection(page, RunsSection);

        var runs = page.Locator("[data-testid='ai-agents-runs']");
        var exportRow = runs.Locator("[data-window-id='win-mon-am'] [data-testid='runs-run'][data-run-id='run-export']");
        Assert.AreEqual("Alt+ArrowLeft Alt+ArrowRight Alt+ArrowUp Alt+ArrowDown",
            await exportRow.GetAttributeAsync("aria-keyshortcuts"),
            "the documented key set is announced on the row it moves");

        // What the browser was told: a prevented key can never trigger the Back/Forward history navigation. The listener
        // sits in the CAPTURE phase (nothing a nested `:stopPropagation` fence does can hide the event from it) and reads
        // `defaultPrevented` on the next task, i.e. AFTER the module's own listener had its chance to cancel it.
        await page.EvaluateAsync(
            "() => { window.__tmMoveKeys = [];"
            + " document.addEventListener('keydown', event => {"
            // Only the four arrows: pressing Alt dispatches its own keydown, and that one proves nothing here.
            + "   if (event.key.indexOf('Arrow') !== 0) return;"
            + "   const entry = { key: event.key, alt: event.altKey };"
            + "   setTimeout(() => { entry.prevented = event.defaultPrevented; window.__tmMoveKeys.push(entry); }, 0);"
            + " }, true); }");
        var url = page.Url;

        // The FIRST window has nothing before it: the chord is announced, never silently swallowed.
        await FocusRunTriggerAsync(page, runs, "win-mon-am", "run-export");
        await page.Keyboard.PressAsync("Alt+ArrowLeft");
        await page.WaitForFunctionAsync(
            "() => document.querySelector(\"[data-testid='ai-agents-runs'] [role='status']\")?.textContent?.includes('first window')",
            new PageWaitForFunctionOptions { Timeout = 10000 });
        Assert.AreEqual(1, await runs.Locator("[data-window-id='win-mon-am'] [data-testid='runs-run'][data-run-id='run-export']").CountAsync(),
            "a key past the start of the range moves nothing");

        await FocusRunTriggerAsync(page, runs, "win-mon-am", "run-export");
        await page.Keyboard.PressAsync("Alt+ArrowRight");
        await page.WaitForFunctionAsync(
            "() => document.querySelector(\"[data-testid='ai-agents-runs-log']\")?.textContent?.includes('moved to')",
            new PageWaitForFunctionOptions { Timeout = 10000 });
        Assert.AreEqual(1, await runs.Locator("[data-window-id='win-mon-pm'] [data-testid='runs-run'][data-run-id='run-export']").CountAsync(),
            "Alt+ArrowRight moves the run to the next window");

        await page.WaitForFunctionAsync(
            "() => window.__tmMoveKeys.length === 2",
            new PageWaitForFunctionOptions { Timeout = 10000 });
        var keys = await page.EvaluateAsync<string[]>(
            "() => window.__tmMoveKeys.map(entry => entry.key + (entry.alt ? ':alt' : '') + (entry.prevented ? ':prevented' : ':default'))");
        CollectionAssert.AreEqual(
            new[] { "ArrowLeft:alt:prevented", "ArrowRight:alt:prevented" },
            keys,
            "both chords were cancelled before the browser could run its Back/Forward history shortcut");
        Assert.AreEqual(url, page.Url, "the page never navigated");

        await ShootAsync(page, "ai-agents-runs-move-chord-light-1440");

        // Back again, then the NEXT DAY, then a FULL window (the demo's overloaded win-tue-pm has no room at all).
        await FocusRunTriggerAsync(page, runs, "win-mon-pm", "run-export");
        await page.Keyboard.PressAsync("Alt+ArrowLeft");
        await page.WaitForFunctionAsync(
            "() => document.querySelector(\"[data-testid='ai-agents-runs'] [data-window-id='win-mon-am'] "
            + "[data-testid='runs-run'][data-run-id='run-export']\") !== null",
            new PageWaitForFunctionOptions { Timeout = 10000 });

        await FocusRunTriggerAsync(page, runs, "win-mon-am", "run-export");
        await page.Keyboard.PressAsync("Alt+ArrowDown");
        await page.WaitForFunctionAsync(
            "() => document.querySelector(\"[data-testid='ai-agents-runs'] [data-window-id='win-tue-am'] "
            + "[data-testid='runs-run'][data-run-id='run-export']\") !== null",
            new PageWaitForFunctionOptions { Timeout = 10000 });

        await FocusRunTriggerAsync(page, runs, "win-tue-am", "run-export");
        await page.Keyboard.PressAsync("Alt+ArrowRight");
        await page.WaitForFunctionAsync(
            "() => !!document.querySelector(\"[data-testid='ai-agents-runs'] [data-testid='runs-notice']\")",
            new PageWaitForFunctionOptions { Timeout = 10000 });
        StringAssert.Contains(await TextAsync(page, "runs-notice"), "has no free room",
            "a window with no room refuses the keyboard move exactly like a drop and says why");
        Assert.AreEqual(1, await runs.Locator("[data-window-id='win-tue-am'] [data-testid='runs-run'][data-run-id='run-export']").CountAsync(),
            "a refused move leaves the run where it was");
        Assert.AreEqual(
            (await runs.Locator("[data-testid='runs-announcement']").TextContentAsync())?.Trim(),
            (await runs.Locator("[data-testid='runs-notice']").TextContentAsync())?.Trim(),
            "the refusal is announced through the same one region");

        // The same chord in the dark theme, and on a 375 px phone, where the windows stack.
        await ToggleThemeAsync(page);
        await ScrollToSection(page, RunsSection);
        await FocusRunTriggerAsync(page, runs, "win-tue-am", "run-export");
        await page.Keyboard.PressAsync("Alt+ArrowUp");
        await page.WaitForFunctionAsync(
            "() => document.querySelector(\"[data-testid='ai-agents-runs'] [data-window-id='win-mon-am'] "
            + "[data-testid='runs-run'][data-run-id='run-export']\") !== null",
            new PageWaitForFunctionOptions { Timeout = 10000 });
        await ShootAsync(page, "ai-agents-runs-move-chord-dark-1440");

        var phone = await OpenAsync(375, 812, coarsePointer: true);
        await ScrollToSection(phone, RunsSection);
        var phoneRuns = phone.Locator("[data-testid='ai-agents-runs']");
        await FocusRunTriggerAsync(phone, phoneRuns, "win-mon-am", "run-export");
        await phone.Keyboard.PressAsync("Alt+ArrowRight");
        await phone.WaitForFunctionAsync(
            "() => document.querySelector(\"[data-testid='ai-agents-runs'] [data-window-id='win-mon-pm'] "
            + "[data-testid='runs-run'][data-run-id='run-export']\") !== null",
            new PageWaitForFunctionOptions { Timeout = 10000 });
        await ScrollToSection(phone, RunsSection);
        await ShootAsync(phone, "ai-agents-runs-move-chord-phone-375");
    }

    /// <summary>
    /// A screenshot that leaves the pointer and the focus exactly where they are: <see cref="ShootAsync"/> moves the mouse
    /// to the origin and blurs the active element, which would end a drag (the pointer is down and captured) or dismiss a
    /// focus-anchored popover.
    /// </summary>
    private async Task ShootRawAsync(IPage page, string name)
    {
        await page.WaitForTimeoutAsync(300);
        await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = Path.Combine(ShotDir, $"{name}.png"),
            FullPage = false,
        });
    }

    /// <summary>
    /// Focuses the MOVE MENU trigger of one run — the element a keyboard reader actually lands on inside the row, and the
    /// event target the module resolves the run from (<c>closest('[data-tm-run-move-keys]')</c>).
    /// </summary>
    private static async Task FocusRunTriggerAsync(IPage page, ILocator runs, string windowId, string runId)
    {
        var trigger = runs.Locator(
            $"[data-window-id='{windowId}'] [data-testid='runs-run'][data-run-id='{runId}'] .tm-context-menu__trigger").First;
        await trigger.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        await trigger.FocusAsync();
        // The chord is resolved from the event TARGET, so the focus has to be the row's trigger before the key is sent —
        // a key that reaches the page or another control would prove nothing.
        await page.WaitForFunctionAsync(
            "() => document.activeElement?.classList?.contains('tm-context-menu__trigger') === true"
            + " && document.activeElement?.closest('[data-tm-run-move-keys]') !== null",
            new PageWaitForFunctionOptions { Timeout = 5000 });
    }
}
