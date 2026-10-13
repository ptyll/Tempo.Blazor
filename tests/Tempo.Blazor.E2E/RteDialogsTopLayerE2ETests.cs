using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// FZE-10: the rich-editor dialogs (link, image, table, video, find &amp; replace) are TmModals —
/// each promotes to the browser top layer, so a dialog opened from the editor toolbar inside an
/// already-promoted bottom drawer paints ABOVE it, Escape closes it, and focus returns to the
/// editor inside the drawer (the caret the dialog was opened from is replayed on close). The demo
/// hosts a full editor in a promoted drawer on /richtext specifically for this contract.
/// The review round additionally pins the close-then-insert contract: Enter in the link dialog
/// wraps exactly the selected word in the DRAWER editor (never a marker span, never the page
/// editor), and escaping find &amp; replace restores the selected word as the live selection.
/// </summary>
[TestClass]
[TestCategory("WASM")]
public class RteDialogsTopLayerE2ETests : WasmTestBase
{
    private const int DesktopWidth = 1440;
    private const int DesktopHeight = 900;
    private const int MobileWidth = 390;
    private const int MobileHeight = 844;

    /// <summary>Opens /richtext, opens the promoted bottom drawer with a full editor inside.</summary>
    private async Task<IPage> OpenEditorInDrawerAsync(int width, int height)
    {
        var context = await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = width, Height = height },
            Locale = "en-US",
            IgnoreHTTPSErrors = true
        });
        RegisterContext(context);
        var page = await context.NewPageAsync();
        await page.GotoAsync($"{BaseUrl}/richtext");
        await WaitForAppReadyAsync(page);

        await page.Locator("[data-testid='open-editor-drawer']").ClickAsync();
        var drawer = page.Locator(".tm-drawer--bottom");
        await drawer.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15000 });
        await WaitForPromotedAsync(page, ".tm-drawer--bottom");
        return page;
    }

    private static Task WaitForPromotedAsync(IPage page, string selector) =>
        page.WaitForFunctionAsync(
            "selector => { const el = document.querySelector(selector); return !!el && el.matches(':popover-open'); }",
            selector,
            new PageWaitForFunctionOptions { Timeout = 15000 });

    /// <summary>Waits until no finite animation is still running (the sheet promote/settle is done).</summary>
    private static Task WaitForAnimationsSettledAsync(IPage page) =>
        page.WaitForFunctionAsync(
            """
            () => document.getAnimations().every(a =>
                a.playState !== 'running' ||
                (a.effect && a.effect.getTiming && a.effect.getTiming().iterations === Infinity))
            """,
            null,
            new PageWaitForFunctionOptions { PollingInterval = 50, Timeout = 5000 });

    private async Task<IPage> OpenDialogFromDrawerToolbarAsync(string format, string dialogClass, int width = DesktopWidth, int height = DesktopHeight)
    {
        var page = await OpenEditorInDrawerAsync(width, height);

        await page.Locator($".tm-drawer--bottom .tm-rte-btn[data-format='{format}']").ClickAsync();
        var dialog = page.Locator($".tm-modal-overlay.{dialogClass}");
        await dialog.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15000 });
        await WaitForPromotedAsync(page, $".tm-modal-overlay.{dialogClass}");
        return page;
    }

    private async Task<IPage> OpenFindReplaceFromDrawerEditorAsync(int width = DesktopWidth, int height = DesktopHeight)
    {
        var page = await OpenEditorInDrawerAsync(width, height);

        await page.Locator(".tm-drawer--bottom .tm-rte-editor-content").ClickAsync();
        await page.Keyboard.PressAsync("Control+f");
        var dialog = page.Locator(".tm-modal-overlay.tm-rte-findreplace-dialog");
        await dialog.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15000 });
        await WaitForPromotedAsync(page, ".tm-modal-overlay.tm-rte-findreplace-dialog");
        return page;
    }

    /// <summary>Types a sentence into the drawer editor and selects the word "tempo" as the live selection.</summary>
    private static async Task TypeSentenceAndSelectTempoAsync(IPage page)
    {
        var editor = page.Locator(".tm-drawer--bottom .tm-rte-editor-content");
        await editor.ClickAsync();
        await page.Keyboard.TypeAsync("The tempo is fast today");

        var selected = await page.EvaluateAsync<bool>(
            """
            () => {
                const editor = document.querySelector('.tm-drawer--bottom .tm-rte-editor-content');
                const walker = document.createTreeWalker(editor, NodeFilter.SHOW_TEXT);
                let node;
                while ((node = walker.nextNode())) {
                    const idx = node.textContent.indexOf('tempo');
                    if (idx >= 0) {
                        const range = document.createRange();
                        range.setStart(node, idx);
                        range.setEnd(node, idx + 5);
                        const sel = window.getSelection();
                        sel.removeAllRanges();
                        sel.addRange(range);
                        return true;
                    }
                }
                return false;
            }
            """);
        Assert.IsTrue(selected, "the word 'tempo' must exist in the drawer editor and be selectable");
        Assert.AreEqual("tempo", await page.EvaluateAsync<string>("() => window.getSelection().toString()"),
            "the test setup must leave 'tempo' as the live selection");
    }

    private static async Task AssertPaintsAboveTheDrawerAsync(IPage page, string dialogClass)
    {
        Assert.IsTrue(
            await OverlayTopLayerScenarios.PaintsAtAsync(page, ".tm-drawer--bottom", $".tm-modal-overlay.{dialogClass}"),
            $"the {dialogClass} opened from the drawer's editor must paint above the open drawer (top-layer order equals open order)");
    }

    private static async Task AssertFocusBackInTheDrawerEditorAsync(IPage page)
    {
        // A timeout here IS the failure: focus never came back to the drawer's editor surface.
        await page.WaitForFunctionAsync(
            """
            () => {
                const a = document.activeElement;
                return !!a && a.classList.contains('tm-rte-editor-content') && !!a.closest('.tm-drawer--bottom');
            }
            """,
            null,
            new PageWaitForFunctionOptions { Timeout = 10000 });
    }

    private static async Task CloseWithEscapeAndAssertFocusRestoredAsync(IPage page, string dialogClass)
    {
        await page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(page.Locator($".tm-modal-overlay.{dialogClass}"))
            .ToBeHiddenAsync(new LocatorAssertionsToBeHiddenOptions { Timeout = 5000 });
        await AssertFocusBackInTheDrawerEditorAsync(page);
    }

    /// <summary>The sheet's footer bar must be completely inside the viewport at 390.</summary>
    private static async Task AssertSheetFooterFullyVisibleAsync(IPage page)
    {
        // Measure only after the sheet finished sliding up — mid-transition the footer is still
        // below the fold and the check would race the transition.
        await WaitForAnimationsSettledAsync(page);
        var rect = await page.EvaluateAsync<string>(
            """
            () => {
                const f = document.querySelector('.tm-modal-footer');
                if (!f) return JSON.stringify({ found: false });
                const r = f.getBoundingClientRect();
                return JSON.stringify({
                    found: true, top: r.top, bottom: r.bottom, height: r.height,
                    viewport: window.innerHeight
                });
            }
            """);
        var parsed = System.Text.Json.JsonSerializer.Deserialize<FooterProbe>(rect,
            new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        Assert.IsTrue(parsed.Found && parsed.Height > 0 && parsed.Bottom <= parsed.Viewport && parsed.Top >= 0,
            $"the dialog sheet's footer must be fully visible (bottom <= viewport height); probe: {rect}");
    }

    private sealed class FooterProbe
    {
        public bool Found { get; set; }
        public double Top { get; set; }
        public double Bottom { get; set; }
        public double Height { get; set; }
        public double Viewport { get; set; }
    }

    private async Task TakeViewportScreenshotAsync(IPage page, string name)
    {
        // Viewport crop only (never a stretched full-page capture). Animations must have settled
        // first — a mid-transition shot is a false failure report. The file goes to an explicit
        // scratch directory AND the test results — the runner's per-test In folder has proven
        // lossy for later tests in a run.
        await WaitForAnimationsSettledAsync(page);
        var screenshot = await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Type = ScreenshotType.Png,
            FullPage = false,
            Timeout = 60_000
        });
        var scratch = Path.Combine(FindRepositoryRoot(), "tmp", "fze10-shots");
        Directory.CreateDirectory(scratch);
        var path = Path.Combine(scratch, $"{name}_{DateTime.Now:yyyyMMdd_HHmmss}.png");
        await File.WriteAllBytesAsync(path, screenshot);
        Assert.IsTrue(File.Exists(path), $"screenshot must be written to {path}");
        TestContext.AddResultFile(path);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "TempoBlazor.slnx")))
            {
                return directory.FullName;
            }
            directory = directory.Parent;
        }
        throw new InvalidOperationException("Repository root was not found.");
    }

    [TestMethod]
    [Description("Link dialog opened from the drawer's editor toolbar is top layer above the drawer; Escape closes it and focus returns to the editor")]
    public async Task LinkDialog_FromDrawerToolbar_PaintsAboveDrawer_EscapeReturnsFocus()
    {
        var page = await OpenDialogFromDrawerToolbarAsync("link", "tm-rte-link-dialog");

        Assert.IsTrue(await page.Locator(".tm-modal-overlay.tm-rte-link-dialog").EvaluateAsync<bool>("el => el.matches(':popover-open')"),
            "the link dialog must be a live top-layer popover");
        await AssertPaintsAboveTheDrawerAsync(page, "tm-rte-link-dialog");

        // The dialog is named by its title (axe contract).
        await Assertions.Expect(page.Locator(".tm-rte-link-dialog .tm-modal-title")).ToBeVisibleAsync();

        await TakeViewportScreenshotAsync(page, "rte_link_dialog_1440");

        await CloseWithEscapeAndAssertFocusRestoredAsync(page, "tm-rte-link-dialog");

        // The drawer survives the dialog's Escape.
        Assert.AreEqual(1, await page.Locator(".tm-drawer--bottom").CountAsync(), "the drawer must survive the dialog's Escape");
    }

    [TestMethod]
    [Description("Typing in the drawer editor, selecting a word and confirming the link dialog with Enter wraps exactly that word in the DRAWER editor — no marker span, page editor untouched")]
    public async Task LinkDialog_EnterSubmit_WrapsExactlyTheSelectedWordInTheDrawerEditor()
    {
        var page = await OpenEditorInDrawerAsync(DesktopWidth, DesktopHeight);

        // The page editor outside the drawer must stay untouched by the whole flow.
        var pageEditorBefore = await page.EvaluateAsync<string>(
            "() => { const e = document.querySelector('.tm-rte-editor-content:not(.tm-drawer--bottom *)'); return e ? e.innerHTML : null; }");

        await TypeSentenceAndSelectTempoAsync(page);

        // Selecting "tempo" then clicking Link: the toolbar must NOT have collapsed the
        // selection (mousedown default prevented) — the dialog opens on top of the drawer.
        await page.Locator(".tm-drawer--bottom .tm-rte-btn[data-format='link']").ClickAsync();
        var dialog = page.Locator(".tm-modal-overlay.tm-rte-link-dialog");
        await dialog.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15000 });
        await WaitForPromotedAsync(page, ".tm-modal-overlay.tm-rte-link-dialog");

        // The selection is bookmarked, so it may leave the editor — but the preselected link
        // text the dialog offers is the word that was selected.
        var preselected = await page.EvaluateAsync<string>(
            "() => { const i = document.querySelector('.tm-rte-link-text-input'); return i ? i.value : null; }");
        Assert.AreEqual("tempo", preselected, "the dialog must offer the selected word as the link text");

        // Enter in the URL field submits the form (implicit submission through the form's
        // type=submit footer button) — the close-then-insert path runs after the dialog unmounts.
        await page.Locator(".tm-rte-link-url-input").FillAsync("https://example.com/tempo");
        await page.Keyboard.PressAsync("Enter");

        await Assertions.Expect(dialog).ToBeHiddenAsync(new LocatorAssertionsToBeHiddenOptions { Timeout = 5000 });
        await AssertFocusBackInTheDrawerEditorAsync(page);

        var result = await page.EvaluateAsync<string>(
            """
            () => {
                const editor = document.querySelector('.tm-drawer--bottom .tm-rte-editor-content');
                const links = Array.from(editor.querySelectorAll('a'))
                    .map(a => ({ href: a.getAttribute('href'), text: a.textContent }));
                return JSON.stringify({
                    links,
                    markers: document.querySelectorAll('#tm-cursor-marker').length
                });
            }
            """);
        var parsed = System.Text.Json.JsonSerializer.Deserialize<LinkProbe>(result,
            new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        Assert.AreEqual(1, parsed.Links.Count, $"exactly one link must exist in the DRAWER editor, got: {result}");
        Assert.AreEqual("tempo", parsed.Links[0].Text, "the link must wrap the word that was selected");
        Assert.AreEqual("https://example.com/tempo", parsed.Links[0].Href);
        Assert.AreEqual(0, parsed.Markers, "no #tm-cursor-marker may remain in the document");

        var pageEditorAfter = await page.EvaluateAsync<string>(
            "() => { const e = document.querySelector('.tm-rte-editor-content:not(.tm-drawer--bottom *)'); return e ? e.innerHTML : null; }");
        Assert.AreEqual(pageEditorBefore, pageEditorAfter, "the page editor outside the drawer must be untouched");
    }

    private sealed class LinkProbe
    {
        public List<LinkInfo> Links { get; set; } = new();
        public int Markers { get; set; }
    }

    private sealed class LinkInfo
    {
        public string Href { get; set; } = string.Empty;
        public string Text { get; set; } = string.Empty;
    }

    [TestMethod]
    [Description("Find & Replace opened with Ctrl+F from the drawer's editor is top layer; Escape returns focus AND restores the selected word as the live selection")]
    public async Task FindReplaceDialog_CtrlF_PaintsAboveDrawer_EscapeReturnsFocusIntoEditor()
    {
        var page = await OpenEditorInDrawerAsync(DesktopWidth, DesktopHeight);

        // Select a word BEFORE opening the dialog — Escape must replay it as the live selection.
        await TypeSentenceAndSelectTempoAsync(page);
        await page.Keyboard.PressAsync("Control+f");

        var dialog = page.Locator(".tm-modal-overlay.tm-rte-findreplace-dialog");
        await dialog.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15000 });
        await WaitForPromotedAsync(page, ".tm-modal-overlay.tm-rte-findreplace-dialog");

        await AssertPaintsAboveTheDrawerAsync(page, "tm-rte-findreplace-dialog");

        // The find input holds the initial focus inside the dialog.
        var findFocused = await page.EvaluateAsync<bool>(
            "() => { const a = document.activeElement; return !!a && a.classList.contains('tm-rte-findreplace-find-input'); }");
        Assert.IsTrue(findFocused, "the find input must take the initial focus");

        await TakeViewportScreenshotAsync(page, "rte_find_dialog_1440");

        await page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(dialog).ToBeHiddenAsync(new LocatorAssertionsToBeHiddenOptions { Timeout = 5000 });
        await AssertFocusBackInTheDrawerEditorAsync(page);

        // The saved selection is replayed — the word is the live selection again (a collapsed
        // caret or an empty selection would be the old marker-span behaviour).
        await page.WaitForFunctionAsync(
            "() => (window.getSelection().toString() || '') === 'tempo'",
            null,
            new PageWaitForFunctionOptions { Timeout = 10000 });
    }

    [TestMethod]
    [Description("Image dialog opened from the drawer's editor toolbar is top layer above the drawer")]
    public async Task ImageDialog_FromDrawerToolbar_PaintsAboveDrawer_EscapeReturnsFocus()
    {
        var page = await OpenDialogFromDrawerToolbarAsync("image", "tm-rte-image-dialog");

        await AssertPaintsAboveTheDrawerAsync(page, "tm-rte-image-dialog");
        await CloseWithEscapeAndAssertFocusRestoredAsync(page, "tm-rte-image-dialog");
    }

    [TestMethod]
    [Description("Table dialog opened from the drawer's editor toolbar is top layer above the drawer")]
    public async Task TableDialog_FromDrawerToolbar_PaintsAboveDrawer_EscapeReturnsFocus()
    {
        var page = await OpenDialogFromDrawerToolbarAsync("table", "tm-rte-table-dialog");

        await AssertPaintsAboveTheDrawerAsync(page, "tm-rte-table-dialog");
        await CloseWithEscapeAndAssertFocusRestoredAsync(page, "tm-rte-table-dialog");
    }

    [TestMethod]
    [Description("Video dialog opened from the drawer's editor toolbar is top layer above the drawer")]
    public async Task VideoDialog_FromDrawerToolbar_PaintsAboveDrawer_EscapeReturnsFocus()
    {
        var page = await OpenDialogFromDrawerToolbarAsync("video", "tm-rte-video-dialog");

        await AssertPaintsAboveTheDrawerAsync(page, "tm-rte-video-dialog");
        await CloseWithEscapeAndAssertFocusRestoredAsync(page, "tm-rte-video-dialog");
    }

    [TestMethod]
    [Description("Mobile 390: the link dialog opens as a sheet above the drawer sheet, its footer is fully visible, and Escape closes it")]
    public async Task LinkDialog_Mobile390_OpensAsSheetAboveTheDrawer()
    {
        var page = await OpenDialogFromDrawerToolbarAsync("link", "tm-rte-link-dialog", MobileWidth, MobileHeight);

        await AssertPaintsAboveTheDrawerAsync(page, "tm-rte-link-dialog");
        await AssertSheetFooterFullyVisibleAsync(page);
        await TakeViewportScreenshotAsync(page, "rte_link_dialog_390");

        await CloseWithEscapeAndAssertFocusRestoredAsync(page, "tm-rte-link-dialog");
    }

    [TestMethod]
    [Description("Mobile 390: the table dialog opens as a sheet above the drawer sheet, its footer is fully visible, and Escape closes it")]
    public async Task TableDialog_Mobile390_OpensAsSheetAboveTheDrawer()
    {
        var page = await OpenDialogFromDrawerToolbarAsync("table", "tm-rte-table-dialog", MobileWidth, MobileHeight);

        await AssertPaintsAboveTheDrawerAsync(page, "tm-rte-table-dialog");
        await AssertSheetFooterFullyVisibleAsync(page);
        await TakeViewportScreenshotAsync(page, "rte_table_dialog_390");

        await CloseWithEscapeAndAssertFocusRestoredAsync(page, "tm-rte-table-dialog");
    }

    [TestMethod]
    [Description("Mobile 390: find & replace opens above the drawer sheet, its footer is fully visible, and Escape closes it")]
    public async Task FindReplaceDialog_Mobile390_OpensAboveTheDrawer()
    {
        var page = await OpenFindReplaceFromDrawerEditorAsync(MobileWidth, MobileHeight);

        await AssertPaintsAboveTheDrawerAsync(page, "tm-rte-findreplace-dialog");
        await AssertSheetFooterFullyVisibleAsync(page);
        await TakeViewportScreenshotAsync(page, "rte_find_dialog_390");

        await CloseWithEscapeAndAssertFocusRestoredAsync(page, "tm-rte-findreplace-dialog");
    }
}
