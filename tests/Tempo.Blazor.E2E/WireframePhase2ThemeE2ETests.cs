using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Wireframe redesign plan ce19aa8c, phase 2 — the editor, the export dialog and the shared
/// <c>TmDocumentOpenDialog</c> read the core tokens. Captures each surface at 1440 in every
/// combination of light/dark × default/indigo into <c>__screenshots__/wireframe/phase2/</c> (and the
/// open dialog into <c>__screenshots__/document-library/phase2/</c>) and ASSERTS the theme in computed
/// style, so a regression is a failing number and not only a picture to squint at:
/// the accent follows <c>--tm-color-primary</c>; dark mode has no light chrome; the page and the
/// toolbox thumbnails stay paper; the exported SVG never carries a <c>var()</c>.
/// </summary>
[TestClass]
public sealed class WireframePhase2ThemeE2ETests : WasmTestBase
{
    private const string EditorUrl = "/wireframe-editor";
    private const string OpenDialogUrl = "/document-open-dialog";

    private static string ScreenshotRoot =>
        Path.Combine(FindRepoRoot(), "tests", "Tempo.Blazor.E2E", "__screenshots__");

    /// <summary>
    /// The committed phase-2 screenshots are only rewritten on purpose: set <c>TM_E2E_UPDATE_BASELINE=1</c> (same switch as
    /// <c>WireframeResponsiveScreenshotTests</c>). Without it the captures go to the (git-ignored) per-run TestResults folder
    /// and are attached to the test result, so a normal run never dirties the working tree.
    /// </summary>
    private static bool UpdateBaseline =>
        string.Equals(Environment.GetEnvironmentVariable("TM_E2E_UPDATE_BASELINE"), "1", StringComparison.Ordinal);

    private static string WireframeDirectory => UpdateBaseline
        ? Path.Combine(ScreenshotRoot, "wireframe", "phase2")
        : Path.Combine(FindRepoRoot(), "tests", "Tempo.Blazor.E2E", "TestResults", "wireframe-phase2");

    private static string DocumentLibraryDirectory => UpdateBaseline
        ? Path.Combine(ScreenshotRoot, "document-library", "phase2")
        : Path.Combine(FindRepoRoot(), "tests", "Tempo.Blazor.E2E", "TestResults", "document-library-phase2");

    // ── Theme matrix ──────────────────────────────────────────────────────────

    [TestMethod]
    [TestCategory("WASM")]
    [Description("Editor, export dialog and open dialog at 1440 in light/dark x default/indigo, with computed-style assertions")]
    public async Task Matrix_1440_EditorExportAndOpenDialog_FollowTheTheme()
    {
        Directory.CreateDirectory(WireframeDirectory);
        Directory.CreateDirectory(DocumentLibraryDirectory);

        foreach (var dark in new[] { false, true })
        {
            foreach (var indigo in new[] { false, true })
            {
                var tag = (dark ? "dark" : "light") + "-" + (indigo ? "indigo" : "default");

                // 1. Editor + export dialog.
                var page = await OpenAsync(EditorUrl, 1440, 900, dark, indigo);
                await page.WaitForFunctionAsync(
                    "() => document.querySelector('.tm-wd-editor .tm-wd-toolbox__item') !== null",
                    null, new PageWaitForFunctionOptions { Timeout = 30_000 });
                var editor = page.Locator(".tm-wd-editor").First;
                await AssertChromeFollowsTheThemeAsync(page, dark, indigo);

                // A selected element shows the accent through the selection chrome.
                var before = await editor.Locator("g[data-el-id]").CountAsync();
                await editor.Locator(".tm-wd-toolbox__item[data-component-type='TmButton']").First.ClickAsync();
                await page.WaitForFunctionAsync(
                    "n => document.querySelectorAll('.tm-wd-editor')[0].querySelectorAll('g[data-el-id]').length > n",
                    before, new PageWaitForFunctionOptions { Timeout = 10_000 });
                await page.WaitForSelectorAsync(".tm-wd-editor .tm-wd-selection rect",
                    new PageWaitForSelectorOptions { State = WaitForSelectorState.Attached, Timeout = 10_000 });
                await AssertSelectionUsesTheAccentAsync(page, dark, indigo);
                await AssertMinimapFollowsTheThemeAsync(page, dark, indigo);

                await Shot(page.Locator(".tm-wd-editor").First, WireframeDirectory, $"editor--{tag}");
                await HideDemoChromeAsync(page);   // the minimap card would intercept the toolbar clicks below

                await editor.Locator("[data-testid='wd-export'] button.tm-dropdown-trigger").First.ClickAsync();
                await page.Locator("[data-testid='wd-export-png']").First.ClickAsync();
                await page.WaitForSelectorAsync(".tm-wd-export-dialog",
                    new PageWaitForSelectorOptions { State = WaitForSelectorState.Visible, Timeout = 15_000 });
                await SettleAnimationsAsync(page);
                await AssertExportDialogAsync(page, dark);
                await ShotPage(page, WireframeDirectory, $"export-dialog--{tag}");

                // 2. Open dialog.
                var dialogPage = await OpenAsync(OpenDialogUrl, 1440, 900, dark, indigo);
                await dialogPage.ClickAsync("[data-testid=open-dialog-btn]");
                await dialogPage.WaitForSelectorAsync(".tm-document-open-dialog",
                    new PageWaitForSelectorOptions { State = WaitForSelectorState.Visible, Timeout = 20_000 });
                await dialogPage.Locator(".tm-dod-tree-node", new() { HasTextString = "Designs" }).First.ClickAsync();
                await dialogPage.WaitForSelectorAsync(".tm-dod-row",
                    new PageWaitForSelectorOptions { State = WaitForSelectorState.Visible, Timeout = 20_000 });
                await dialogPage.Locator(".tm-dod-row").First.ClickAsync();
                await SettleAnimationsAsync(dialogPage);
                await AssertOpenDialogFollowsTheThemeAsync(dialogPage, dark, indigo);
                await ShotPage(dialogPage, DocumentLibraryDirectory, $"open-dialog--{tag}");
            }
        }
    }

    // ── Export honesty ────────────────────────────────────────────────────────

    [TestMethod]
    [TestCategory("WASM")]
    [Description("The exported SVG carries explicit colours - never var(), whatever the theme")]
    public async Task ExportedSvg_HasExplicitPaints_InDarkIndigo()
    {
        var page = await OpenAsync(EditorUrl, 1440, 900, dark: true, indigo: true);
        await page.WaitForFunctionAsync(
            "() => document.querySelector('.tm-wd-editor .tm-wd-canvas__svg') !== null",
            null, new PageWaitForFunctionOptions { Timeout = 30_000 });
        await HideDemoChromeAsync(page);

        var svg = await page.EvaluateAsync<string>(
            """
            () => {
                const el = document.querySelector('.tm-wd-editor .tm-wd-canvas__svg');
                return window.tmWireframeDesigner.exportSvg(el);
            }
            """);

        Assert.IsFalse(string.IsNullOrWhiteSpace(svg), "exportSvg must return the canvas markup");
        Assert.IsFalse(svg.Contains("var(--tm-wd-"), "an exported SVG is standalone: no custom-property reference may remain");
        Assert.IsFalse(svg.Contains("data-wd-paint"), "the bake marker must be removed from the export");

        // The page background is paper (white) in EVERY theme, so the export matches the screen.
        var pageFill = await page.EvaluateAsync<string>(
            "() => getComputedStyle(document.querySelector('.tm-wd-editor .tm-wd-canvas__svg > rect')).fill");
        Assert.AreEqual("rgb(255, 255, 255)", pageFill, "the wireframe page is paper, also in dark mode");
        Assert.IsTrue(svg.Contains("rgb(255, 255, 255)") || svg.Contains("#ffffff"),
            "the baked page fill is an explicit paper colour");
    }

    // ── Edge cases ────────────────────────────────────────────────────────────

    [TestMethod]
    [TestCategory("WASM")]
    [Description("Edge: toolbar buttons are 32px square icon buttons; no emoji glyphs; tabbing shows a visible focus ring")]
    public async Task Toolbar_IconButtons_Are32px_NoGlyphs_AndKeepAFocusRing()
    {
        var page = await OpenAsync(EditorUrl, 1440, 900, dark: false, indigo: false);
        await page.WaitForFunctionAsync(
            "() => document.querySelector('.tm-wd-editor .tm-wd-editor__toolbar') !== null",
            null, new PageWaitForFunctionOptions { Timeout = 30_000 });
        await HideDemoChromeAsync(page);

        var metrics = await page.EvaluateAsync<System.Text.Json.JsonElement>(
            """
            () => {
                const toolbar = document.querySelector('.tm-wd-editor .tm-wd-editor__toolbar');
                const buttons = [...toolbar.querySelectorAll('.tm-toolbar-btn--icon-only:not(.tm-toolbar-item--collapsed)')];
                const heights = buttons.map(b => Math.round(b.getBoundingClientRect().height));
                const widths = buttons.map(b => Math.round(b.getBoundingClientRect().width));
                const glyphs = /[\u2196\u270B\u21A9\u21AA\u{1F512}\u{1F4C1}\u{1F4C2}\u{1F9F2}\u{1F4CF}\u{1F3A8}\u{1F58C}\u{1F4D0}\u25E7\u25E8]/u;
                const withGlyph = buttons.filter(b => glyphs.test(b.textContent)).length;
                const withIcon = buttons.filter(b => b.querySelector('svg.tm-icon')).length;
                return { count: buttons.length, min: Math.min(...heights), max: Math.max(...heights), minW: Math.min(...widths), withGlyph, withIcon };
            }
            """);

        // Phase 4: the command row is a core TmToolbar. Icon-only buttons (zoom) keep a >=32px target and render a TmIcon.
        Assert.IsTrue(metrics.GetProperty("count").GetInt32() >= 2, "the toolbar renders its icon-only buttons (zoom in / out)");
        Assert.IsTrue(metrics.GetProperty("min").GetInt32() >= 32, "toolbar icon buttons are at least 32px high");
        Assert.IsTrue(metrics.GetProperty("minW").GetInt32() >= 32, "icon buttons are at least 32px wide");
        Assert.AreEqual(0, metrics.GetProperty("withGlyph").GetInt32(), "no emoji / unicode glyph in a toolbar button");
        Assert.AreEqual(metrics.GetProperty("count").GetInt32(), metrics.GetProperty("withIcon").GetInt32(), "every icon button renders a TmIcon svg");

        // Keyboard focus ring: Tab onto a toolbar button paints the core two-layer ring (box-shadow), not nothing.
        await page.Locator(".tm-wd-editor__toolbar .tm-toolbar-btn").First.FocusAsync();
        await page.Keyboard.PressAsync("Tab");
        await page.Keyboard.PressAsync("Shift+Tab");
        var ring = await page.EvaluateAsync<string>(
            "() => getComputedStyle(document.activeElement).boxShadow");
        Assert.IsFalse(string.IsNullOrWhiteSpace(ring) || ring == "none", $"a focused toolbar button shows a focus ring (box-shadow: {ring})");
    }

    [TestMethod]
    [TestCategory("WASM")]
    [Description("Edge: a read-only editor mutes its toolbox, and a toolbox item keeps Space from scrolling the palette")]
    public async Task ReadOnlyToolbox_IsMuted_AndSpaceDoesNotScrollThePalette()
    {
        var page = await OpenAsync(EditorUrl, 1440, 900, dark: false, indigo: false);
        await page.WaitForFunctionAsync(
            "() => document.querySelector('.tm-wd-editor .tm-wd-toolbox__item') !== null",
            null, new PageWaitForFunctionOptions { Timeout = 30_000 });
        await HideDemoChromeAsync(page);

        // The second demo editor is host-pinned ReadOnly="true" but hides its toolbox, so the first (editable) editor is
        // switched to read-only with its built-in toggle and ITS toolbox must then be muted.
        await page.Locator(".tm-wd-editor").First.Locator("button.tm-toolbar-more").ClickAsync();
        await page.Locator("[role='menu'] [role='menuitemcheckbox']").Filter(new() { HasTextString = "Read-only" }).First.ClickAsync();
        var readOnlyItem = page.Locator(".tm-wd-editor").First.Locator(".tm-wd-toolbox__item").First;
        await readOnlyItem.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Attached, Timeout = 10_000 });
        Assert.IsTrue(await readOnlyItem.CountAsync() > 0, "the read-only editor must still render its (inert) toolbox items");
        Assert.AreEqual("true", await readOnlyItem.GetAttributeAsync("aria-disabled"));
        var opacity = await readOnlyItem.EvaluateAsync<string>("el => getComputedStyle(el).opacity");
        Assert.IsTrue(double.Parse(opacity, System.Globalization.CultureInfo.InvariantCulture) < 1, $"a read-only item is muted (opacity {opacity})");

        // Back to edit mode, then Space on a focused item of the EDITABLE toolbox: the palette must not scroll (keydown default cancelled)
        // while activation still happens exactly once, on keyup.
        var editor = page.Locator(".tm-wd-editor").First;
        await editor.Locator("button.tm-toolbar-more").ClickAsync();
        await page.Locator("[role='menu'] [role='menuitemcheckbox']").Filter(new() { HasTextString = "Read-only" }).First.ClickAsync();
        var item = editor.Locator(".tm-wd-toolbox__item[data-component-type='TmButton']").First;
        await item.FocusAsync();
        var scrollBefore = await editor.Locator(".tm-wd-toolbox__list").EvaluateAsync<double>("el => el.scrollTop");
        var count = await editor.Locator("g[data-el-id]").CountAsync();
        await page.Keyboard.PressAsync("Space");
        await page.WaitForFunctionAsync(
            "n => document.querySelectorAll('.tm-wd-editor')[0].querySelectorAll('g[data-el-id]').length === n + 1",
            count, new PageWaitForFunctionOptions { Timeout = 10_000 });
        var scrollAfter = await editor.Locator(".tm-wd-toolbox__list").EvaluateAsync<double>("el => el.scrollTop");
        Assert.AreEqual(scrollBefore, scrollAfter, "Space on a toolbox item must not scroll the palette");
    }

    [TestMethod]
    [TestCategory("WASM")]
    [Description("Edge: the open dialog keeps its row selected without scrolling on Space, and a selected card is outlined in the primary colour")]
    public async Task OpenDialog_SpaceSelectsWithoutScrolling_AndSelectionUsesPrimary()
    {
        var page = await OpenAsync(OpenDialogUrl, 1440, 900, dark: false, indigo: true);
        await page.ClickAsync("[data-testid=open-dialog-btn]");
        await page.WaitForSelectorAsync(".tm-document-open-dialog",
            new PageWaitForSelectorOptions { State = WaitForSelectorState.Visible, Timeout = 20_000 });
        await page.Locator(".tm-dod-tree-node", new() { HasTextString = "Designs" }).First.ClickAsync();
        await page.WaitForSelectorAsync(".tm-dod-row",
            new PageWaitForSelectorOptions { State = WaitForSelectorState.Visible, Timeout = 20_000 });

        var row = page.Locator(".tm-dod-row").First;
        await row.FocusAsync();
        var scrollBefore = await page.Locator(".tm-dod-content").EvaluateAsync<double>("el => el.scrollTop");
        await page.Keyboard.PressAsync("Space");
        await page.WaitForSelectorAsync(".tm-dod-row--selected",
            new PageWaitForSelectorOptions { State = WaitForSelectorState.Attached, Timeout = 5_000 });
        Assert.AreEqual(scrollBefore, await page.Locator(".tm-dod-content").EvaluateAsync<double>("el => el.scrollTop"),
            "Space on a focused row selects it and does not scroll the list");

        // Indigo primary (#4f46e5) comes from the TOKEN: the selected row's inset bar is the theme primary.
        var bar = await page.Locator(".tm-dod-row--selected").First.EvaluateAsync<string>("el => getComputedStyle(el).boxShadow");
        StringAssert.Contains(bar, "rgb(79, 70, 229)", "the selected-row marker is --tm-color-primary (indigo theme)");

        await page.Locator(".tm-dod-view-grid").First.ClickAsync();
        await page.Locator(".tm-dod-card").First.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10_000 });
        await page.Locator(".tm-dod-card").First.ClickAsync();
        var border = await page.Locator(".tm-dod-card--selected").First.EvaluateAsync<string>("el => getComputedStyle(el).borderTopColor");
        Assert.AreEqual("rgb(79, 70, 229)", border, "the selected card is outlined in the theme primary");
    }

    // ── Assertions ────────────────────────────────────────────────────────────

    private static async Task AssertChromeFollowsTheThemeAsync(IPage page, bool dark, bool indigo)
    {
        var data = await page.EvaluateAsync<System.Text.Json.JsonElement>(
            """
            () => {
                const editor = document.querySelector('.tm-wd-editor');
                const toolbar = editor.querySelector('.tm-wd-editor__toolbar');
                const toolbox = editor.querySelector('.tm-wd-toolbox');
                const props = editor.querySelector('.tm-wd-props');
                const workspace = editor.querySelector('.tm-wd-editor__canvas-wrap');
                const page = editor.querySelector('.tm-wd-canvas__svg > rect');
                const preview = editor.querySelector('.tm-wd-toolbox__item-preview-paper');   // null when every visible tile shows an icon
                const bg = el => getComputedStyle(el).backgroundColor;
                const lum = c => { const m = c.match(/\d+(\.\d+)?/g).map(Number); const k = c.startsWith("color(") ? 1 : 1 / 255; return (0.2126*m[0] + 0.7152*m[1] + 0.0722*m[2]) * k; };
                return {
                    toolbar: bg(toolbar), toolbox: bg(toolbox.closest('.tm-editor-shell__panel') ?? toolbox.parentElement), props: bg(props.closest('.tm-editor-shell__panel') ?? props.parentElement), workspace: bg(workspace),
                    pageFill: getComputedStyle(page).fill, preview: preview ? bg(preview) : null,
                    toolbarLum: lum(bg(toolbar)), workspaceLum: lum(bg(workspace)),
                    toolboxHex: getComputedStyle(editor).getPropertyValue('--tm-wd-accent').trim()
                };
            }
            """);

        var toolbarLum = data.GetProperty("toolbarLum").GetDouble();
        var workspaceLum = data.GetProperty("workspaceLum").GetDouble();
        if (dark)
        {
            Assert.IsTrue(toolbarLum < 0.35, $"dark: the toolbar must be dark (luminance {toolbarLum:0.00}, {data.GetProperty("toolbar")})");
            Assert.IsTrue(workspaceLum < 0.25, $"dark: the workspace must be dark (luminance {workspaceLum:0.00})");
        }
        else
        {
            Assert.IsTrue(toolbarLum > 0.85, $"light: the toolbar is a light surface (luminance {toolbarLum:0.00})");
        }

        // The PAGE and the preview tiles stay paper in every theme.
        Assert.AreEqual("rgb(255, 255, 255)", data.GetProperty("pageFill").GetString(), "the wireframe page is paper");
        if (data.GetProperty("preview").ValueKind == System.Text.Json.JsonValueKind.String)
            Assert.AreEqual("rgb(255, 255, 255)", data.GetProperty("preview").GetString(), "toolbox thumbnails are paper");

        // The workspace is --tm-bg-workspace tinted 6 % toward the text colour: in light themes it is a faint grey
        // (strictly darker than the white page, so the page stands out even in the default theme where the
        // workspace token itself is white); in dark themes it stays a deep slate.
        var workspaceBg = data.GetProperty("workspace").GetString();
        if (dark)
        {
            Assert.IsTrue(workspaceLum < 0.25, $"dark: the workspace is a deep slate ({workspaceBg}, luminance {workspaceLum:0.00})");
        }
        else
        {
            Assert.AreNotEqual("rgb(255, 255, 255)", workspaceBg, "light: the workspace must differ from the white page");
            Assert.IsTrue(workspaceLum is > 0.85 and < 0.98, $"light: the workspace is a faint grey, not white and not dark ({workspaceBg}, luminance {workspaceLum:0.00})");
        }
    }

    private static async Task AssertSelectionUsesTheAccentAsync(IPage page, bool dark, bool indigo)
    {
        var expected = indigo
            ? (dark ? "rgb(129, 140, 248)" : "rgb(79, 70, 229)")
            : (dark ? "rgb(96, 165, 250)" : "rgb(37, 99, 235)");
        await page.WaitForFunctionAsync(
            "expected => { const r = document.querySelector('.tm-wd-editor .tm-wd-selection rect'); return r && getComputedStyle(r).stroke === expected; }",
            expected, new PageWaitForFunctionOptions { Timeout = 5_000 });
    }

    /// <summary>The minimap is a thumbnail of the paper page: paper card, accent viewport - in every theme.</summary>
    private static async Task AssertMinimapFollowsTheThemeAsync(IPage page, bool dark, bool indigo)
    {
        var expectedAccent = indigo
            ? (dark ? "rgb(129, 140, 248)" : "rgb(79, 70, 229)")
            : (dark ? "rgb(96, 165, 250)" : "rgb(37, 99, 235)");
        var data = await page.EvaluateAsync<System.Text.Json.JsonElement>(
            """
            () => {
                const map = document.querySelector('.tm-wd-editor .tm-wd-minimap');
                if (!map) return { present: false };
                const first = map.querySelector('svg > rect');
                const selected = map.querySelector('.tm-wd-minimap__box--selected');
                const viewport = map.querySelector('.tm-wd-minimap__viewport');
                return {
                    present: true,
                    page: getComputedStyle(first).fill,
                    selectedStroke: selected ? getComputedStyle(selected).stroke : null,
                    viewportStroke: viewport ? getComputedStyle(viewport).stroke : null
                };
            }
            """);
        if (!data.GetProperty("present").GetBoolean())
        {
            return;
        }

        Assert.AreEqual("rgb(255, 255, 255)", data.GetProperty("page").GetString(), "the minimap card is paper in every theme");
        if (data.GetProperty("selectedStroke").ValueKind == System.Text.Json.JsonValueKind.String)
            Assert.AreEqual(expectedAccent, data.GetProperty("selectedStroke").GetString(), "the selected box is the accent");
        if (data.GetProperty("viewportStroke").ValueKind == System.Text.Json.JsonValueKind.String)
            Assert.AreEqual(expectedAccent, data.GetProperty("viewportStroke").GetString(), "the viewport frame is the accent");
    }
    private static async Task AssertExportDialogAsync(IPage page, bool dark)
    {
        var cancel = page.Locator(".tm-modal button", new() { HasTextString = "Cancel" }).First;
        var cls = await cancel.GetAttributeAsync("class") ?? string.Empty;
        StringAssert.Contains(cls, "tm-btn-secondary", "Cancel is the Secondary (outline) variant");
        var surface = await page.Locator(".tm-modal").First.EvaluateAsync<string>("el => getComputedStyle(el).backgroundColor");
        if (dark)
            Assert.AreEqual("rgb(30, 41, 59)", surface, "dark: the dialog surface is --tm-bg-surface (no light plate)");
    }

    private static async Task AssertOpenDialogFollowsTheThemeAsync(IPage page, bool dark, bool indigo)
    {
        var expectedPrimary = indigo
            ? (dark ? "rgb(129, 140, 248)" : "rgb(79, 70, 229)")
            : (dark ? "rgb(96, 165, 250)" : "rgb(37, 99, 235)");

        // The active tree node ("All documents" is not active after navigating; "Designs" is) takes the primary.
        var color = await page.Locator(".tm-dod-tree-node--active").First.EvaluateAsync<string>("el => getComputedStyle(el).color");
        Assert.AreEqual(expectedPrimary, color, "the active folder is --tm-color-primary (no hard-coded indigo)");

        var rowBar = await page.Locator(".tm-dod-row--selected").First.EvaluateAsync<string>("el => getComputedStyle(el).boxShadow");
        StringAssert.Contains(rowBar, expectedPrimary, "the selected row carries the primary marker");

        if (dark)
        {
            var luminance = await page.Locator(".tm-dod-row").First.EvaluateAsync<double>(
                "el => { const m = getComputedStyle(el.closest('.tm-modal')).backgroundColor.match(/\\d+/g).map(Number); return (0.2126*m[0] + 0.7152*m[1] + 0.0722*m[2]) / 255; }");
            Assert.IsTrue(luminance < 0.25, $"dark: the dialog surface must be dark (luminance {luminance:0.00})");
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task<IPage> OpenAsync(string path, int width, int height, bool dark, bool indigo)
    {
        var context = await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = width, Height = height },
            Locale = "en-US",
            IgnoreHTTPSErrors = true,
            AcceptDownloads = true,
        });
        RegisterContext(context);

        var page = await context.NewPageAsync();
        await page.AddInitScriptAsync("localStorage.setItem('tm-demo-culture', 'en');"
            + (indigo ? " localStorage.setItem('tm-demo-color-theme', 'indigo');" : " localStorage.removeItem('tm-demo-color-theme');"));
        if (path == EditorUrl)
        {
            // Desktop container for the editor (layout resolves from the container; see the phase 6 notes).
            await page.AddInitScriptAsync(
                "document.addEventListener('DOMContentLoaded', () => { const s = document.createElement('style'); s.textContent = 'aside.fixed.inset-y-0.left-0.z-30 { display: none !important; } main { margin-left: 0 !important; width: 100vw !important; max-width: 100vw !important; }'; document.head.appendChild(s); });");
        }

        await page.GotoAsync($"{BaseUrl}{path}", new PageGotoOptions { WaitUntil = WaitUntilState.Load, Timeout = 60_000 });
        await WaitForAppReadyAsync(page);

        if (indigo)
        {
            await page.EvaluateAsync("() => document.documentElement.setAttribute('data-tm-theme', 'indigo')");
        }

        if (dark)
        {
            // The demo theme toggle drives ThemeService (data-theme on the layout wrapper), the real dark path.
            // The test hides the demo sidebar (it holds the toggle) so the editor gets a desktop container: dispatch the click on the hidden toggle.
            await page.Locator("[data-testid='theme-toggle']").First.DispatchEventAsync("click");
            await page.WaitForFunctionAsync(
                "() => document.querySelector('[data-theme=\"dark\"]') !== null",
                null, new PageWaitForFunctionOptions { Timeout = 10_000 });
        }

        return page;
    }

    /// <summary>Hides the minimap and the demo sidebar so neither covers the capture or intercepts a click.</summary>
    private static async Task HideDemoChromeAsync(IPage page) =>
        await page.EvaluateAsync("""
            () => {
                const style = document.createElement('style');
                style.textContent = '.tm-wd-editor__minimap-wrap { display: none !important; }';
                document.head.appendChild(style);
            }
            """);

    private static async Task SettleAnimationsAsync(IPage page)
    {
        await page.EvaluateAsync(
            """
            async () => {
                const animations = document.getAnimations();
                await Promise.all(animations.map(a => a.finished.catch(() => { })));
            }
            """);
        await page.Mouse.MoveAsync(0, 0);
    }

    private async Task Shot(ILocator locator, string directory, string name)
    {
        await locator.ScrollIntoViewIfNeededAsync();
        await locator.Page.Mouse.MoveAsync(0, 0);
        var path = Path.Combine(directory, name + ".png");
        await locator.ScreenshotAsync(new LocatorScreenshotOptions { Path = path, Type = ScreenshotType.Png, Timeout = 60_000 });
        TestContext.AddResultFile(path);
    }

    private async Task ShotPage(IPage page, string directory, string name)
    {
        var path = Path.Combine(directory, name + ".png");
        await page.ScreenshotAsync(new PageScreenshotOptions { Path = path, Type = ScreenshotType.Png, FullPage = false, Timeout = 60_000 });
        TestContext.AddResultFile(path);
    }

    private static string FindRepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "TempoBlazor.slnx")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate TempoBlazor.slnx.");
    }
}
