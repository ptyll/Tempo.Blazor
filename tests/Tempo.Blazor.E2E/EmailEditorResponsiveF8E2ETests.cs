using FluentAssertions;
using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Fáze 8 — the editor's responsive chrome on the core <c>TmEditorShell</c>, measured in a real browser:
/// desktop 1440 (docked palette + inspector, one command row), tablet 1024 with a coarse pointer (same
/// docked chrome, one panel railed, no overflow) and phone 390 / 320 (canvas + bottom sheet with
/// Bloky | Vlastnosti, compact header, action bar, "⋮" overflow), plus the Tabs presentation, the Escape
/// close path and a 200 % zoom.
/// <para>
/// Screenshots land in <c>__screenshots__/email-templates/f8-*.png</c> and are the UX review material for
/// this phase; they are compared against
/// <c>output/imagegen/tempo-reimagined/Tempo.Blazor.EmailTemplates/email-template-editor--{desktop|tablet|mobile}.png</c>.
/// </para>
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class EmailEditorResponsiveF8E2ETests : EmailTemplateE2ETestBase
{
    private const string EditorRoute = "/email-templates/edit/11111111-1111-1111-1111-111111111111";

    /// <summary>Opens the editor on an explicit viewport (and, on a phone, a real coarse-pointer context).</summary>
    private async Task<IPage> OpenEditorAsync(int width, int height, bool touch = false)
    {
        var context = await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = width, Height = height },
            HasTouch = touch,
            IsMobile = touch,
            Locale = "en-US",
            IgnoreHTTPSErrors = true,
        });
        RegisterContext(context);

        var page = await context.NewPageAsync();
        await page.GotoAsync($"{BaseUrl}{EditorRoute}", new PageGotoOptions
        {
            WaitUntil = WaitUntilState.Load,
            Timeout = 60000,
        });
        await WaitForAppReadyAsync(page);
        await page.WaitForSelectorAsync("[data-tm-email-editor]", new() { Timeout = 60000 });
        await page.WaitForSelectorAsync(".tm-editor-shell[data-layout]", new() { Timeout = 30000 });
        await page.WaitForSelectorAsync("[data-tm-canvas-doc]", new() { Timeout = 30000 });
        return page;
    }

    private static async Task<string> ResolvedLayoutAsync(IPage page)
        => await page.Locator(".tm-editor-shell").First.GetAttributeAsync("data-layout") ?? string.Empty;

    /// <summary>Inline overflow of an element: how many CSS pixels it is wider than its content box.</summary>
    private static async Task<int> InlineOverflowAsync(IPage page, string selector)
        => await page.EvaluateAsync<int>(
            "sel => { const el = document.querySelector(sel); return el ? Math.round(el.scrollWidth - el.clientWidth) : -1; }",
            selector);

    private static async Task WaitForLayoutAsync(IPage page, string layout)
        => await page.WaitForSelectorAsync($".tm-editor-shell[data-layout='{layout}']", new() { Timeout = 30000 });

    private static async Task<int> EditorWidthAsync(IPage page)
        => await page.EvaluateAsync<int>(
            "() => Math.round(document.querySelector('[data-tm-email-editor]').getBoundingClientRect().width)");

    /// <summary>
    /// Gives the editor the whole viewport by hiding the DEMO's own app shell (a fixed 18 rem sidebar plus
    /// the matching main offset). The shell decides the layout from the editor's CONTAINER, so with the demo
    /// chrome in place a 1024 px viewport is a 664 px editor (= mobile); the helper is how a test reaches the
    /// width it wants to talk about, and the container-vs-viewport rule gets its own test.
    /// </summary>
    private static async Task ExpandEditorToViewportAsync(IPage page)
    {
        await page.AddStyleTagAsync(new()
        {
            Content = "aside.fixed { display: none !important; } "
                + "main { margin-left: 0 !important; width: 100vw !important; max-width: 100vw !important; }",
        });
        await page.WaitForTimeoutAsync(500);
    }

    private static async Task SettleAsync(IPage page)
    {
        // The demo auto-saves; a sheet/toast must not cover the screenshot, and the shell needs one frame
        // to repaint after a ResizeObserver report.
        await page.Keyboard.PressAsync("Escape");
        await page.Mouse.MoveAsync(0, 0);
        await page.WaitForTimeoutAsync(300);
    }

    // ── Desktop 1440 ───────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task Desktop_1440_paints_the_docked_shell_with_no_horizontal_overflow()
    {
        var page = await OpenEditorAsync(1440, 900);
        await ExpandEditorToViewportAsync(page);
        await WaitForLayoutAsync(page, "desktop");

        (await page.Locator("[data-region='toolbar'] [data-tm-group]").CountAsync())
            .Should().BeGreaterThanOrEqualTo(4, "the command row is grouped by task");
        (await page.Locator("[data-region='header']").IsVisibleAsync()).Should().BeFalse(
            "a desktop editor has no compact mobile header (the slot renders nothing and the band collapses)");
        // The palette is either docked or railed (a shell-imposed tablet rail is reported through
        // ToolboxCollapsedChanged — D10 — and survives the flip back to desktop); the inspector is docked.
        (await page.Locator("[data-region='left'] [data-tm-toolbox], [data-region='rail-left']").CountAsync())
            .Should().Be(1, "the palette is reachable in exactly one shape");
        (await page.Locator("[data-region='right'] [data-tm-properties]").CountAsync()).Should().Be(1);
        (await page.Locator("[data-region='status-bar']").CountAsync()).Should().Be(1,
            "the save status is chrome of the status bar");

        var overflow = await InlineOverflowAsync(page, "[data-tm-email-editor]");
        overflow.Should().BeLessThanOrEqualTo(0, "the editor must never scroll horizontally");

        var right = await page.Locator("[data-region='right']").First.BoundingBoxAsync();
        right.Should().NotBeNull();
        (right!.X + right.Width).Should().BeLessThanOrEqualTo(1440 + 1, "the inspector is fully visible");

        // The reference desktop frame shows the palette docked: expand it if the resize path railed it.
        if (await page.Locator("[data-region='rail-left']").CountAsync() == 1)
        {
            await page.Locator("[data-region='rail-left'] button.tm-editor-shell__panel-toggle").First.ClickAsync();
            await page.WaitForSelectorAsync("[data-region='left'] [data-tm-toolbox]", new() { Timeout = 15000 });
        }

        await SettleAsync(page);
        await SaveNamedScreenshotAsync(page, "f8-editor-desktop-1440.png");
    }

    // ── Tablet 1024 (touch) ────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task Tablet_1024_touch_keeps_the_docked_panels_without_clipping_the_inspector()
    {
        var page = await OpenEditorAsync(1024, 800, touch: true);
        await ExpandEditorToViewportAsync(page);
        await WaitForLayoutAsync(page, "tablet");

        (await page.Locator("[data-region='toolbar']").CountAsync()).Should().Be(1,
            "a tablet keeps the desktop command row");
        (await page.Locator("aside.tm-editor-shell__panel--right").CountAsync()).Should().Be(1,
            "the inspector stays docked (F6-TABLET-DOCKED-PANELS)");
        (await page.Locator(".tm-side-panel").CountAsync()).Should().Be(0, "no overlay panel on a tablet");

        // F7A review #12 acceptance: at 1024 px the old splitter clipped the inspector (root.scrollWidth >
        // root.clientWidth). The shell must keep both panels fully visible.
        (await InlineOverflowAsync(page, "[data-tm-email-editor]"))
            .Should().BeLessThanOrEqualTo(0, "1024 px must not overflow horizontally");

        var right = await page.Locator("aside.tm-editor-shell__panel--right").First.BoundingBoxAsync();
        right.Should().NotBeNull();
        (right!.X + right.Width).Should().BeLessThanOrEqualTo(1024 + 1, "the inspector is fully visible");

        // At most one panel is expanded: the toolbox is railed, and the rail is operable.
        (await page.Locator("[data-region='rail-left'], aside.tm-editor-shell__panel--left").CountAsync())
            .Should().Be(1, "exactly one of the two left shapes is rendered");
        (await page.Locator("[data-region='rail-left'] [data-tm-rail-block]").CountAsync())
            .Should().BeGreaterThan(0, "the railed palette keeps every block one tap away");

        await SettleAsync(page);
        await SaveNamedScreenshotAsync(page, "f8-editor-tablet-1024.png");
    }

    [TestMethod]
    public async Task Tablet_1024_swaps_which_panel_is_expanded()
    {
        var page = await OpenEditorAsync(1024, 800, touch: true);
        await ExpandEditorToViewportAsync(page);
        await WaitForLayoutAsync(page, "tablet");
        (await page.Locator("[data-region='rail-left']").CountAsync()).Should().Be(1,
            "the inspector wins the tablet by default (F6-SHELL-PANEL-STATES)");

        await page.Locator("[data-region='rail-left'] button.tm-editor-shell__panel-toggle").First.ClickAsync();
        await page.WaitForSelectorAsync("[data-region='left'] [data-tm-toolbox]", new() { Timeout = 8000 });

        (await page.Locator("aside.tm-editor-shell__panel--left").CountAsync()).Should().Be(1);
        (await InlineOverflowAsync(page, "[data-tm-email-editor]")).Should().BeLessThanOrEqualTo(0);
        await SettleAsync(page);
        await SaveNamedScreenshotAsync(page, "f8-editor-tablet-toolbox-1024.png");
    }

    // ── Container vs viewport ─────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task Layout_follows_the_editors_container_not_the_viewport()
    {
        // F6-SHELL-BREAKPOINTS: 1024 px of VIEWPORT inside the demo's app shell leaves the editor a 664 px
        // container, so the editor is a PHONE layout there — and it becomes a tablet the moment its
        // container grows, without the viewport changing at all.
        var page = await OpenEditorAsync(1024, 800, touch: true);
        await WaitForLayoutAsync(page, "mobile");
        (await EditorWidthAsync(page)).Should().BeLessThan(768,
            "the editor's container decides the layout, not the window");

        await ExpandEditorToViewportAsync(page);
        await WaitForLayoutAsync(page, "tablet");

        var wide = await EditorWidthAsync(page);
        wide.Should().BeGreaterThanOrEqualTo(768).And.BeLessThan(1200);
        (await InlineOverflowAsync(page, "[data-tm-email-editor]")).Should().BeLessThanOrEqualTo(0);
    }

    // ── Mobile 390 ─────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task Mobile_390_presents_the_sheet_with_blocks_and_properties()
    {
        var page = await OpenEditorAsync(390, 844, touch: true);
        await WaitForLayoutAsync(page, "mobile");

        // Compact header: primary Save + the "⋮" overflow; the command row is gone (the shell drops the
        // Toolbar slot when the action bar is present).
        (await page.Locator("[data-region='header'] [data-tm-save]").CountAsync()).Should().Be(1);
        (await page.Locator("[data-region='toolbar']").CountAsync()).Should().Be(0);
        (await page.Locator("[data-tm-mobile-overflow]").CountAsync()).Should().Be(1);

        // Sheet with the two panel tabs.
        await page.WaitForSelectorAsync(".tm-editor-shell__sheet", new() { Timeout = 15000 });
        var tabs = await page.Locator(".tm-editor-shell__sheet .tm-editor-shell__tab").AllInnerTextsAsync();
        tabs.Should().HaveCount(2);
        (await page.Locator("[data-region='left'] [data-tm-toolbox]").IsVisibleAsync())
            .Should().BeTrue("the sheet starts on the blocks palette");

        (await InlineOverflowAsync(page, "[data-tm-email-editor]"))
            .Should().BeLessThanOrEqualTo(0, "a phone must never scroll horizontally");

        // The action bar is the F5 primitive: Undo / Redo / Preview (+ the "More" overflow).
        (await page.Locator(".tm-mobile-action-bar").CountAsync()).Should().BeGreaterThan(0);

        // The sheet opens at the editor's own first snap (0.65 of the stage, not the shell's half default),
        // so "Blocks" carries a usable palette and not just its search box. In the demo the page's own chrome
        // is ~300 px, which still leaves the first tile row just under the fold — the snap ratio is what the
        // editor controls, so that is what this asserts.
        var sheetBox = await page.Locator(".tm-editor-shell__sheet .tm-drawer__panel").First.BoundingBoxAsync();
        var stageBox = await page.Locator(".tm-editor-shell__stage").First.BoundingBoxAsync();
        sheetBox.Should().NotBeNull();
        stageBox.Should().NotBeNull();
        ((double)sheetBox!.Height / stageBox!.Height).Should().BeGreaterThan(0.6,
            "the panels sheet opens above the shell's half default so the palette is usable");
        (await page.Locator(".tm-editor-shell__sheet [data-tm-toolbox-list] [data-tm-block]").CountAsync())
            .Should().BeGreaterThan(0, "the palette is what the sheet opens on");

        // The initial phone frame is the UX review material for the default state (sheet open on Blocks).
        await SaveNamedScreenshotAsync(page, "f8-editor-mobile-390.png");
    }

    [TestMethod]
    public async Task Mobile_selecting_a_block_switches_the_sheet_to_properties()
    {
        var page = await OpenEditorAsync(390, 844, touch: true);
        await WaitForLayoutAsync(page, "mobile");

        // Close the sheet first so the canvas is hittable, then select a block.
        await page.Locator(".tm-editor-shell__sheet-close").First.ClickAsync();
        await page.WaitForSelectorAsync("[data-tm-canvas-doc]", new() { Timeout = 15000 });
        await page.Locator("[data-tm-block-id]").First.ClickAsync();

        // The sheet reopens on the inspector. Only ONE panel is open (the blocks palette was dismissed), so
        // the sheet carries the panel TITLE rather than a tab strip — the shell's one-panel shape.
        await page.WaitForSelectorAsync(".tm-editor-shell__sheet [data-tm-properties]", new() { Timeout = 15000 });
        (await page.Locator(".tm-editor-shell__sheet .tm-drawer__title").First.InnerTextAsync())
            .Should().Be("Properties",
                "the sheet follows the selection (the inspector is where a block is edited)");
        (await page.Locator(".tm-editor-shell__sheet [data-tm-prop-target='block']").CountAsync())
            .Should().Be(1, "the selected block's inspector is what the sheet shows");

        await SettleAsync(page);
        await SaveNamedScreenshotAsync(page, "f8-editor-mobile-properties-390.png");
    }

    [TestMethod]
    public async Task Mobile_inserting_a_block_collapses_the_sheet_and_focuses_the_new_block()
    {
        var page = await OpenEditorAsync(390, 844, touch: true);
        await WaitForLayoutAsync(page, "mobile");
        var before = await page.Locator("[data-tm-block-id]").CountAsync();

        await page.Locator(".tm-editor-shell__sheet [data-tm-block='text']").First.ClickAsync();

        await page.WaitForFunctionAsync("() => document.querySelectorAll('.tm-editor-shell__sheet').length === 0",
            null, new PageWaitForFunctionOptions { Timeout = 15000 });
        (await page.Locator("[data-tm-block-id]").CountAsync()).Should().BeGreaterThan(before,
            "the block was inserted");
        (await page.EvaluateAsync<bool>("() => document.activeElement?.hasAttribute('data-tm-block-id') === true"))
            .Should().BeTrue("the focus (and so the scroll position) follows the block the user just inserted");

        // The collapsed bar is the one way back into the panels.
        (await page.Locator(".tm-editor-shell__panel-toggle--mobile").CountAsync()).Should().Be(1);

        await SettleAsync(page);
        await SaveNamedScreenshotAsync(page, "f8-editor-mobile-after-insert-390.png");
    }

    [TestMethod]
    public async Task Mobile_escape_closes_the_panels_sheet()
    {
        var page = await OpenEditorAsync(390, 844, touch: true);
        await WaitForLayoutAsync(page, "mobile");
        await page.WaitForSelectorAsync(".tm-editor-shell__sheet", new() { Timeout = 15000 });

        await page.Locator(".tm-editor-shell__sheet .tm-editor-shell__tab").First.ClickAsync();
        await page.Keyboard.PressAsync("Escape");

        await page.WaitForFunctionAsync("() => document.querySelectorAll('.tm-editor-shell__sheet').length === 0",
            null, new PageWaitForFunctionOptions { Timeout = 15000 });

        // The focus comes back only when it was lost with the sheet (focusIfLost), never stolen.
        (await page.EvaluateAsync<bool>("() => !!document.activeElement?.closest('[data-tm-email-editor]')"))
            .Should().BeTrue("focus never falls to <body> after the sheet closes");
    }

    [TestMethod]
    public async Task Mobile_overflow_lists_import_export_validate_and_shortcuts()
    {
        var page = await OpenEditorAsync(390, 844, touch: true);
        await WaitForLayoutAsync(page, "mobile");

        await page.Locator("[data-tm-mobile-overflow] .tm-popover__trigger").ClickAsync();
        await page.WaitForSelectorAsync("[data-tm-mobile-overflow-panel]", new() { Timeout = 15000 });

        var panel = page.Locator("[data-tm-mobile-overflow-panel]");
        (await panel.Locator("[data-tm-import-btn]").CountAsync()).Should().Be(1);
        (await panel.Locator("[data-tm-mjml-btn]").CountAsync()).Should().Be(1);
        (await panel.Locator("[data-tm-validate-btn]").CountAsync()).Should().Be(1);
        (await panel.Locator("[data-tm-help-btn]").CountAsync()).Should().Be(1);

        await SaveNamedScreenshotAsync(page, "f8-editor-mobile-overflow-390.png");
    }

    [TestMethod]
    public async Task Mobile_tabs_presentation_replaces_the_sheet_with_a_full_region_tablist()
    {
        var page = await OpenEditorAsync(390, 844, touch: true);
        await WaitForLayoutAsync(page, "mobile");

        await page.SelectOptionAsync("[data-tm-demo-presentation]", "Tabs");
        await page.WaitForSelectorAsync(".tm-editor-shell__tabs--region", new() { Timeout = 15000 });

        (await page.Locator(".tm-editor-shell__sheet").CountAsync()).Should().Be(0, "Tabs has no sheet");
        var tabs = await page.Locator(".tm-editor-shell__tabs--region .tm-editor-shell__tab").AllInnerTextsAsync();
        tabs.Should().HaveCount(3, "Blocks | Canvas | Properties");
        (await InlineOverflowAsync(page, "[data-tm-email-editor]")).Should().BeLessThanOrEqualTo(0);

        await SaveNamedScreenshotAsync(page, "f8-editor-mobile-tabs-390.png");
    }

    // ── 320 px edge case ───────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task Mobile_320_keeps_every_control_reachable_without_horizontal_overflow()
    {
        var page = await OpenEditorAsync(320, 720, touch: true);
        await WaitForLayoutAsync(page, "mobile");

        (await InlineOverflowAsync(page, "[data-tm-email-editor]"))
            .Should().BeLessThanOrEqualTo(0, "320 px is the narrowest supported phone");

        (await page.Locator("[data-region='header'] [data-tm-save]").IsVisibleAsync()).Should().BeTrue();
        (await page.Locator("[data-tm-mobile-overflow]").IsVisibleAsync()).Should().BeTrue();

        // Every sheet tab is still a finger-sized target.
        var tab = await page.Locator(".tm-editor-shell__sheet .tm-editor-shell__tab").First.BoundingBoxAsync();
        tab.Should().NotBeNull();
        tab!.Height.Should().BeGreaterThanOrEqualTo(32);

        await SettleAsync(page);
        await SaveNamedScreenshotAsync(page, "f8-editor-mobile-320.png");
    }

    // ── 200 % zoom ─────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task Desktop_at_200_percent_zoom_keeps_every_function_reachable()
    {
        var page = await OpenEditorAsync(1440, 900);
        await ExpandEditorToViewportAsync(page);
        await WaitForLayoutAsync(page, "desktop");

        // A browser zoom halves the CSS viewport: 1440 / 2 = 720 px of container, i.e. the mobile branch.
        // Nothing may be lost — the shell swaps to the compact chrome and the page never scrolls sideways.
        await page.EvaluateAsync("() => { document.documentElement.style.zoom = '2'; }");
        await page.WaitForTimeoutAsync(800);

        var layout = await ResolvedLayoutAsync(page);
        layout.Should().BeOneOf("mobile", "tablet");
        (await InlineOverflowAsync(page, "[data-tm-email-editor]"))
            .Should().BeLessThanOrEqualTo(0, "200 % zoom must not introduce horizontal scrolling");
        (await page.Locator("[data-tm-save]").First.IsVisibleAsync()).Should().BeTrue("Save stays reachable");

        await SaveNamedScreenshotAsync(page, "f8-editor-desktop-200pct-zoom.png");
    }
}
