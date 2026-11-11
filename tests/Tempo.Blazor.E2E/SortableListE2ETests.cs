using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Plan 3e96f6d8 phase 1 — <c>TmSortableList</c> in a real browser. The assertions read behaviour rather
/// than markup: the list is a <c>role="list"</c> of <c>sortable item</c> rows, the keyboard path picks a row
/// up, moves it and drops it with the live region narrating the step, the POINTER path rides the real
/// <c>pointer-drag.js</c> module and commits exactly one move, the per-row menu is the tap alternative, a
/// disabled list keeps its rows but loses every reorder path, Tab cancels a grab instead of trapping the
/// focus, the focus indicator is an inset outline, reduced motion clamps the row transition, dark mode
/// re-points the row surface, the list stays inside a 390 px viewport and a scoped axe scan of the section
/// finds no critical or serious violation. Reference screenshots (light, dark, phone) land in
/// <c>TestResults/sortable-list</c>.
/// </summary>
public abstract class SortableListE2EBase : PlaywrightTestBase
{
    private const string SectionSelector = "[data-testid='sortable-basic-section']";

    private string ShotDir
        => Path.Combine(FindRepoRoot(), "tests", "Tempo.Blazor.E2E", "TestResults", "sortable-list");

    [TestInitialize]
    public void EnsureShotDir() => Directory.CreateDirectory(ShotDir);

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TempoBlazor.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root was not found.");
    }

    private static ILocator Section(IPage page) => page.Locator(SectionSelector);

    private static ILocator List(IPage page) => Section(page).Locator("[data-testid='sortable-basic']");

    private static ILocator Rows(IPage page) => List(page).Locator(".tm-sortable-list__item");

    private static async Task<string[]> OrderAsync(IPage page)
    {
        var items = await page.Locator("[data-testid='sortable-order-item']").AllTextContentsAsync();
        return [.. items.Select(item => item.Trim())];
    }

    private static ILocator LastMove(IPage page) => page.Locator("[data-testid='sortable-last-move']");

    private async Task<IPage> OpenAsync(int width = 1440, int height = 1000, bool reduceMotion = false,
        bool coarsePointer = false)
    {
        var context = await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = width, Height = height },
            Locale = "en-US",
            ReducedMotion = reduceMotion ? ReducedMotion.Reduce : ReducedMotion.NoPreference,
            IgnoreHTTPSErrors = true,
            // A phone is a coarse pointer: the media query that grows the handle to the touch target
            // only matches when the context says so.
            HasTouch = coarsePointer,
        });
        await context.AddInitScriptAsync(
            "localStorage.setItem('tm-demo-culture', 'en'); document.cookie = 'tm-demo-culture=en;path=/;max-age=31536000;samesite=lax';");
        RegisterContext(context);

        var page = await context.NewPageAsync();
        await GotoAppReadyAsync(page, $"{BaseUrl}/sortable-list");
        await page.WaitForSelectorAsync(
            $"{SectionSelector} [data-testid='sortable-basic'] [role='list']",
            new PageWaitForSelectorOptions { Timeout = 30000 });
        await page.WaitForTimeoutAsync(400);
        return page;
    }

    [TestMethod]
    public async Task TheListExposesItsAccessibleModel()
    {
        var page = await OpenAsync();

        var list = List(page).Locator("[role='list']");
        Assert.AreEqual(1, await list.CountAsync());
        Assert.AreEqual("Plan steps", await list.GetAttributeAsync("aria-label"));
        Assert.AreEqual(4, await Rows(page).CountAsync());

        var describedBy = await list.GetAttributeAsync("aria-describedby");
        Assert.IsFalse(string.IsNullOrEmpty(describedBy), "the list explains its keyboard model");
        StringAssert.Contains(await page.Locator($"#{describedBy}").TextContentAsync() ?? string.Empty, "Enter or Space");

        var roleDescriptions = await Rows(page).EvaluateAllAsync<string[]>(
            "els => els.map(el => el.getAttribute('aria-roledescription'))");
        CollectionAssert.AreEqual(
            new[] { "sortable item", "sortable item", "sortable item", "sortable item" }, roleDescriptions);

        // One focus stop per row; the decorative handle is never a second one.
        var tabIndexes = await Rows(page).EvaluateAllAsync<string[]>(
            "els => els.map(el => el.getAttribute('tabindex'))");
        CollectionAssert.AreEqual(new[] { "0", "0", "0", "0" }, tabIndexes);

        var handle = List(page).Locator(".tm-sortable-list__handle").First;
        Assert.AreEqual("-1", await handle.GetAttributeAsync("tabindex"));
        Assert.AreEqual("true", await handle.GetAttributeAsync("aria-hidden"));

        Assert.AreEqual(1, await List(page).Locator("[role='status']").CountAsync(),
            "one polite live region for the whole list, never one per row");

        await ShootAsync(page, "sortable-list-light");
    }

    [TestMethod]
    public async Task KeyboardMove_ReordersAndAnnouncesEveryStep()
    {
        var page = await OpenAsync();

        Assert.AreEqual("Draft the outline", (await OrderAsync(page))[0]);

        var row = Rows(page).First;
        await row.FocusAsync();
        await page.Keyboard.PressAsync("Enter");
        await page.WaitForTimeoutAsync(150);

        Assert.AreEqual("true", await Rows(page).First.GetAttributeAsync("data-grabbed"));
        StringAssert.Contains(
            await List(page).Locator("[role='status']").TextContentAsync() ?? string.Empty,
            "Draft the outline picked up");

        await page.Keyboard.PressAsync("ArrowDown");
        await page.WaitForTimeoutAsync(150);
        StringAssert.Contains(
            await List(page).Locator("[role='status']").TextContentAsync() ?? string.Empty,
            "moved to position 2 of 4");

        await page.Keyboard.PressAsync("Enter");
        await page.WaitForTimeoutAsync(300);

        var order = await OrderAsync(page);
        Assert.AreEqual("Confirm the sources", order[0]);
        Assert.AreEqual("Draft the outline", order[1]);
        Assert.AreEqual("Keyboard", await LastMove(page).GetAttributeAsync("data-source"));
        StringAssert.Contains(await LastMove(page).TextContentAsync() ?? string.Empty, "position 1 → position 2");
        Assert.AreEqual("false", await Rows(page).First.GetAttributeAsync("data-grabbed"));
    }

    [TestMethod]
    public async Task EscapeRollsTheGrabBack()
    {
        var page = await OpenAsync();

        var row = Rows(page).First;
        await row.FocusAsync();
        await page.Keyboard.PressAsync("Enter");
        await page.Keyboard.PressAsync("ArrowDown");
        await page.WaitForTimeoutAsync(150);
        await page.Keyboard.PressAsync("Escape");
        await page.WaitForTimeoutAsync(200);

        Assert.AreEqual("Draft the outline", (await OrderAsync(page))[0], "the host's order is untouched");
        Assert.AreEqual("none", await LastMove(page).GetAttributeAsync("data-source"));
        StringAssert.Contains(
            await List(page).Locator("[role='status']").TextContentAsync() ?? string.Empty, "Move cancelled");
    }

    [TestMethod]
    public async Task TabReleasesTheGrabInsteadOfTrappingFocus()
    {
        var page = await OpenAsync();

        var row = Rows(page).First;
        await row.FocusAsync();
        await page.Keyboard.PressAsync("Enter");
        await page.WaitForTimeoutAsync(150);
        Assert.AreEqual("true", await Rows(page).First.GetAttributeAsync("data-grabbed"));

        await page.Keyboard.PressAsync("ArrowDown");
        await page.WaitForTimeoutAsync(150);
        Assert.AreEqual(1, await List(page).Locator("[data-grabbed='true']").CountAsync(),
            "the grabbed row is painted in the pending order");

        await page.Keyboard.PressAsync("Tab");
        await page.WaitForTimeoutAsync(250);

        Assert.AreEqual(0, await List(page).Locator("[data-grabbed='true']").CountAsync(),
            "Tab cancels the grab instead of being swallowed by the row");
        Assert.AreEqual("Draft the outline", (await OrderAsync(page))[0], "the pending order is rolled back");
        StringAssert.Contains(
            await List(page).Locator("[role='status']").TextContentAsync() ?? string.Empty, "Move cancelled");

        // And the browser's own Tab ran: the focus is no longer on the grabbed row. A swallowed Tab here
        // would be the keyboard trap WCAG 2.1.2 forbids.
        var focusedIsRow = await Rows(page).First.EvaluateAsync<bool>("el => document.activeElement === el");
        Assert.IsFalse(focusedIsRow, "Tab must move the focus out of the row");
    }

    [TestMethod]
    public async Task PointerDrag_CommitsExactlyOneReorder()
    {
        var page = await OpenAsync();

        var handle = List(page).Locator(".tm-sortable-list__handle").First;
        var handleBox = await handle.BoundingBoxAsync();
        var targetRow = await Rows(page).Nth(2).BoundingBoxAsync();
        Assert.IsNotNull(handleBox);
        Assert.IsNotNull(targetRow);

        var startX = handleBox!.X + handleBox.Width / 2;
        var startY = handleBox.Y + handleBox.Height / 2;
        // Aim BELOW the centre of the third row: passing a row's centre is what trades places with it,
        // and a pointer sitting exactly ON the centre is a tie (it has not passed anything).
        var endY = targetRow!.Y + targetRow.Height * 0.75;

        await page.Mouse.MoveAsync(startX, startY);
        await page.Mouse.DownAsync();
        // Several steps: the module needs a 4 px threshold, and every step re-hit-tests the landing slot.
        for (var step = 1; step <= 8; step++)
        {
            await page.Mouse.MoveAsync(startX, (float)(startY + (endY - startY) * step / 8.0));
            await page.WaitForTimeoutAsync(30);
        }

        await page.Mouse.UpAsync();
        await page.WaitForTimeoutAsync(400);

        var order = await OrderAsync(page);
        Assert.AreEqual("Confirm the sources", order[0], "the dragged row left the first slot");
        Assert.AreEqual("Draft the outline", order[2], "and landed where the pointer dropped it");
        Assert.AreEqual("Pointer", await LastMove(page).GetAttributeAsync("data-source"));

        // The module must restore every inline style it painted, or a later render would show stale offsets.
        var transforms = await Rows(page).EvaluateAllAsync<string[]>(
            "els => els.map(el => el.style.transform)");
        CollectionAssert.AreEqual(new[] { "", "", "", "" }, transforms);
    }

    [TestMethod]
    public async Task TheRowMenu_IsTheTapAlternative()
    {
        var page = await OpenAsync();

        await List(page).Locator(".tm-context-menu__trigger").First.ClickAsync();
        await page.WaitForSelectorAsync("[role='menu'] [role='menuitem']");
        await page.Locator("[role='menu'] [role='menuitem']", new PageLocatorOptions { HasTextString = "Move to bottom" })
            .ClickAsync();
        await page.WaitForTimeoutAsync(400);

        var order = await OrderAsync(page);
        Assert.AreEqual("Draft the outline", order[3], "the menu moved the row to the end");
        Assert.AreEqual("Menu", await LastMove(page).GetAttributeAsync("data-source"));
    }

    [TestMethod]
    public async Task WholeItemModeHasNoHandle_AndTheDisabledListHasNoReorderPath()
    {
        var page = await OpenAsync();

        var whole = page.Locator("[data-testid='sortable-whole-item']");
        Assert.AreEqual(0, await whole.Locator(".tm-sortable-list__handle").CountAsync(),
            "the whole row is the drag surface, so there is no separate handle");
        Assert.AreEqual(0, await whole.Locator(".tm-sortable-list__move").CountAsync(),
            "the demo turned the move menu off for this list");
        Assert.AreEqual(3, await whole.Locator("[data-tm-sortable-grip]").CountAsync(),
            "every whole-item row is its own grip");

        var readOnly = page.Locator("[data-testid='sortable-readonly']");
        Assert.AreEqual("true", await readOnly.GetAttributeAsync("data-disabled"));
        Assert.AreEqual(3, await readOnly.Locator(".tm-sortable-list__item").CountAsync(),
            "a read-only list still shows its rows");
        Assert.AreEqual(0, await readOnly.Locator(".tm-sortable-list__handle").CountAsync(),
            "and drops the grip, which would be a false affordance");
        Assert.AreEqual(0, await readOnly.Locator("[tabindex='0']").CountAsync(),
            "and offers no focus stop, so nothing invites a reorder");
        Assert.AreEqual(0, await readOnly.Locator(".tm-sortable-list__move").CountAsync());
    }

    [TestMethod]
    public async Task TheFocusedRowCarriesAnInsetOutline()
    {
        var page = await OpenAsync();

        await Rows(page).First.FocusAsync();
        await page.Keyboard.PressAsync("Enter");
        await page.WaitForTimeoutAsync(150);

        var outline = await Rows(page).First.EvaluateAsync<string>(
            """
            el => {
                const style = getComputedStyle(el);
                return `${style.outlineStyle} ${style.outlineWidth} ${style.outlineOffset}`;
            }
            """);

        Assert.AreEqual("solid 2px -2px", outline,
            "the ring is INSET: a list inside a scroller would clip an outward ring exactly where it is needed");
    }

    [TestMethod]
    public async Task ReducedMotion_ClampsTheRowTransition()
    {
        var page = await OpenAsync(reduceMotion: true);

        var duration = await Rows(page).First.EvaluateAsync<string>(
            "el => getComputedStyle(el).transitionDuration");
        var first = duration.Split(',')[0].Trim();
        var seconds = first.EndsWith("ms", StringComparison.Ordinal)
            ? double.Parse(first[..^2], System.Globalization.CultureInfo.InvariantCulture) / 1000
            : double.Parse(first.TrimEnd('s'), System.Globalization.CultureInfo.InvariantCulture);

        Assert.IsTrue(seconds <= 0.001, $"prefers-reduced-motion must clamp the row transition (got {duration})");
    }

    [TestMethod]
    public async Task DarkTheme_RepointsTheRowSurface()
    {
        var page = await OpenAsync();

        var light = await Rows(page).First.EvaluateAsync<string>("el => getComputedStyle(el).backgroundColor");

        // The demo's OWN toggle: it flips the shell's `dark` class AND the `data-theme` attribute, so the
        // whole frame repaints (setting data-theme alone leaves the demo's Tailwind `dark:` variants
        // behind and the row text unreadable — a fake dark mode proves nothing).
        await page.Locator("[data-testid='theme-toggle']:visible").First.ClickAsync();
        await page.WaitForTimeoutAsync(600);

        var dark = await Rows(page).First.EvaluateAsync<string>("el => getComputedStyle(el).backgroundColor");
        Assert.AreEqual("rgb(30, 41, 59)", dark, "the dark theme re-points --tm-bg-surface (#1e293b)");
        Assert.AreNotEqual(light, dark, "the row really re-painted for the dark theme");

        await ShootAsync(page, "sortable-list-dark");
    }

    [TestMethod]
    public async Task PhoneWidth_KeepsTheListInsideTheViewport()
    {
        var page = await OpenAsync(width: 390, height: 844, coarsePointer: true);

        var overflow = await page.EvaluateAsync<double>(
            "() => document.documentElement.scrollWidth - window.innerWidth");
        Assert.IsTrue(overflow <= 1, $"a 390 px phone must not scroll the page sideways (overflow {overflow}px)");

        var box = await List(page).BoundingBoxAsync();
        Assert.IsNotNull(box);
        Assert.IsTrue(box!.X >= -1 && box.X + box.Width <= 391,
            $"the list stays inside the 390 px viewport (x {box.X}, width {box.Width})");

        // A coarse pointer must still be able to hit the handle, so the CSS grows it to the touch target.
        var handleSize = await List(page).Locator(".tm-sortable-list__handle").First.EvaluateAsync<double[]>(
            "el => [el.getBoundingClientRect().width, el.getBoundingClientRect().height]");
        Assert.IsTrue(handleSize[0] >= 44 && handleSize[1] >= 44,
            $"a coarse pointer needs the 44 px touch target on the grip (got {handleSize[0]}x{handleSize[1]})");

        // The move menu is the only reorder path a finger has without a hold, so it grows too.
        var moveSize = await List(page).Locator(".tm-sortable-list__move-trigger").First.EvaluateAsync<double[]>(
            "el => [el.getBoundingClientRect().width, el.getBoundingClientRect().height]");
        Assert.IsTrue(moveSize[0] >= 44 && moveSize[1] >= 44,
            $"the move trigger needs the 44 px touch target too (got {moveSize[0]}x{moveSize[1]})");

        await ShootAsync(page, "sortable-list-phone");
    }

    [TestMethod]
    public async Task ScopedAxeScan_HasNoCriticalOrSeriousViolations()
    {
        var page = await OpenAsync();
        await page.AddScriptTagAsync(new PageAddScriptTagOptions
        {
            Url = "https://cdnjs.cloudflare.com/ajax/libs/axe-core/4.10.2/axe.min.js"
        });

        var violations = await page.EvaluateAsync<string[]>(
            """
            async (selector) => {
                const host = document.querySelector(selector);
                const result = await axe.run(host, {
                    runOnly: { type: 'tag', values: ['wcag2a', 'wcag2aa'] },
                    resultTypes: ['violations']
                });
                return result.violations
                    .filter(v => v.impact === 'critical' || v.impact === 'serious')
                    .map(v => `${v.impact}: ${v.id} - ${v.help} (${v.nodes.map(n => n.target.join(' ')).slice(0, 4).join('; ')})`);
            }
            """, SectionSelector);

        Assert.AreEqual(0, violations.Length, "axe: " + string.Join(" | ", violations));
    }

    private async Task ShootAsync(IPage page, string name)
    {
        // Scroll the section into view BEFORE the shot: a viewport screenshot of the top of the page
        // would prove nothing about this component.
        await Section(page).ScrollIntoViewIfNeededAsync();
        await page.WaitForTimeoutAsync(400);
        await page.Mouse.MoveAsync(0, 0);
        await page.EvaluateAsync(
            "() => document.activeElement instanceof HTMLElement && document.activeElement.blur()");
        await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = Path.Combine(ShotDir, $"{name}.png"),
            FullPage = false,
        });
    }
}

/// <summary>The WASM lane (the demo default).</summary>
[TestClass]
[TestCategory("WASM")]
public class SortableListE2ETests : SortableListE2EBase
{
    protected override string BaseUrl =>
        Environment.GetEnvironmentVariable("TM_E2E_WASM_URL") ?? "https://localhost:7106";
}

/// <summary>
/// The Server lane (plan 7ba95ed6, Fáze 11): the same <c>/sortable-list</c> contract over a real Blazor
/// Server circuit. The keyboard grab/move/drop path, the Tab release that keeps the focus out of a trap, the
/// real <c>pointer-drag.js</c> gesture with its single commit, the per-row move menu, the inset focus outline,
/// the reduced-motion clamp, the dark repaint through the demo's own toggle and the phone geometry are all
/// inherited, so a circuit-specific focus, interop or timing regression fails here instead of hiding behind a
/// green WASM run.
/// </summary>
[TestClass]
[TestCategory("Server")]
public class SortableListServerE2ETests : SortableListE2EBase
{
    protected override string BaseUrl => "https://localhost:7107";
}

/// <summary>
/// The InteractiveAuto lane: the same list on the host that renders over the Server circuit first and then
/// hands the page to the WebAssembly runtime. Every walk is inherited and therefore also covers the handover
/// between the two runtimes.
/// </summary>
[TestClass]
[TestCategory("InteractiveAuto")]
public class SortableListInteractiveAutoE2ETests : SortableListE2EBase
{
    protected override string BaseUrl => "https://localhost:7108";
}
