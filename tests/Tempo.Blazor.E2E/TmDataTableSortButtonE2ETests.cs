using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// E2E for the <c>.tm-th-sort</c> sort button contract, run in BOTH engines the library supports:
/// the accessible name a screen reader announces (including the next-action suffix), single-path
/// keyboard activation, Shift+Enter multi-sort, the header-text drag that creates a group, and the
/// library-owned hover/focus styling — the pieces that cannot regress behind a passing bUnit
/// render because they live in real layout, a real accessibility tree and a real cascade.
/// The component-side parameter coverage (<c>SortLabel</c>, fallbacks, the DEBUG throw) is bUnit's
/// job in <c>TmDataTableSortButtonNameTests</c>; here the browser proves the name resolves, Enter
/// sorts once, Shift+Enter multi-sorts, a header drag groups — in Chromium AND in Firefox, because
/// an accessibility-tree answer that is only true in one engine is not an answer.
/// </summary>
[TestClass]
public class TmDataTableSortButtonE2ETests : WasmTestBase
{
    private const string DataTablePage = "/data-table";

    [TestMethod]
    [TestCategory("WASM")]
    public async Task SortButton_AccessibleName_Keyboard_And_HoverFocus_Chromium()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        await OpenDataTableAsync(page);
        await AssertSortButtonContractAsync(page);
    }

    [TestMethod]
    [TestCategory("WASM")]
    public async Task SortButton_AccessibleName_Keyboard_And_HoverFocus_Firefox()
    {
        // The shared browser of the base class is Chromium; Firefox is launched per-test so the
        // same contract runs against the second engine the matrix claims.
        await using var firefox = await NewFirefoxAsync();
        await OpenDataTableAsync(firefox.Page);
        await AssertSortButtonContractAsync(firefox.Page);
    }

    [TestMethod]
    [TestCategory("WASM")]
    public async Task SortButton_ShiftEnter_MultiSort_Chromium()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        await OpenDataTableAsync(page);
        await AssertShiftEnterMultiSortAsync(page);
    }

    [TestMethod]
    [TestCategory("WASM")]
    public async Task SortButton_ShiftEnter_MultiSort_Firefox()
    {
        await using var firefox = await NewFirefoxAsync();
        await OpenDataTableAsync(firefox.Page);
        await AssertShiftEnterMultiSortAsync(firefox.Page);
    }

    [TestMethod]
    [TestCategory("WASM")]
    public async Task GroupableHeader_DragFromHeaderText_Groups_Chromium()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        await OpenDataTableAsync(page);
        await AssertHeaderDragGroupsAsync(page);
    }

    [TestMethod]
    [TestCategory("WASM")]
    public async Task GroupableHeader_DragFromHeaderText_Groups_Firefox()
    {
        await using var firefox = await NewFirefoxAsync();
        await OpenDataTableAsync(firefox.Page);
        await AssertHeaderDragGroupsAsync(firefox.Page);
    }

    private async Task OpenDataTableAsync(IPage page)
    {
        await page.SetViewportSizeAsync(1440, 1100);
        await page.GotoAsync($"{BaseUrl}{DataTablePage}",
            new PageGotoOptions { WaitUntil = WaitUntilState.Load, Timeout = 60000 });
        await WaitForAppReadyAsync(page);
        await page.Locator("[data-testid='dt-ergonomics-section']").WaitForAsync(
            new LocatorWaitForOptions { Timeout = 30000 });
    }

    /// <summary>The per-test Firefox chain — disposed with the test.</summary>
    private sealed class FirefoxSession : IAsyncDisposable
    {
        public required IPlaywright Playwright { get; init; }
        public required IBrowser Browser { get; init; }
        public required IBrowserContext Context { get; init; }
        public required IPage Page { get; init; }

        public async ValueTask DisposeAsync()
        {
            try { await Context.DisposeAsync(); } catch { /* teardown */ }
            try { await Browser.DisposeAsync(); } catch { /* teardown */ }
            try { Playwright.Dispose(); } catch { /* teardown */ }
        }
    }

    private static async Task<FirefoxSession> NewFirefoxAsync()
    {
        var playwright = await Microsoft.Playwright.Playwright.CreateAsync();
        var browser = await playwright.Firefox.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true,
        });
        var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = 1440, Height = 1100 },
            Locale = "en-US",
            IgnoreHTTPSErrors = true,
        });
        return new FirefoxSession
        {
            Playwright = playwright,
            Browser = browser,
            Context = context,
            Page = await context.NewPageAsync(),
        };
    }

    /// <summary>
    /// N87 was raised as a question — whether a <c>&lt;button&gt;</c> inside a
    /// <c>draggable</c> <c>&lt;th&gt;</c> starts a drag in Firefox — and it is answered by
    /// measurement, not assumption: this test drags from the button's centre (the header's text,
    /// where the user actually grabs) into the group zone and requires the group chip to appear.
    /// The run's diagnostic output (whether dragstart fired and on which element) is what the
    /// commit message records as the measurement.
    /// </summary>
    private async Task AssertHeaderDragGroupsAsync(IPage page)
    {
        // The page ships two grouping zones — one pre-grouped (row-attributes demo, "Dept ×") and
        // one empty (the persons table, placeholder "Drag column headers here to group"). Scope by
        // the placeholder: the empty zone is the one a drag can still create a group in.
        var table = page.Locator(".tm-data-table-wrapper").Filter(
            new LocatorFilterOptions { Has = page.Locator(".tm-data-table-group-placeholder") });
        var zone = table.Locator(".tm-data-table-group-zone");
        await zone.WaitForAsync(new LocatorWaitForOptions { Timeout = 30000 });

        // Resolve the zone to an ELEMENT HANDLE before the gesture: a successful drop swaps the
        // placeholder for chips, which makes the placeholder-filtered locator match nothing —
        // the element itself survives Blazor's in-place patching, so the handle stays valid.
        var zoneElement = await zone.ElementHandleAsync();
        Assert.IsNotNull(zoneElement, "the empty group drop zone must be on screen");

        var header = table.Locator("th.tm-col-groupable").First;
        await header.ScrollIntoViewIfNeededAsync();

        var button = header.Locator("button.tm-th-sort");

        // Instrument BEFORE the gesture: capture whether a dragstart fires and on which element —
        // the assertion below is the contract, this log is the N87 measurement itself.
        await page.EvaluateAsync(
            """
            () => {
                window.__tmDragLog = [];
                for (const ev of ['dragstart', 'dragenter', 'drop', 'dragend']) {
                    document.addEventListener(ev, e => {
                        const t = e.target;
                        window.__tmDragLog.push(ev + '@' + t.tagName + '[' + (t.className || '') + ']');
                    }, true);
                }
            }
            """);

        var src = await button.BoundingBoxAsync();
        var dst = await zone.BoundingBoxAsync();
        Assert.IsNotNull(src, "the sort button inside a groupable header must be on screen");
        Assert.IsNotNull(dst, "the group drop zone must be on screen");

        await page.Mouse.MoveAsync(src!.X + (src.Width / 2), src.Y + (src.Height / 2));
        await page.Mouse.DownAsync();
        // Move in small steps — a single jump can land outside every intermediate element and a
        // real user's drag does not teleport.
        await page.Mouse.MoveAsync(dst!.X + (dst.Width / 2), dst.Y + (dst.Height / 2),
            new MouseMoveOptions { Steps = 30 });
        await page.Mouse.UpAsync();

        var dragLog = await page.EvaluateAsync<string[]>("() => window.__tmDragLog");
        TestContext.WriteLine(
            $"[N87 measurement] drag events: [{(dragLog is null || dragLog.Length == 0 ? "none" : string.Join(" | ", dragLog))}]");

        await page.WaitForFunctionAsync(
            "zone => zone.querySelector('.tm-data-table-group-chip') !== null",
            zoneElement,
            new PageWaitForFunctionOptions { Timeout = 10000 });
    }

    /// <summary>
    /// Shift+Enter is the keyboard multi-sort path: focus each sort button and press Shift+Enter —
    /// the second column must join the sort rather than replace the first, so BOTH headers carry
    /// a live <c>aria-sort</c>.
    /// </summary>
    private static async Task AssertShiftEnterMultiSortAsync(IPage page)
    {
        var section = page.Locator("[data-testid='dt-ergonomics-section']");
        var sortableHeaders = section.Locator("th[data-sortable='true']");
        var buttons = section.Locator("button.tm-th-sort");
        Assert.IsTrue(await buttons.CountAsync() >= 2,
            "the ergonomics table needs at least two sortable columns for a multi-sort claim");

        // Column 1: plain Enter — sole ascending sort.
        await buttons.Nth(0).FocusAsync();
        await buttons.Nth(0).PressAsync("Enter");
        await Assertions.Expect(sortableHeaders.Nth(0)).ToHaveAttributeAsync("aria-sort", "ascending");

        // Column 2: Shift+Enter — adds to the sort instead of replacing it.
        await buttons.Nth(1).FocusAsync();
        await buttons.Nth(1).PressAsync("Shift+Enter");

        await Assertions.Expect(sortableHeaders.Nth(0)).ToHaveAttributeAsync("aria-sort", "ascending");
        await Assertions.Expect(sortableHeaders.Nth(1)).ToHaveAttributeAsync("aria-sort", "ascending");

        // And the spoken name tracks the cycle: the first column's next activation now DESCENDS.
        var name = await buttons.Nth(0).GetAttributeAsync("aria-label");
        Assert.IsTrue(name?.Contains("descending", StringComparison.OrdinalIgnoreCase) == true
                      || name?.Contains("sestupně", StringComparison.OrdinalIgnoreCase) == true
                      || name?.Contains("décroissant", StringComparison.OrdinalIgnoreCase) == true,
            $"the sorted column's name must announce the next action — descending — not stay static; got '{name}'");
    }

    private static async Task AssertSortButtonContractAsync(IPage page)
    {
        var section = page.Locator("[data-testid='dt-ergonomics-section']");
        var buttons = section.Locator("button.tm-th-sort");
        var count = await buttons.CountAsync();
        Assert.IsTrue(count > 0, "the ergonomics table renders sortable columns");

        // ── Accessible name: every sort button resolves a non-empty name in the AX tree ──
        for (var i = 0; i < count; i++)
        {
            var label = await buttons.Nth(i).GetAttributeAsync("aria-label");
            Assert.IsFalse(string.IsNullOrWhiteSpace(label),
                $"sort button #{i} must announce a name — an aria-hidden icon alone says nothing");
            var named = section.GetByRole(AriaRole.Button,
                new LocatorGetByRoleOptions { Name = label, Exact = true });
            Assert.IsTrue(await named.CountAsync() >= 1,
                $"accessible name '{label}' must resolve through the browser's AX tree");
        }

        // ── Hover: the label takes whatever colour the header is painting, no repaint ──
        // `color: inherit` on :hover means the unsorted header still brightens via the th's own
        // `.tm-col-sortable:hover` rule — and a SORTED header keeps its accent instead of snapping
        // back to the resting ink.
        var first = buttons.First;
        var restColor = await first.EvaluateAsync<string>("el => getComputedStyle(el).color");
        await first.HoverAsync();
        var hoverColor = await first.EvaluateAsync<string>("el => getComputedStyle(el).color");
        var expectedHover = await page.EvaluateAsync<string>(
            """
            () => {
                const probe = document.createElement('span');
                probe.style.color = 'var(--tm-text-primary)';
                document.body.appendChild(probe);
                const value = getComputedStyle(probe).color;
                probe.remove();
                return value;
            }
            """);
        Assert.AreEqual(expectedHover, hoverColor,
            "hovering an unsorted sort button inherits the th's own hover colour — --tm-text-primary");
        Assert.AreNotEqual(restColor, hoverColor,
            "the hover colour must differ from the resting colour, otherwise the rule paints nothing");

        // ── Sorted accent survives hover: sort column 1, then hover its button ──
        var header = section.Locator("th[data-sortable='true']").First;
        await first.FocusAsync();
        await first.PressAsync("Enter");
        await Assertions.Expect(header).ToHaveAttributeAsync("aria-sort", "ascending");

        var sortedRest = await first.EvaluateAsync<string>("el => getComputedStyle(el).color");
        await first.HoverAsync();
        var sortedHover = await first.EvaluateAsync<string>("el => getComputedStyle(el).color");
        var sortedAccent = await page.EvaluateAsync<string>(
            """
            () => {
                const probe = document.createElement('span');
                probe.style.color = 'var(--tm-color-primary-text)';
                document.body.appendChild(probe);
                const value = getComputedStyle(probe).color;
                probe.remove();
                return value;
            }
            """);
        Assert.AreEqual(sortedAccent, sortedRest,
            "a sorted column's label paints --tm-color-primary-text — the sorted accent");
        Assert.AreEqual(sortedAccent, sortedHover,
            "hovering a SORTED sort button must keep the sorted accent — repainting it to the " +
            "plain header ink is the N89 bug this test now forbids");

        // ── Focus ring: the library ships its own :focus-visible indicator (WCAG 2.4.7) ──
        // Real keyboard focus is what :focus-visible matches, so walk Tab to the button rather
        // than scripting .focus() — programmatic focus is not guaranteed keyboard modality.
        var reachedSortButton = false;
        for (var tab = 0; tab < 30 && !reachedSortButton; tab++)
        {
            await page.Keyboard.PressAsync("Tab");
            reachedSortButton = await page.EvaluateAsync<bool>(
                "() => document.activeElement && document.activeElement.classList.contains('tm-th-sort')");
        }

        Assert.IsTrue(reachedSortButton,
            "Tab navigation must land on a .tm-th-sort button within 30 presses");

        var ring = await page.EvaluateAsync<string[]>(
            """
            () => {
                const el = document.activeElement;
                const cs = getComputedStyle(el);
                return [cs.outlineStyle, cs.outlineWidth, cs.outlineOffset,
                        String(el.matches(':focus-visible'))];
            }
            """);
        Assert.AreEqual("true", ring[3],
            "a sort button reached by Tab must match :focus-visible — that is the state the ring is painted for");
        Assert.AreEqual("solid", ring[0],
            "the focused sort button paints the library's focus ring, not the platform default of 'none'");
        Assert.AreEqual("2px", ring[1]);
        Assert.AreEqual("2px", ring[2],
            "the ring is drawn OUTSIDE the box (outline-offset: 2px) — a negative offset painted it " +
            "over the label's glyphs, which is the second half of the N89 bug");
    }
}
