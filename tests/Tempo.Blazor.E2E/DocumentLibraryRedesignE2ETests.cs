using System.Net.Http.Json;
using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// WF4 (wireframe redesign plan ce19aa8c, phase 8): the redesigned shared <c>TmDocumentOpenDialog</c> over the live
/// Demo.Api library (WASM demo, API at 5100) — keyboard-operable tree, the opt-in "Recent" node, always-visible actions,
/// radio-card mode choice, the footer that never clips at tablet width, and named screenshots at 1440 and 1024 (touch)
/// in light/dark × default/indigo. The committed screenshots under
/// <c>__screenshots__/document-library/redesign/</c> are only rewritten with <c>TM_E2E_UPDATE_BASELINE=1</c>; otherwise
/// the captures go to the git-ignored TestResults folder.
/// </summary>
[TestClass]
public class DocumentLibraryRedesignE2ETests : WasmTestBase
{
    private const string DialogPage = "/document-open-dialog";

    private static bool UpdateBaseline =>
        string.Equals(Environment.GetEnvironmentVariable("TM_E2E_UPDATE_BASELINE"), "1", StringComparison.Ordinal);

    private static string ScreenshotDirectory => UpdateBaseline
        ? Path.Combine(RepoRoot(), "tests", "Tempo.Blazor.E2E", "__screenshots__", "document-library", "redesign")
        : Path.Combine(RepoRoot(), "tests", "Tempo.Blazor.E2E", "TestResults", "document-library-redesign");

    private static string RepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "TempoBlazor.slnx")))
            {
                return current.FullName;
            }

            current = current.Parent!;
        }

        throw new DirectoryNotFoundException("Could not locate TempoBlazor.slnx.");
    }

    private async Task<IPage> OpenPageAsync(int width, int height, bool touch = false, bool dark = false, bool indigo = false)
    {
        var context = await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = width, Height = height },
            HasTouch = touch,
            Locale = "en-US",
            IgnoreHTTPSErrors = true,
        });
        RegisterContext(context);

        var page = await context.NewPageAsync();
        await page.AddInitScriptAsync("localStorage.setItem('tm-demo-culture', 'en');"
            + (indigo ? " localStorage.setItem('tm-demo-color-theme', 'indigo');" : " localStorage.removeItem('tm-demo-color-theme');"));
        await page.GotoAsync($"{BaseUrl}{DialogPage}", new PageGotoOptions { WaitUntil = WaitUntilState.Load, Timeout = 60_000 });
        await WaitForAppReadyAsync(page);

        if (indigo)
        {
            await page.EvaluateAsync("() => document.documentElement.setAttribute('data-tm-theme', 'indigo')");
        }

        if (dark)
        {
            await page.Locator("[data-testid='theme-toggle']").First.DispatchEventAsync("click");
            await page.WaitForFunctionAsync("() => document.querySelector('[data-theme=\"dark\"]') !== null",
                null, new PageWaitForFunctionOptions { Timeout = 10_000 });
        }

        return page;
    }

    private static async Task OpenDialogAsync(IPage page)
    {
        await page.ClickAsync("[data-testid=open-dialog-btn]");
        await page.WaitForSelectorAsync(".tm-document-open-dialog",
            new PageWaitForSelectorOptions { State = WaitForSelectorState.Visible, Timeout = 20_000 });
        await page.WaitForSelectorAsync(".tm-dod-tree-node, .tm-dod-folder-row", new PageWaitForSelectorOptions { Timeout = 20_000 });
        // The modal slides in: never capture / measure mid-animation.
        await page.EvaluateAsync("async () => { await Promise.all(document.getAnimations().map(a => a.finished.catch(() => { }))); }");
    }

    private static ILocator Node(IPage page, string text) =>
        page.Locator(".tm-dod-tree-node", new() { HasTextString = text }).First;

    private static async Task WaitForActivePathAsync(IPage page, string path) =>
        await page.WaitForFunctionAsync(
            "p => document.activeElement && document.activeElement.getAttribute('data-path') === p",
            path, new PageWaitForFunctionOptions { Timeout = 5_000 });

    private async Task ShotAsync(IPage page, string name)
    {
        Directory.CreateDirectory(ScreenshotDirectory);
        await page.EvaluateAsync("async () => { await Promise.all(document.getAnimations().map(a => a.finished.catch(() => { }))); }");
        await page.Mouse.MoveAsync(0, 0);
        await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = Path.Combine(ScreenshotDirectory, name),
            Type = ScreenshotType.Png,
            Timeout = 60_000,
        });
    }

    // ── Keyboard: the tree ───────────────────────────────────────────────────────────

    [TestMethod]
    [TestCategory("WASM")]
    [Description("The folder tree is one tab stop with WAI-ARIA arrow keys: collapse/expand, parent/child, Home/End, Enter")]
    public async Task DocLib_TreeKeyboard()
    {
        var page = await OpenPageAsync(1440, 900);
        await OpenDialogAsync(page);

        Assert.AreEqual(1, await page.Locator(".tm-dod-tree-node[tabindex='0']").CountAsync(), "a tree is ONE tab stop");
        Assert.AreEqual("tree", await page.Locator(".tm-dod-tree").GetAttributeAsync("role"));

        var designs = Node(page, "Designs");
        await designs.FocusAsync();
        Assert.AreEqual("true", await designs.GetAttributeAsync("aria-expanded"));
        Assert.IsTrue(await Node(page, "Mobile").IsVisibleAsync());

        // Left on an open parent collapses it; the child leaves the tree.
        await page.Keyboard.PressAsync("ArrowLeft");
        await page.WaitForFunctionAsync(
            "() => ![...document.querySelectorAll('.tm-dod-tree-node')].some(n => n.textContent.includes('Mobile'))");
        Assert.AreEqual("false", await Node(page, "Designs").GetAttributeAsync("aria-expanded"));
        await WaitForActivePathAsync(page, "/Designs");

        // Right on a closed parent opens it, Right again steps to the first child, Left steps back to the parent.
        await page.Keyboard.PressAsync("ArrowRight");
        await Node(page, "Mobile").WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5_000 });
        await page.Keyboard.PressAsync("ArrowRight");
        await WaitForActivePathAsync(page, "/Designs/Mobile");
        await page.Keyboard.PressAsync("ArrowLeft");
        await WaitForActivePathAsync(page, "/Designs");

        // Home / End jump to the first / last visible item; Down moves one.
        await page.Keyboard.PressAsync("Home");
        await WaitForActivePathAsync(page, "/");
        await page.Keyboard.PressAsync("ArrowDown");
        await page.WaitForFunctionAsync(
            "() => document.activeElement && document.activeElement.classList.contains('tm-dod-tree-node') && document.activeElement.getAttribute('data-path') !== '/'");
        await page.Keyboard.PressAsync("End");
        await page.WaitForFunctionAsync(
            "() => { const all = [...document.querySelectorAll('.tm-dod-tree-node')]; return document.activeElement === all[all.length - 1]; }");

        // Enter navigates into the focused folder (rows appear); Space does the same on keyup (Archive is a different folder).
        await Node(page, "Designs").FocusAsync();
        await page.Keyboard.PressAsync("Enter");
        await page.WaitForSelectorAsync(".tm-dod-row", new PageWaitForSelectorOptions { Timeout = 10_000 });
        Assert.AreEqual("true", await Node(page, "Designs").GetAttributeAsync("aria-selected"));
        await Node(page, "Archive").FocusAsync();
        await page.Keyboard.PressAsync("Space");
        await page.WaitForFunctionAsync("() => document.querySelector(\".tm-dod-tree-node[data-path=\\\"/Archive\\\"]\")?.getAttribute(\"aria-selected\") === \"true\"",
            null, new PageWaitForFunctionOptions { Timeout = 10_000 });
        await Node(page, "Designs").ClickAsync();
        await page.WaitForSelectorAsync(".tm-dod-row");

        // The chevron is a mouse-only affordance: it toggles without navigating.
        var before = await page.Locator(".tm-dod-row").CountAsync();
        await Node(page, "Designs").Locator(".tm-dod-tree-toggle").ClickAsync();
        Assert.AreEqual("false", await Node(page, "Designs").GetAttributeAsync("aria-expanded"));
        Assert.AreEqual("true", await Node(page, "Designs").GetAttributeAsync("aria-selected"), "the chevron never navigates");
        Assert.AreEqual(before, await page.Locator(".tm-dod-row").CountAsync());
    }

    [TestMethod]
    [TestCategory("WASM")]
    [Description("List rows and grid cards are one roving tab stop with arrow keys; Space selects, Enter opens, Up/Down in the grid step a row")]
    public async Task DocLib_ListAndGridRovingFocus()
    {
        var page = await OpenPageAsync(1440, 900);
        await OpenDialogAsync(page);
        await Node(page, "Designs").ClickAsync();
        await page.WaitForSelectorAsync(".tm-dod-row");

        Assert.AreEqual(1, await page.Locator(".tm-dod-row[tabindex='0']").CountAsync(), "one tab stop in the list");
        var rows = await page.Locator(".tm-dod-row").CountAsync();
        Assert.IsTrue(rows >= 2);

        await page.Locator(".tm-dod-row").First.FocusAsync();
        await page.Keyboard.PressAsync("ArrowDown");
        await page.WaitForFunctionAsync(
            "() => document.activeElement === document.querySelectorAll('.tm-dod-row')[1]");
        await page.Keyboard.PressAsync("Space");
        await page.WaitForSelectorAsync(".tm-dod-row--selected");
        Assert.AreEqual(1, await page.Locator(".tm-dod-row--selected").CountAsync());
        Assert.IsFalse(await page.Locator("[data-testid=open-result]").IsVisibleAsync(), "Space only selects");
        await page.Keyboard.PressAsync("End");
        await page.WaitForFunctionAsync(
            "() => { const r = document.querySelectorAll('.tm-dod-row'); return document.activeElement === r[r.length - 1]; }");

        await page.ClickAsync(".tm-dod-view-grid");
        await page.WaitForSelectorAsync(".tm-dod-card");
        Assert.AreEqual(1, await page.Locator(".tm-dod-card[tabindex='0']").CountAsync(), "one tab stop in the grid");
        Assert.AreEqual("Documents", await page.Locator(".tm-dod-grid").GetAttributeAsync("aria-label"),
            "the listbox has its own label, not the dialog title");

        await page.Locator(".tm-dod-card").First.FocusAsync();
        await page.Keyboard.PressAsync("ArrowRight");
        await page.WaitForFunctionAsync("() => document.activeElement === document.querySelectorAll('.tm-dod-card')[1]");
        await page.Keyboard.PressAsync("ArrowLeft");
        await page.WaitForFunctionAsync("() => document.activeElement === document.querySelectorAll('.tm-dod-card')[0]");
        // Down steps by the measured column count; past the last row nothing happens (no clamp), so a single row keeps the focus.
        var columns = await page.EvaluateAsync<int>(
            "() => { const c = [...document.querySelectorAll('.tm-dod-card')]; return c.filter(x => x.offsetTop === c[0].offsetTop).length; }");
        await page.Keyboard.PressAsync("ArrowDown");
        var expected = columns < await page.Locator(".tm-dod-card").CountAsync() ? columns : 0;
        await page.WaitForFunctionAsync($"() => document.activeElement === document.querySelectorAll('.tm-dod-card')[{expected}]");
    }

    // ── Recent (opt-in) ────────────────────────────────────────────────────────────────

    [TestMethod]
    [TestCategory("WASM")]
    [Description("ShowRecent adds a Recent node that lists the provider's newest documents first; off by default")]
    public async Task DocLib_RecentNode()
    {
        // The demo store follows the FolderPath contract (null => root), so seed two root documents with a known order.
        var older = "Recent-old-" + Guid.NewGuid().ToString("N")[..6];
        var newer = "Recent-new-" + Guid.NewGuid().ToString("N")[..6];
        var created = new List<Guid>();
        using (var http = DemoApiTestHttp.CreateClient())
        {
            foreach (var name in new[] { older, newer })
            {
                var resp = await http.PostAsJsonAsync(
                    "https://localhost:5100/api/document-library/wireframe/documents",
                    new { name, folderPath = "/", payloadJson = "{}", author = "E2E" });
                resp.EnsureSuccessStatusCode();
                created.Add((await resp.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>()).GetProperty("id").GetGuid());
                await Task.Delay(30);
            }
        }

        try
        {
            await AssertRecentNodeAsync(older, newer);
        }
        finally
        {
            // The API store is shared by every document-library spec: leave it as found.
            using var http = DemoApiTestHttp.CreateClient();
            await http.PostAsJsonAsync("https://localhost:5100/api/document-library/wireframe/documents/delete", new { ids = created });
        }
    }

    private async Task AssertRecentNodeAsync(string older, string newer)
    {

        var page = await OpenPageAsync(1440, 900);
        await OpenDialogAsync(page);
        Assert.AreEqual(0, await page.Locator(".tm-dod-tree-node--recent").CountAsync(), "Recent is opt-in");

        await page.CloseAsync();
        page = await OpenPageAsync(1440, 900);
        await page.CheckAsync("[data-testid=show-recent]");
        await OpenDialogAsync(page);

        var recent = page.Locator(".tm-dod-tree-node--recent");
        Assert.AreEqual(1, await recent.CountAsync());
        StringAssert.Contains(await recent.InnerTextAsync(), "Recent");
        await recent.ClickAsync();

        await page.WaitForFunctionAsync(
            $"() => [...document.querySelectorAll('.tm-dod-row')].some(r => r.dataset.name === '{newer}')",
            null, new PageWaitForFunctionOptions { Timeout = 15_000 });
        var names = await page.Locator(".tm-dod-row").EvaluateAllAsync<string[]>("rows => rows.map(r => r.dataset.name)");
        Assert.IsTrue(Array.IndexOf(names, newer) < Array.IndexOf(names, older), $"newest first: {string.Join(", ", names)}");
        Assert.AreEqual("true", await recent.GetAttributeAsync("aria-selected"));
        Assert.IsTrue(await page.Locator(".tm-dod-col-name").IsDisabledAsync(), "Recent fixes the order");
        StringAssert.Contains(await page.InnerTextAsync(".tm-dod-breadcrumb"), "Recent");

        // Leaving the node restores the folder query and the sortable header.
        await Node(page, "Designs").ClickAsync();
        await page.WaitForFunctionAsync("() => !document.querySelector('.tm-dod-col-name')?.disabled && document.querySelectorAll('.tm-dod-row').length > 0");
        Assert.AreEqual("ascending", await page.Locator(".tm-dod-col-name").EvaluateAsync<string>("e => e.parentElement.getAttribute('aria-sort')"));
    }

    // ── Actions and mode cards ──────────────────────────────────────────────────────────

    [TestMethod]
    [TestCategory("WASM")]
    [Description("Rename/Delete are always rendered and disabled until a document is selected; mode options are radio cards with their own hint; the radio group is keyboard-operable")]
    public async Task DocLib_ActionsAndModeCards()
    {
        var page = await OpenPageAsync(1440, 900);
        await OpenDialogAsync(page);

        Assert.IsTrue(await page.Locator(".tm-dod-rename").IsDisabledAsync());
        Assert.IsTrue(await page.Locator(".tm-dod-delete").IsDisabledAsync());
        Assert.IsTrue(await page.Locator(".tm-dod-open").IsDisabledAsync());
        await Node(page, "Designs").ClickAsync();
        await page.Locator(".tm-dod-row").First.ClickAsync();
        Assert.IsFalse(await page.Locator(".tm-dod-rename").IsDisabledAsync());
        Assert.IsFalse(await page.Locator(".tm-dod-delete").IsDisabledAsync());
        Assert.IsFalse(await page.Locator(".tm-dod-open").IsDisabledAsync());

        Assert.AreEqual("radiogroup", await page.Locator(".tm-dod-mode").GetAttributeAsync("role"));
        Assert.AreEqual(2, await page.Locator(".tm-dod-mode-card").CountAsync());
        Assert.AreEqual(2, await page.Locator(".tm-dod-mode-card .tm-dod-mode-hint").CountAsync(), "each card carries its hint");
        Assert.IsTrue(await page.Locator(".tm-dod-mode-link").IsCheckedAsync(), "Link is the default");

        // Arrow keys move through the native radio group; the selected card follows.
        await page.Locator(".tm-dod-mode-link").FocusAsync();
        await page.Keyboard.PressAsync("ArrowDown");
        await page.WaitForSelectorAsync(".tm-dod-mode-card--selected .tm-dod-mode-copy");
        Assert.IsTrue(await page.Locator(".tm-dod-mode-copy").IsCheckedAsync());

        // Open delivers the chosen mode.
        await page.Locator(".tm-dod-open").ClickAsync();
        await page.WaitForSelectorAsync("[data-testid=result-mode]");
        Assert.AreEqual("Copy", await page.InnerTextAsync("[data-testid=result-mode]"));
    }

    [TestMethod]
    [TestCategory("WASM")]
    [Description("Edge: Escape closes the dialog even while a tree item has focus (the keyboard handlers do not swallow it)")]
    public async Task DocLib_Escape_ClosesTheDialog()
    {
        var page = await OpenPageAsync(1440, 900);
        await OpenDialogAsync(page);
        await Node(page, "Designs").FocusAsync();

        await page.Keyboard.PressAsync("Escape");
        await page.WaitForFunctionAsync("() => !document.querySelector('.tm-document-open-dialog')",
            null, new PageWaitForFunctionOptions { Timeout = 10_000 });
    }

    // ── Tablet: the footer never clips ──────────────────────────────────────────────────

    [TestMethod]
    [TestCategory("WASM")]
    [Description("At tablet width (1024 touch) and in a forced tablet layout the narrow tree and the footer on its own row keep Cancel / Open fully visible")]
    public async Task DocLib_Tablet_FooterButtonsAreNeverClipped()
    {
        var page = await OpenPageAsync(1024, 768, touch: true);
        await page.SelectOptionAsync("[data-testid=layout-select]", "Tablet");
        await OpenDialogAsync(page);

        Assert.AreEqual("tablet", await page.Locator(".tm-dod").GetAttributeAsync("data-layout"));
        Assert.IsTrue(await page.Locator(".tm-dod-tree--narrow").CountAsync() == 1);
        foreach (var selector in new[] { ".tm-dod-cancel", ".tm-dod-open" })
        {
            var box = await page.Locator(selector).BoundingBoxAsync();
            Assert.IsNotNull(box);
            Assert.IsTrue(box!.X >= 0 && box.X + box.Width <= 1024, $"{selector} is inside the viewport (x={box.X}, w={box.Width})");
            Assert.IsTrue(box.Width >= 64, $"{selector} keeps its natural width ({box.Width}px)");
            Assert.IsTrue(box.Height >= 40, $"{selector} is touch-sized ({box.Height}px)");
        }

        // The buttons sit BELOW the mode cards (own row), not squeezed beside them.
        var cards = await page.Locator(".tm-dod-mode").BoundingBoxAsync();
        var open = await page.Locator(".tm-dod-open").BoundingBoxAsync();
        Assert.IsTrue(open!.Y >= cards!.Y + cards.Height - 1, "Cancel / Open are on their own row");

        // Touch targets: toolbar actions and rows reach 44 px on a coarse pointer.
        var coarse = await page.EvaluateAsync<bool>("() => matchMedia('(pointer: coarse)').matches");
        if (coarse)
        {
            Assert.IsTrue((await page.Locator(".tm-dod-new-folder").BoundingBoxAsync())!.Height >= 43);
        }
    }

    [TestMethod]
    [TestCategory("WASM")]
    [Description("Edge (review hint): at a narrow viewport the footer collapses without clipping Open - every footer control stays inside the viewport (below 640 px of dialog body the dialog is the phase-9 mobile panel: folder rows instead of the tree)")]
    public async Task DocLib_NarrowViewport_FooterNeverClipsOpen()
    {
        foreach (var width in new[] { 700, 600 })
        {
            var page = await OpenPageAsync(width, 800);
            await OpenDialogAsync(page);
            // The layout decides how a folder is entered: a tree item (tablet / desktop) or a drill-down row (mobile).
            await page.Locator(".tm-dod-tree-node, .tm-dod-folder-row").Filter(new() { HasTextString = "Designs" }).First.ClickAsync();
            await page.Locator(".tm-dod-row").First.WaitForAsync();
            await page.Locator(".tm-dod-row").First.ClickAsync();

            // The mobile panel has no Cancel button (its header close button cancels); every control that exists must stay inside.
            var mobile = await page.Locator(".tm-dod").GetAttributeAsync("data-layout") == "mobile";
            foreach (var selector in mobile ? new[] { ".tm-dod-open" } : new[] { ".tm-dod-cancel", ".tm-dod-open" })
            {
                var box = await page.Locator(selector).BoundingBoxAsync();
                Assert.IsNotNull(box, $"{selector} at {width}px");
                Assert.IsTrue(box!.X >= 0 && box.X + box.Width <= width + 0.5, $"{selector} inside the {width}px viewport (x={box.X}, w={box.Width})");
                Assert.IsTrue(box.Width >= 64, $"{selector} keeps its natural width at {width}px ({box.Width}px)");
            }

            Assert.AreEqual("false", await page.Locator(".tm-dod-open").GetAttributeAsync("aria-disabled") ?? "false");
            Assert.IsFalse(await page.Locator(".tm-dod-open").IsDisabledAsync());
            await page.CloseAsync();
        }
    }
    // ── Screenshots (UX review against the references) ────────────────────────────────────

    [TestMethod]
    [TestCategory("WASM")]
    [Description("Desktop 1440: list, grid, Recent, delete confirm in light/dark × default/indigo")]
    public async Task DocLibShot_Desktop1440()
    {
        foreach (var dark in new[] { false, true })
        {
            foreach (var indigo in new[] { false, true })
            {
                var tag = (dark ? "dark" : "light") + "-" + (indigo ? "indigo" : "default");
                var page = await OpenPageAsync(1440, 900, dark: dark, indigo: indigo);
                await page.CheckAsync("[data-testid=show-recent]");
                await OpenDialogAsync(page);

                await Node(page, "Designs").ClickAsync();
                await page.WaitForSelectorAsync(".tm-dod-row");
                await page.Locator(".tm-dod-row").First.ClickAsync();
                await ShotAsync(page, $"desktop-1440-{tag}-list.png");

                await page.ClickAsync(".tm-dod-view-grid");
                await page.WaitForSelectorAsync(".tm-dod-card");
                await ShotAsync(page, $"desktop-1440-{tag}-grid.png");

                await page.ClickAsync(".tm-dod-view-list");
                await page.Locator(".tm-dod-tree-node--recent").ClickAsync();
                await page.WaitForFunctionAsync("() => document.querySelector('.tm-dod-tree-node--recent[aria-selected=true]')");
                await ShotAsync(page, $"desktop-1440-{tag}-recent.png");

                await page.CloseAsync();
            }
        }
    }

    [TestMethod]
    [TestCategory("WASM")]
    [Description("Tablet 1024 touch: list and grid in light/dark × default/indigo (Auto layout: the 900 px modal body stays on the wide layout), plus the forced Tablet layout")]
    public async Task DocLibShot_Tablet1024Touch()
    {
        foreach (var dark in new[] { false, true })
        {
            foreach (var indigo in new[] { false, true })
            {
                var tag = (dark ? "dark" : "light") + "-" + (indigo ? "indigo" : "default");
                var page = await OpenPageAsync(1024, 768, touch: true, dark: dark, indigo: indigo);
                await OpenDialogAsync(page);

                await Node(page, "Designs").ClickAsync();
                await page.WaitForSelectorAsync(".tm-dod-row");
                await page.Locator(".tm-dod-row").First.ClickAsync();
                await ShotAsync(page, $"tablet-1024-touch-{tag}-list.png");

                await page.ClickAsync(".tm-dod-view-grid");
                await page.WaitForSelectorAsync(".tm-dod-card");
                await page.WaitForSelectorAsync(".tm-dod-card");
                await ShotAsync(page, $"tablet-1024-touch-{tag}-grid.png");

                await page.CloseAsync();

                // The forced tablet layout (narrow tree, footer on its own row) is what a narrower container resolves to.
                page = await OpenPageAsync(1024, 768, touch: true, dark: dark, indigo: indigo);
                await page.SelectOptionAsync("[data-testid=layout-select]", "Tablet");
                await OpenDialogAsync(page);
                await Node(page, "Designs").ClickAsync();
                await page.WaitForSelectorAsync(".tm-dod-row");
                await page.Locator(".tm-dod-row").First.ClickAsync();
                await ShotAsync(page, $"tablet-1024-touch-{tag}-forced-tablet.png");
                await page.CloseAsync();
            }
        }
    }
}
