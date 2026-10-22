using System.Globalization;
using System.Net.Http.Json;
using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// WF4 phase 9 (wireframe redesign plan ce19aa8c): the mobile layout of the shared <c>TmDocumentOpenDialog</c> over the live
/// Demo.Api library (WASM demo) at 390x844 and 320x640 with touch - the fullscreen panel, drill-down folder rows, the back
/// button, two-line document rows with a preview, the row menu, the footer action bar, the Link / Copy cards, the footer above an
/// on-screen keyboard, no horizontal scroll - plus the regression of the desktop / tablet dialog. Named screenshots go to the git-ignored
/// TestResults folder unless <c>TM_E2E_UPDATE_BASELINE=1</c> rewrites the committed set under
/// <c>__screenshots__/document-library/mobile/</c>.
/// </summary>
[TestClass]
public class DocumentLibraryMobileE2ETests : WasmTestBase
{
    private const string DialogPage = "/document-open-dialog";

    private static bool UpdateBaseline =>
        string.Equals(Environment.GetEnvironmentVariable("TM_E2E_UPDATE_BASELINE"), "1", StringComparison.Ordinal);

    private static string ScreenshotDirectory => UpdateBaseline
        ? Path.Combine(RepoRoot(), "tests", "Tempo.Blazor.E2E", "__screenshots__", "document-library", "mobile")
        : Path.Combine(RepoRoot(), "tests", "Tempo.Blazor.E2E", "TestResults", "document-library-mobile");

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

    private async Task<IPage> OpenPageAsync(int width, int height, bool dark = false, bool touch = true, string culture = "en", string layout = "Auto")
    {
        var context = await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = width, Height = height },
            HasTouch = touch,
            IsMobile = touch && width < 700,
            Locale = culture == "en" ? "en-US" : culture,
            IgnoreHTTPSErrors = true,
        });
        RegisterContext(context);

        var page = await context.NewPageAsync();
        await page.AddInitScriptAsync($"localStorage.setItem('tm-demo-culture', '{culture}'); localStorage.removeItem('tm-demo-color-theme');");
        await page.GotoAsync($"{BaseUrl}{DialogPage}", new PageGotoOptions { WaitUntil = WaitUntilState.Load, Timeout = 60_000 });
        await WaitForAppReadyAsync(page);
        if (dark)
        {
            await page.Locator("[data-testid='theme-toggle']").First.DispatchEventAsync("click");
            await page.WaitForFunctionAsync("() => document.querySelector('[data-theme=\"dark\"]') !== null",
                null, new PageWaitForFunctionOptions { Timeout = 10_000 });
        }

        if (layout != "Auto")
        {
            await page.SelectOptionAsync("[data-testid=layout-select]", layout);
        }

        return page;
    }

    private static async Task OpenDialogAsync(IPage page)
    {
        await page.Locator("[data-testid=open-dialog-btn]").DispatchEventAsync("click");
        await page.WaitForSelectorAsync(".tm-document-open-dialog",
            new PageWaitForSelectorOptions { State = WaitForSelectorState.Visible, Timeout = 20_000 });
        await page.WaitForSelectorAsync(".tm-dod-folder-row, .tm-dod-row, .tm-dod-tree-node", new PageWaitForSelectorOptions { Timeout = 20_000 });
        await SettleAsync(page);
    }

    private static async Task SettleAsync(IPage page) =>
        await page.EvaluateAsync("async () => { await Promise.all(document.getAnimations().map(a => a.finished.catch(() => { }))); }");

    private static ILocator FolderRow(IPage page, string name) =>
        page.Locator($".tm-dod-folder-row[data-path$='/{name}']").First;

    private static ILocator Row(IPage page, string name) =>
        page.Locator($".tm-dod-row[data-name='{name}']").First;

    private static async Task DrillAsync(IPage page, string folder)
    {
        await FolderRow(page, folder).TapAsync();
        await page.WaitForFunctionAsync($"() => document.querySelector('.tm-dod-crumb--current')?.textContent.includes('{folder}')",
            null, new PageWaitForFunctionOptions { Timeout = 10_000 });
        await page.WaitForSelectorAsync(".tm-dod-row, .tm-dod-folder-row, .tm-dod-empty", new PageWaitForSelectorOptions { Timeout = 10_000 });
    }

    private async Task ShotAsync(IPage page, string name)
    {
        Directory.CreateDirectory(ScreenshotDirectory);
        await SettleAsync(page);
        await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = Path.Combine(ScreenshotDirectory, name),
            Type = ScreenshotType.Png,
            Timeout = 60_000,
        });
    }

    private static async Task AssertNoHorizontalScrollAsync(IPage page, string where)
    {
        var overflow = await page.EvaluateAsync<double[]>(
            @"() => {
                const doc = document.documentElement;
                const body = document.querySelector('.tm-modal-body');
                const content = document.querySelector('.tm-dod-content');
                return [doc.scrollWidth - doc.clientWidth, body ? body.scrollWidth - body.clientWidth : 0, content ? content.scrollWidth - content.clientWidth : 0];
            }");
        Assert.IsTrue(overflow[0] <= 1, $"{where}: the page scrolls horizontally by {overflow[0]}px");
        Assert.IsTrue(overflow[1] <= 1, $"{where}: the modal body scrolls horizontally by {overflow[1]}px");
        Assert.IsTrue(overflow[2] <= 1, $"{where}: the content scrolls horizontally by {overflow[2]}px");
    }

    private static async Task AssertInViewportAsync(IPage page, string selector, int width, int height, string what)
    {
        var box = await page.Locator(selector).First.BoundingBoxAsync();
        Assert.IsNotNull(box, $"{what} must be rendered");
        Assert.IsTrue(box!.X >= -0.5 && box.X + box.Width <= width + 0.5, $"{what} stays inside the {width}px viewport (x={box.X}, w={box.Width})");
        Assert.IsTrue(box.Y >= -0.5 && box.Y + box.Height <= height + 0.5, $"{what} stays inside the {height}px viewport (y={box.Y}, h={box.Height})");
    }

    // ── Presentation ──────────────────────────────────────────────────────────

    [TestMethod]
    [TestCategory("WASM")]
    [DataRow(390, 844)]
    [DataRow(320, 640)]
    [Description("Phase 9: at 390 and 320 the dialog is a fullscreen panel (no tree), the footer is inside the viewport, no horizontal scroll")]
    public async Task DocLibMobile_FullscreenPanel_StickyFooter_NoHorizontalScroll(int width, int height)
    {
        var page = await OpenPageAsync(width, height);
        await OpenDialogAsync(page);

        Assert.AreEqual("mobile", await page.Locator(".tm-dod").GetAttributeAsync("data-layout"));
        Assert.AreEqual(0, await page.Locator(".tm-dod-tree").CountAsync(), "no tree on mobile");
        var panel = await page.Locator(".tm-modal").BoundingBoxAsync();
        Assert.IsTrue(panel!.Width >= width - 1 && panel.Height >= height - 1, $"the panel fills the viewport ({panel.Width}x{panel.Height})");
        await AssertInViewportAsync(page, ".tm-modal-footer", width, height, "the footer");
        await AssertInViewportAsync(page, ".tm-dod-open", width, height, "Open");
        await AssertInViewportAsync(page, ".tm-dod-mode", width, height, "the Link / Copy cards");
        await AssertNoHorizontalScrollAsync(page, $"{width}px root");

        await DrillAsync(page, "Designs");
        await AssertNoHorizontalScrollAsync(page, $"{width}px in Designs");
        await ShotAsync(page, $"mobile-{width}-designs-list.png");
    }

    [TestMethod]
    [TestCategory("WASM")]
    [DataRow(390, 844)]
    [DataRow(320, 640)]
    [Description("Phase 9: drill into a folder, select a document, switch to Copy, Open - the result is delivered with the chosen mode")]
    public async Task DocLibMobile_DrillDown_Select_Copy_Open(int width, int height)
    {
        var page = await OpenPageAsync(width, height);
        await OpenDialogAsync(page);

        await DrillAsync(page, "Designs");
        Assert.IsTrue(await page.Locator(".tm-dod-folder-row").CountAsync() >= 1, "Mobile is a subfolder of Designs");
        await DrillAsync(page, "Mobile");
        await page.Locator(".tm-dod-back").TapAsync();
        await page.WaitForFunctionAsync("() => document.querySelector('.tm-dod-crumb--current')?.textContent.includes('Designs')");

        await Row(page, "Home page").TapAsync();
        Assert.AreEqual("true", await Row(page, "Home page").GetAttributeAsync("aria-selected"));
        Assert.IsFalse(await page.Locator(".tm-dod-open").IsDisabledAsync());

        await page.Locator(".tm-dod-mode-card", new() { HasTextString = "Insert a copy" }).TapAsync();
        await page.Locator(".tm-dod-open").TapAsync();

        await page.WaitForSelectorAsync("[data-testid=open-result]");
        Assert.AreEqual("Home page", await page.InnerTextAsync("[data-testid=result-name]"));
        Assert.AreEqual("Copy", await page.InnerTextAsync("[data-testid=result-mode]"));
    }

    [TestMethod]
    [TestCategory("WASM")]
    [Description("Phase 9: the breadcrumb jumps straight back to the library root from two levels down; the back button is gone there")]
    public async Task DocLibMobile_Breadcrumb_JumpsToRoot()
    {
        var page = await OpenPageAsync(390, 844);
        await OpenDialogAsync(page);
        await DrillAsync(page, "Designs");
        await DrillAsync(page, "Mobile");

        await page.Locator(".tm-dod-breadcrumb .tm-dod-crumb[aria-label='Home']").TapAsync();

        await page.WaitForFunctionAsync("() => document.querySelectorAll('.tm-dod-folder-row').length >= 2 && !document.querySelector('.tm-dod-back')");
        Assert.AreEqual(0, await page.Locator(".tm-dod-back").CountAsync());
    }

    // ── Rows ───────────────────────────────────────────────────────────────────

    [TestMethod]
    [TestCategory("WASM")]
    [Description("Phase 9: a document row is two lines (name; date · author) with a preview thumbnail and a >= 44 px row menu")]
    public async Task DocLibMobile_DocumentRow_TwoLines_Preview_AndTouchTargets()
    {
        var page = await OpenPageAsync(390, 844);
        await OpenDialogAsync(page);
        await DrillAsync(page, "Designs");

        var row = Row(page, "Home page");
        Assert.AreEqual("Home page", (await row.Locator(".tm-dod-row-name").InnerTextAsync()).Trim());
        StringAssert.Matches((await row.Locator(".tm-dod-row-meta").InnerTextAsync()).Trim(), new System.Text.RegularExpressions.Regex(@"^\d{4}-\d{2}-\d{2} · \S+"));
        Assert.AreEqual(1, await row.Locator(".tm-dod-row-preview svg").CountAsync(), "the preview thumbnail");
        var name = await row.Locator(".tm-dod-row-name").BoundingBoxAsync();
        var meta = await row.Locator(".tm-dod-row-meta").BoundingBoxAsync();
        Assert.IsTrue(meta!.Y >= name!.Y + name.Height - 1, "the meta is the SECOND line");

        var menu = await row.Locator(".tm-dod-row-menu button").BoundingBoxAsync();
        Assert.IsTrue(menu!.Width >= 43.5 && menu.Height >= 43.5, $"the row menu is a 44px target ({menu.Width}x{menu.Height})");
        var rowBox = await row.BoundingBoxAsync();
        Assert.IsTrue(rowBox!.Height >= 56, $"two-line rows are at least 56px tall ({rowBox.Height})");
        foreach (var selector in new[] { ".tm-dod-folder-row", ".tm-dod-back" })
        {
            var control = page.Locator(selector).First;
            if (await control.CountAsync() > 0)
            {
                var box = await control.BoundingBoxAsync();
                Assert.IsTrue(box!.Height >= 43.5, $"{selector} is a 44px target ({box.Height})");
            }
        }
    }

    [TestMethod]
    [TestCategory("WASM")]
    [DataRow(390, 844)]
    [DataRow(320, 640)]
    [Description("Phase 9: rename through the row menu (sheet) - the row becomes the selection, the inline bar opens, the new name lands")]
    public async Task DocLibMobile_RenameThroughRowMenu(int width, int height)
    {
        var original = "Mobile-" + Guid.NewGuid().ToString("N")[..6];
        using (var http = DemoApiTestHttp.CreateClient())
        {
            var resp = await http.PostAsJsonAsync("https://localhost:5100/api/document-library/wireframe/documents",
                new { name = original, folderPath = "/Archive", payloadJson = "{}", author = "E2E" });
            resp.EnsureSuccessStatusCode();
        }

        var renamed = "Renamed-" + Guid.NewGuid().ToString("N")[..6];
        try
        {
            var page = await OpenPageAsync(width, height);
            await OpenDialogAsync(page);
            await DrillAsync(page, "Archive");
            await Row(page, original).WaitForAsync();

            await Row(page, original).Locator(".tm-dod-row-menu button").TapAsync();
            var sheet = page.Locator(".tm-overlay-panel-sheet");
            await sheet.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10_000 });
            await SettleAsync(page);
            await ShotAsync(page, $"mobile-{width}-row-menu-sheet.png");
            await sheet.Locator(".tm-dropdown-item", new() { HasTextString = "Rename" }).TapAsync();

            await page.WaitForSelectorAsync(".tm-dod-rename-input");
            Assert.AreEqual(original, await page.Locator(".tm-dod-rename-input").InputValueAsync());
            await AssertInViewportAsync(page, ".tm-dod-rename-bar", width, height, "the rename bar");
            await page.Locator(".tm-dod-rename-input").FillAsync(renamed);
            await ShotAsync(page, $"mobile-{width}-rename-bar.png");
            await page.Locator(".tm-dod-rename-confirm").TapAsync();

            await page.WaitForFunctionAsync($"() => [...document.querySelectorAll('.tm-dod-row')].some(r => r.dataset.name === '{renamed}')");
            await AssertNoHorizontalScrollAsync(page, $"{width}px after rename");
        }
        finally
        {
            await DeleteByNameAsync("/Archive", original, renamed);
        }
    }

    [TestMethod]
    [TestCategory("WASM")]
    [Description("Phase 9: New folder and Delete through the footer action bar - inline bars are usable at 320, the delete confirmation names the document")]
    public async Task DocLibMobile_ActionBar_NewFolder_And_Delete_At320()
    {
        var sacrificial = "Sacrificial-" + Guid.NewGuid().ToString("N")[..6];
        using (var http = DemoApiTestHttp.CreateClient())
        {
            (await http.PostAsJsonAsync("https://localhost:5100/api/document-library/wireframe/documents",
                new { name = sacrificial, folderPath = "/Archive", payloadJson = "{}", author = "E2E" })).EnsureSuccessStatusCode();
        }

        try
        {
            var page = await OpenPageAsync(320, 640);
            await OpenDialogAsync(page);
            await DrillAsync(page, "Archive");
            await Row(page, sacrificial).WaitForAsync();

            Assert.IsTrue(await page.Locator("[data-action-id=rename]").IsDisabledAsync(), "disabled without a selection");
            Assert.IsTrue(await page.Locator("[data-action-id=delete]").IsDisabledAsync());

            // New folder: the bar sits in the sticky head, ABOVE the list, fully inside the 320px viewport.
            var folderName = "E2E-" + Guid.NewGuid().ToString("N")[..6];
            await page.Locator("[data-action-id=new-folder]").TapAsync();
            await page.WaitForSelectorAsync(".tm-dod-new-folder-input");
            await AssertInViewportAsync(page, ".tm-dod-new-folder-bar", 320, 640, "the new-folder bar");
            await page.Locator(".tm-dod-new-folder-input").FillAsync(folderName);
            await ShotAsync(page, "mobile-320-new-folder-bar.png");
            await page.Locator(".tm-dod-new-folder-confirm").TapAsync();
            await page.WaitForFunctionAsync($"() => [...document.querySelectorAll('.tm-dod-folder-row')].some(r => r.dataset.path.endsWith('/{folderName}'))");

            // Delete: select the row, the bar's Delete asks, the confirmation names the document.
            await Row(page, sacrificial).TapAsync();
            await page.Locator("[data-action-id=delete]").TapAsync();
            await page.WaitForSelectorAsync(".tm-dod-delete-confirm");
            StringAssert.Contains(await page.Locator(".tm-dod-delete-confirm").InnerTextAsync(), sacrificial);
            await AssertInViewportAsync(page, ".tm-dod-delete-confirm", 320, 640, "the delete confirmation");
            await ShotAsync(page, "mobile-320-delete-confirm.png");
            await page.Locator(".tm-dod-delete-confirm-ok").TapAsync();
            await page.WaitForFunctionAsync($"() => ![...document.querySelectorAll('.tm-dod-row')].some(r => r.dataset.name === '{sacrificial}')");

            await DeleteFolderAsync(folderName);
        }
        finally
        {
            await DeleteByNameAsync("/Archive", sacrificial);
        }
    }

    // ── Keyboard ───────────────────────────────────────────────────────────────

    [TestMethod]
    [TestCategory("WASM")]
    [DataRow(390, 844)]
    [DataRow(320, 640)]
    [Description("Phase 9: with the on-screen keyboard open (a shrunken visualViewport) the footer - the action bar and Open - stays inside the VISIBLE viewport")]
    public async Task DocLibMobile_FooterStaysAboveTheOnScreenKeyboard(int width, int height)
    {
        var page = await OpenPageAsync(width, height);
        await OpenDialogAsync(page);
        await DrillAsync(page, "Designs");
        await Row(page, "Home page").TapAsync();

        // Playwright cannot open a real keyboard: install a visual viewport that is 260px shorter, as the sheet E2E does.
        var visible = height - 260;
        await page.EvaluateAsync($@"() => {{
            const viewport = {{ height: {visible.ToString(CultureInfo.InvariantCulture)}, offsetTop: 0, width: {width.ToString(CultureInfo.InvariantCulture)} }};
            viewport.listeners = [];
            viewport.addEventListener = (type, fn) => {{ viewport.listeners.push(fn); }};
            viewport.removeEventListener = () => {{}};
            Object.defineProperty(window, 'visualViewport', {{ configurable: true, get: () => viewport }});
            window.__tmViewport = viewport;
        }}");
        // Re-attach the lift: close and reopen the dialog under the fake keyboard (the module reads the viewport when it attaches).
        await page.Keyboard.PressAsync("Escape");
        await page.WaitForFunctionAsync("() => !document.querySelector('.tm-document-open-dialog')");
        await OpenDialogAsync(page);
        await DrillAsync(page, "Designs");
        // A row below the visible part of the (now shorter) list is scrolled to first - a finger can only tap what it can see.
        await Row(page, "Home page").ScrollIntoViewIfNeededAsync();
        await Row(page, "Home page").TapAsync();
        Assert.AreEqual(1, await page.Locator(".tm-document-open-dialog").CountAsync(), "the dialog stays open with the keyboard up");
        await AssertInViewportAsync(page, ".tm-dod-open", width, visible, "Open above the keyboard");
        await AssertInViewportAsync(page, ".tm-mobile-action-bar__bar", width, visible, "the action bar above the keyboard");
        Assert.IsTrue(await page.Locator(".tm-modal-overlay.tm-dod-keyboard").CountAsync() == 1, "the overlay is flagged while the keyboard is up");
        await ShotAsync(page, $"mobile-{width}-keyboard-open.png");
    }

    [TestMethod]
    [TestCategory("WASM")]
    [Description("Phase 9 edge: Escape closes the fullscreen panel and returns focus to the opener")]
    public async Task DocLibMobile_Escape_ClosesTheDialog_AndRestoresFocus()
    {
        var page = await OpenPageAsync(390, 844);
        await page.Locator("[data-testid=open-dialog-btn]").FocusAsync();
        await OpenDialogAsync(page);

        await page.Keyboard.PressAsync("Escape");

        await page.WaitForFunctionAsync("() => !document.querySelector('.tm-document-open-dialog')");
        Assert.AreEqual("open-dialog-btn", await page.EvaluateAsync<string>("() => document.activeElement?.getAttribute('data-testid') ?? ''"));
    }

    // ── Edge cases ─────────────────────────────────────────────────────────────

    [TestMethod]
    [TestCategory("WASM")]
    [Description("Phase 9 edge (fr, 320): the long French labels do not push the footer out of the viewport or clip Open")]
    public async Task DocLibMobile_French_320_NothingClips()
    {
        var page = await OpenPageAsync(320, 640, culture: "fr");
        await OpenDialogAsync(page);
        await DrillAsync(page, "Designs");
        await Row(page, "Home page").TapAsync();

        await AssertInViewportAsync(page, ".tm-dod-open", 320, 640, "Ouvrir");
        await AssertInViewportAsync(page, ".tm-mobile-action-bar__bar", 320, 640, "the action bar");
        await AssertNoHorizontalScrollAsync(page, "fr 320");
        var open = await page.Locator(".tm-dod-open").BoundingBoxAsync();
        Assert.IsTrue(open!.Width >= 64, $"Open keeps a usable width ({open.Width}px)");
        await ShotAsync(page, "mobile-320-fr.png");
    }

    [TestMethod]
    [TestCategory("WASM")]
    [Description("Phase 9 edge: the grid view on mobile is two columns of cards, with the folder rows above")]
    public async Task DocLibMobile_GridView_TwoColumns()
    {
        var page = await OpenPageAsync(390, 844);
        await OpenDialogAsync(page);
        await DrillAsync(page, "Designs");

        await page.Locator(".tm-dod-view-grid").TapAsync();
        await page.WaitForSelectorAsync(".tm-dod-card");

        var columns = await page.EvaluateAsync<int>("() => { const c = [...document.querySelectorAll('.tm-dod-card')]; return c.filter(x => x.offsetTop === c[0].offsetTop).length; }");
        Assert.AreEqual(2, columns);
        await AssertNoHorizontalScrollAsync(page, "grid 390");
        await ShotAsync(page, "mobile-390-grid.png");
    }

    [TestMethod]
    [TestCategory("WASM")]
    [Description("Phase 9 edge: dark theme screenshots at 390 (root list and a folder)")]
    public async Task DocLibMobileShot_Dark390()
    {
        var page = await OpenPageAsync(390, 844, dark: true);
        await OpenDialogAsync(page);
        await ShotAsync(page, "mobile-390-dark-root.png");
        await DrillAsync(page, "Designs");
        await Row(page, "Home page").TapAsync();
        await ShotAsync(page, "mobile-390-dark-designs.png");
    }

    [TestMethod]
    [TestCategory("WASM")]
    [Description("Phase 9 screenshots: the root and a folder at 390 and 320 (light)")]
    public async Task DocLibMobileShot_Light()
    {
        foreach (var (width, height) in new[] { (390, 844), (320, 640) })
        {
            var page = await OpenPageAsync(width, height);
            await OpenDialogAsync(page);
            await ShotAsync(page, $"mobile-{width}-root.png");
            await DrillAsync(page, "Designs");
            await Row(page, "Home page").TapAsync();
            await ShotAsync(page, $"mobile-{width}-designs-selected.png");
            await page.CloseAsync();
        }
    }

    // ── Regression: desktop and tablet stay the XLarge dialog ──────────────────

    [TestMethod]
    [TestCategory("WASM")]
    [Description("Regression: at 1440 the dialog is still the XLarge dialog with the tree - no mobile row, no action bar, no folder rows")]
    public async Task DocLibMobile_Desktop1440_Unchanged()
    {
        var page = await OpenPageAsync(1440, 900, touch: false);
        await OpenDialogAsync(page);

        Assert.AreEqual("desktop", await page.Locator(".tm-dod").GetAttributeAsync("data-layout"));
        Assert.AreEqual(1, await page.Locator(".tm-dod-tree").CountAsync());
        Assert.AreEqual(0, await page.Locator(".tm-dod-folder-row").CountAsync());
        Assert.AreEqual(0, await page.Locator(".tm-mobile-action-bar").CountAsync());
        Assert.AreEqual(1, await page.Locator(".tm-dod-cancel").CountAsync());
        Assert.AreEqual(0, await page.Locator(".tm-modal--fullscreen-presentation").CountAsync());
    }

    [TestMethod]
    [TestCategory("WASM")]
    [Description("Regression: at 1024 touch the forced Tablet layout is still the dialog with the narrow tree - and a forced Tablet on a PHONE-width viewport is a dialog too")]
    public async Task DocLibMobile_Tablet_StaysADialog()
    {
        var page = await OpenPageAsync(1024, 768, layout: "Tablet");
        await OpenDialogAsync(page);
        Assert.AreEqual("tablet", await page.Locator(".tm-dod").GetAttributeAsync("data-layout"));
        Assert.AreEqual(1, await page.Locator(".tm-dod-tree--narrow").CountAsync());
        Assert.AreEqual(0, await page.Locator(".tm-modal--fullscreen-presentation").CountAsync());

        var phone = await OpenPageAsync(390, 844, layout: "Tablet");
        await OpenDialogAsync(phone);
        Assert.AreEqual("tablet", await phone.Locator(".tm-dod").GetAttributeAsync("data-layout"));
        Assert.AreEqual(0, await phone.Locator(".tm-modal--fullscreen-presentation").CountAsync(), "a forced layout wins over the viewport");
    }

    [TestMethod]
    [TestCategory("WASM")]
    [DataRow("Diagram")]
    [DataRow("Spreadsheet")]
    [Description("Phase 9 DoD: the same dialog opens as the 320 px fullscreen panel for the Diagram and Spreadsheet document kinds (the kinds the Notion diagram / spreadsheet blocks insert)")]
    public async Task DocLibMobile_OtherKinds_OpenTheFullscreenPanelAt320(string kind)
    {
        var page = await OpenPageAsync(320, 640);
        await page.SelectOptionAsync("[data-testid=kind-select]", kind);
        await OpenDialogAsync(page);

        Assert.AreEqual("mobile", await page.Locator(".tm-dod").GetAttributeAsync("data-layout"));
        Assert.AreEqual(1, await page.Locator(".tm-modal--fullscreen-presentation").CountAsync());
        Assert.AreEqual(0, await page.Locator(".tm-dod-tree").CountAsync(), "no tree on mobile");
        await AssertInViewportAsync(page, ".tm-modal-footer", 320, 640, $"the {kind} footer");
        await AssertInViewportAsync(page, ".tm-dod-open", 320, 640, $"Open ({kind})");
        await AssertInViewportAsync(page, ".tm-dod-mode", 320, 640, $"the Link / Copy cards ({kind})");
        await AssertNoHorizontalScrollAsync(page, $"{kind} at 320");
        Assert.AreEqual("polite", await page.Locator(".tm-dod-mobile-head [role=status]").GetAttributeAsync("aria-live"), "the drill-down announcement region is present");
    }
    [TestMethod]
    [TestCategory("WASM")]
    [Description("Edge: a viewport between 640 and 752 measures mobile inside the dialog and stays mobile in the panel - the presentation does not flap")]
    public async Task DocLibMobile_700px_StaysMobile_NoFlap()
    {
        var page = await OpenPageAsync(700, 800, touch: false);
        await OpenDialogAsync(page);
        await page.WaitForTimeoutAsync(1200);

        Assert.AreEqual("mobile", await page.Locator(".tm-dod").GetAttributeAsync("data-layout"));
        Assert.AreEqual(1, await page.Locator(".tm-modal--fullscreen-presentation").CountAsync());
        Assert.AreEqual(0, await page.Locator(".tm-dod-tree").CountAsync());
        await AssertInViewportAsync(page, ".tm-dod-open", 700, 800, "Open");
    }

    [TestMethod]
    [TestCategory("WASM")]
    [Description("Edge: resizing a desktop window to phone width switches the open dialog to the fullscreen panel and back")]
    public async Task DocLibMobile_Resize_SwitchesThePresentation_AndBack()
    {
        var page = await OpenPageAsync(1440, 900, touch: false);
        await OpenDialogAsync(page);
        Assert.AreEqual(1, await page.Locator(".tm-dod-tree").CountAsync());

        await page.SetViewportSizeAsync(390, 844);
        await page.WaitForFunctionAsync("() => document.querySelector('.tm-dod')?.getAttribute('data-layout') === 'mobile'", null, new PageWaitForFunctionOptions { Timeout = 10_000 });
        Assert.AreEqual(1, await page.Locator(".tm-modal--fullscreen-presentation").CountAsync());
        Assert.AreEqual(0, await page.Locator(".tm-dod-tree").CountAsync());

        await page.SetViewportSizeAsync(1440, 900);
        await page.WaitForFunctionAsync("() => document.querySelector('.tm-dod')?.getAttribute('data-layout') === 'desktop'", null, new PageWaitForFunctionOptions { Timeout = 10_000 });
        Assert.AreEqual(1, await page.Locator(".tm-dod-tree").CountAsync());
        Assert.AreEqual(0, await page.Locator(".tm-modal--fullscreen-presentation").CountAsync());
    }

    // ── Cleanup helpers (the demo store is shared by every DocumentLibrary spec) ───

    private static async Task DeleteByNameAsync(string folder, params string[] names)
    {
        using var http = DemoApiTestHttp.CreateClient();
        try
        {
            var page = await http.GetFromJsonAsync<System.Text.Json.JsonElement>(
                $"https://localhost:5100/api/document-library/wireframe/browse?folderPath={Uri.EscapeDataString(folder)}&take=200");
            var ids = page.GetProperty("items").EnumerateArray()
                .Where(i => names.Contains(i.GetProperty("name").GetString()))
                .Select(i => i.GetProperty("id").GetGuid())
                .ToList();
            if (ids.Count > 0)
            {
                await http.PostAsJsonAsync("https://localhost:5100/api/document-library/wireframe/documents/delete", new { ids });
            }
        }
        catch
        {
            // best-effort cleanup: the test's own assertions report the real failure
        }
    }

    private static async Task DeleteFolderAsync(string name)
    {
        using var http = DemoApiTestHttp.CreateClient();
        try
        {
            await http.DeleteAsync("https://localhost:5100/api/document-library/wireframe/folders?folderPath=" + Uri.EscapeDataString("/Archive/" + name));
        }
        catch
        {
            // best-effort cleanup
        }
    }
}
