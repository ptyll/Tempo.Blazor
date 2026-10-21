using FluentAssertions;
using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Fáze 8 — the editor's responsive chrome on the core <c>TmEditorShell</c>, measured in a real browser:
/// desktop 1440 (docked palette + inspector, one command row), tablet 1024 with a coarse pointer (same
/// docked chrome, one panel railed, no overflow) and phone 390 / 320 (canvas + bottom sheet with
/// Bloky | Vlastnosti, compact header, action bar, "⋮" overflow), plus the Tabs presentation, both Escape
/// paths and a 200 % zoom.
/// <para>
/// Screenshots land in <c>__screenshots__/email-templates/f8-*.png</c> and are the UX review material for
/// this phase; they are compared against
/// <c>output/imagegen/tempo-reimagined/Tempo.Blazor.EmailTemplates/email-template-editor--{desktop|tablet|mobile}.png</c>.
/// Review (item 8): they are VIEWPORT captures, not full page, and the phone ones hide the demo's own
/// switch bar (the page folds it into a disclosure below the editor's mobile breakpoint).
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

    /// <summary>
    /// Switches the demo (and therefore the shell) to the dark theme. The toggle lives in the demo's app
    /// sidebar, which <see cref="ExpandEditorToViewportAsync"/> HIDES to give the editor the whole viewport —
    /// so the click is dispatched programmatically on the button itself (the Blazor handler runs either way).
    /// </summary>
    private static async Task GoDarkAsync(IPage page)
    {
        var clicked = await page.EvaluateAsync<bool>(
            "() => { const b = document.querySelector(`[data-testid='theme-toggle']`); if (!b) return false; b.click(); return true; }");
        clicked.Should().BeTrue("the demo host renders the theme toggle");
        await page.WaitForSelectorAsync("[data-theme='dark']", new() { Timeout = 15000 });
        await page.WaitForTimeoutAsync(300);
    }

    /// <summary>
    /// The F5 action bar carries exactly the D20 commands (Undo / Redo / Preview / Validate) and no "More":
    /// the editor raises the shell's <c>MobileActionsMaxVisible</c> to 4, so nothing hides behind a menu.
    /// </summary>
    private static async Task AssertFourActionBarCommandsAsync(IPage page)
    {
        (await page.Locator(".tm-mobile-action-bar").CountAsync()).Should().BeGreaterThan(0);
        var ids = await page.Locator(".tm-mobile-action-bar__action").EvaluateAllAsync<string[]>(
            "els => els.map(e => e.getAttribute('data-action-id'))");
        ids.Should().Equal(["undo", "redo", "preview", "validate"],
            "D20 names exactly four bar commands and MobileActionsMaxVisible=4 keeps all of them on the bar");
        (await page.Locator(".tm-mobile-action-bar__more").CountAsync()).Should().Be(0,
            "with a four-slot budget there is nothing left for the bar's own More menu");
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
        // The palette is docked (a tablet auto-rail is no longer reported as a preference — U1 — so a
        // desktop container has both panels open); the inspector is docked.
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

        // The reference desktop frame shows the palette docked.
        if (await page.Locator("[data-region='rail-left']").CountAsync() == 1)
        {
            await page.Locator("[data-region='rail-left'] button.tm-editor-shell__panel-toggle").First.ClickAsync();
            await page.WaitForSelectorAsync("[data-region='left'] [data-tm-toolbox]", new() { Timeout = 15000 });
        }

        await SettleAsync(page);
        await SaveNamedViewportScreenshotAsync(page, "f8-editor-desktop-1440.png");
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

        // At most one panel is expanded: the toolbox is railed, and the rail is named and operable.
        (await page.Locator("[data-region='rail-left'], aside.tm-editor-shell__panel--left").CountAsync())
            .Should().Be(1, "exactly one of the two left shapes is rendered");
        (await page.Locator("[data-region='rail-left'] [data-tm-rail-block]").CountAsync())
            .Should().BeGreaterThan(0, "the railed palette keeps every block one tap away");
        (await page.Locator("[data-region='rail-left']").First.GetAttributeAsync("role")).Should().Be("group",
            "the rail is a named group of blocks, not a run of unnamed buttons");
        (await page.Locator("[data-region='rail-left']").First.GetAttributeAsync("aria-label"))
            .Should().Be("Blocks", "the rail carries the panel's own title as its accessible name");

        await SettleAsync(page);
        await SaveNamedViewportScreenshotAsync(page, "f8-editor-tablet-1024.png");
    }

    [TestMethod]
    public async Task Tablet_1024_swaps_which_panel_is_expanded_and_offers_the_inspector_rail()
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

        // Review item 15: the now-railed INSPECTOR keeps its own way back instead of being a dead end.
        var propertiesRail = page.Locator("[data-tm-properties-rail]");
        (await propertiesRail.CountAsync()).Should().Be(1, "the railed inspector renders its own rail");
        var expand = propertiesRail.Locator("[data-tm-expand-properties]");
        (await expand.GetAttributeAsync("aria-label")).Should().Be("Properties");
        var box = await expand.BoundingBoxAsync();
        box.Should().NotBeNull();
        box!.Height.Should().BeGreaterThanOrEqualTo(44, "a tablet is a coarse pointer: the target is finger-sized");

        await expand.ClickAsync();
        await page.WaitForSelectorAsync("aside.tm-editor-shell__panel--right", new() { Timeout = 8000 });

        await SettleAsync(page);
        await SaveNamedViewportScreenshotAsync(page, "f8-editor-tablet-toolbox-1024.png");
    }

    [TestMethod]
    public async Task Tablet_1024_keyboard_expansion_moves_focus_to_the_expanded_panels_toggle()
    {
        var page = await OpenEditorAsync(1024, 800, touch: true);
        await ExpandEditorToViewportAsync(page);
        await WaitForLayoutAsync(page, "tablet");
        (await page.Locator("[data-region='rail-left']").CountAsync()).Should().Be(1,
            "the inspector wins the tablet by default (F6-SHELL-PANEL-STATES)");

        // ── The palette's rail: Enter on the rail toggle expands the docked panel ────────────
        await page.Locator("[data-region='rail-left'] button").First.FocusAsync();
        await page.Keyboard.PressAsync("Enter");
        await page.WaitForSelectorAsync("[data-region='left'] [data-tm-toolbox]", new() { Timeout = 15000 });

        // Review item 18c: the focused rail button unmounted with the rail, so the focus follows the
        // panel it opened — onto the expanded panel's own collapse toggle, never onto <body>.
        (await ExpandedPanelToggleFocusedAsync(page, "left")).Should().BeTrue(
            "Enter on the palette's rail toggle must land the focus on the expanded left panel's toggle");

        // ── The inspector's rail: the same trip for the panel the host owns ─────────────────
        var expandProperties = page.Locator("[data-tm-properties-rail] [data-tm-expand-properties]");
        (await expandProperties.CountAsync()).Should().Be(1,
            "the now-railed inspector offers its own way back (review item 15)");
        await expandProperties.FocusAsync();
        await page.Keyboard.PressAsync("Enter");
        await page.WaitForSelectorAsync("aside.tm-editor-shell__panel--right", new() { Timeout = 15000 });

        (await ExpandedPanelToggleFocusedAsync(page, "right")).Should().BeTrue(
            "Enter on the inspector's rail button must land the focus on the expanded right panel's toggle");
    }

    /// <summary>
    /// Whether the focus sits on the EXPANDED panel's toggle for <paramref name="side"/>: the toggle class
    /// with <c>aria-expanded="true"</c>, outside any rail (the rail's own toggle reports collapsed).
    /// </summary>
    private static async Task<bool> ExpandedPanelToggleFocusedAsync(IPage page, string side)
        => await page.EvaluateAsync<bool>(
            $$"""
            () => {
                const a = document.activeElement;
                if (!a) return false;
                return a.classList.contains('tm-editor-shell__panel-toggle--{{side}}')
                    && a.getAttribute('aria-expanded') === 'true'
                    && !a.closest('[data-region^="rail"]');
            }
            """);

    [TestMethod]
    public async Task Tablet_1024_dark_mode_keeps_the_docked_chrome_readable()
    {
        // Auto layout (the demo seeds no forced mode): the shell measures its own container at 1024 px.
        var page = await OpenEditorAsync(1024, 800, touch: true);
        await ExpandEditorToViewportAsync(page);
        await WaitForLayoutAsync(page, "tablet");

        await GoDarkAsync(page);

        (await ResolvedLayoutAsync(page)).Should().Be("tablet", "the dark capture stays in the Auto layout");
        (await page.Locator("[data-theme='dark']").CountAsync()).Should().BeGreaterThan(0);
        (await InlineOverflowAsync(page, "[data-tm-email-editor]")).Should().BeLessThanOrEqualTo(0);

        var surface = await page.EvaluateAsync<string>(
            "() => getComputedStyle(document.querySelector('.tm-editor-shell__panel--right')).backgroundColor");
        surface.Should().NotBe("rgba(0, 0, 0, 0)", "the docked inspector paints a dark surface, not the light one");

        await SettleAsync(page);
        await SaveNamedViewportScreenshotAsync(page, "f8-editor-tablet-dark-1024.png");
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

        // Review item 13: without a host ToolbarStart the header names the DOCUMENT, and Save + ⋮ sit right.
        var title = page.Locator("[data-tm-mobile-title]");
        (await title.CountAsync()).Should().Be(1,
            "the demo host supplies no ToolbarStart, so the header shows the template name");
        (await title.InnerTextAsync()).Should().NotBeNullOrWhiteSpace();
        var titleBox = await title.BoundingBoxAsync();
        var saveBox = await page.Locator("[data-region='header'] [data-tm-save]").First.BoundingBoxAsync();
        titleBox.Should().NotBeNull();
        saveBox.Should().NotBeNull();
        (titleBox!.X + titleBox.Width).Should().BeLessThanOrEqualTo(saveBox!.X + 1,
            "the title yields its space to the pinned Save/⋮ group");

        // The demo folds its own switch bar away on a phone (item 9), so the editor owns the stage.
        (await page.Locator("[data-tm-demo-controls]").CountAsync()).Should().Be(1,
            "the demo's switches live in a disclosure below the editor's mobile breakpoint");
        (await page.Locator("[data-tm-demo-controls][open]").CountAsync()).Should().Be(0,
            "and it starts collapsed: the stage belongs to the editor");

        // The action bar is the F5 primitive with all four D20 commands.
        await AssertFourActionBarCommandsAsync(page);

        // Review item 9: the sheet's default snap shows REAL TILES — the first block is hit-testable at its
        // centre and sits above the action bar's top edge (it used to be just under the fold).
        var tile = page.Locator(".tm-editor-shell__sheet [data-tm-toolbox-list] [data-tm-block]").First;
        (await tile.CountAsync()).Should().BeGreaterThan(0, "the palette is what the sheet opens on");
        var tileReachable = await page.EvaluateAsync<bool>(
            @"() => {
                const tile = document.querySelector('.tm-editor-shell__sheet [data-tm-toolbox-list] [data-tm-block]');
                if (!tile) return false;
                const rect = tile.getBoundingClientRect();
                const bar = document.querySelector('.tm-mobile-action-bar');
                const barTop = bar ? bar.getBoundingClientRect().top : window.innerHeight;
                if (rect.top < 0 || rect.bottom > barTop) return false;
                const hit = document.elementFromPoint(rect.left + rect.width / 2, rect.top + rect.height / 2);
                return !!hit && (hit === tile || tile.contains(hit));
            }");
        tileReachable.Should().BeTrue(
            "the default snap must show a real tile above the action bar, not only the tabs and the search box");

        // Review item 10: the save state keeps a READABLE label on a phone (no icon-only chip). A clean,
        // never-saved editor renders no badge at all, so the state is produced by an actual save first.
        await page.Locator("[data-region='header'] [data-tm-save]").ClickAsync();
        await page.WaitForSelectorAsync("[data-tm-save-badge]", new() { Timeout = 20000 });
        var saveLabel = page.Locator(".tm-email-editor__save-label").First;
        (await saveLabel.IsVisibleAsync())
            .Should().BeTrue("the save state is a word on a phone, not a 24px severity dot");
        var saveLabelBox = await saveLabel.BoundingBoxAsync();
        saveLabelBox.Should().NotBeNull();
        saveLabelBox!.Width.Should().BeGreaterThan(24,
            "the label must be able to say 'Saved just now', not collapse to a glyph");
        (await page.Locator(".tm-email-editor__save-message").First.IsVisibleAsync())
            .Should().BeFalse("only the host's free-text reason is dropped on a phone");

        (await InlineOverflowAsync(page, "[data-tm-email-editor]"))
            .Should().BeLessThanOrEqualTo(0, "a phone must never scroll horizontally");

        // The initial phone frame is the UX review material for the default state (sheet open on Blocks).
        await SaveNamedViewportScreenshotAsync(page, "f8-editor-mobile-390.png");
    }

    [TestMethod]
    public async Task Mobile_390_dark_mode_keeps_the_compact_chrome_readable()
    {
        var page = await OpenEditorAsync(390, 844, touch: true);
        await WaitForLayoutAsync(page, "mobile");

        await GoDarkAsync(page);

        (await ResolvedLayoutAsync(page)).Should().Be("mobile", "the dark capture stays in the Auto layout");
        (await InlineOverflowAsync(page, "[data-tm-email-editor]")).Should().BeLessThanOrEqualTo(0);
        (await page.Locator("[data-tm-mobile-title]").IsVisibleAsync()).Should().BeTrue();
        await AssertFourActionBarCommandsAsync(page);

        await SaveNamedViewportScreenshotAsync(page, "f8-editor-mobile-dark-390.png");
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
        // Review item 8: the capture has to show the BLOCK's inspector, not an empty properties panel —
        // and item 9: the sheet's own tab already names the panel, so no second heading may repeat it.
        (await page.Locator(".tm-editor-shell__sheet [data-tm-properties] .tm-email-properties__title").First.InnerTextAsync())
            .Should().NotBeNullOrWhiteSpace("the inspector names the selected block's type");
        (await page.Locator(".tm-editor-shell__sheet [data-tm-properties] .tm-email-editor__panel-header").First.IsVisibleAsync())
            .Should().BeFalse("on a phone the panel header is hidden: the sheet tab is the title (item 9)");

        await SaveNamedViewportScreenshotAsync(page, "f8-editor-mobile-properties-390.png");
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
        await SaveNamedViewportScreenshotAsync(page, "f8-editor-mobile-after-insert-390.png");
    }

    [TestMethod]
    public async Task Mobile_tabs_inserting_a_block_activates_the_canvas_tab_and_focuses_it()
    {
        var page = await OpenEditorAsync(390, 844, touch: true);
        await WaitForLayoutAsync(page, "mobile");

        await OpenDemoControlsAsync(page);
        await page.SelectOptionAsync("[data-tm-demo-presentation]", "Tabs");
        await page.WaitForSelectorAsync(".tm-editor-shell__tabs--region", new() { Timeout = 15000 });

        // Review item 2: the Tabs presentation has NO sheet to close, so an insert has to select the CANVAS
        // tab — otherwise the new block lands on a canvas the user cannot see.
        await page.Locator(".tm-editor-shell__tabs--region .tm-editor-shell__tab").First.ClickAsync();
        await page.WaitForSelectorAsync("[data-region='left']:not([hidden]) [data-tm-toolbox]", new() { Timeout = 15000 });

        await page.Locator("[data-region='left'] [data-tm-block='text']").First.ClickAsync();

        await page.WaitForFunctionAsync(
            "() => document.activeElement?.hasAttribute('data-tm-block-id') === true",
            null, new PageWaitForFunctionOptions { Timeout = 15000 });
        var canvasTabSelected = await page.EvaluateAsync<bool>(
            "() => { const t = document.querySelector(\"[data-region='canvas']\"); "
            + "return !!t && !t.hasAttribute('hidden'); }");
        canvasTabSelected.Should().BeTrue("the insert reveals the canvas tab");

        await SaveNamedViewportScreenshotAsync(page, "f8-editor-mobile-tabs-after-insert-390.png");
    }

    [TestMethod]
    public async Task Mobile_escape_from_a_sheet_tab_moves_focus_to_the_panels_toggle()
    {
        var page = await OpenEditorAsync(390, 844, touch: true);
        await WaitForLayoutAsync(page, "mobile");
        await page.WaitForSelectorAsync(".tm-editor-shell__sheet", new() { Timeout = 15000 });

        await page.Locator(".tm-editor-shell__sheet .tm-editor-shell__tab").First.ClickAsync();
        await page.Keyboard.PressAsync("Escape");

        await page.WaitForFunctionAsync("() => document.querySelectorAll('.tm-editor-shell__sheet').length === 0",
            null, new PageWaitForFunctionOptions { Timeout = 15000 });

        // Review item 18a: the focused tab unmounted with the sheet, so the shell re-asserts the focus on
        // the closed-state bar — never leaving it on <body>.
        (await page.EvaluateAsync<bool>(
            "() => document.activeElement?.classList.contains('tm-editor-shell__panel-toggle--mobile') === true"))
            .Should().BeTrue("Escape from a sheet tab lands on the panels toggle, not on <body>");
    }

    [TestMethod]
    public async Task Mobile_escape_from_the_canvas_leaves_the_focus_alone()
    {
        var page = await OpenEditorAsync(390, 844, touch: true);
        await WaitForLayoutAsync(page, "mobile");

        await page.Locator(".tm-editor-shell__sheet-close").First.ClickAsync();
        await page.WaitForSelectorAsync("[data-tm-canvas-doc]", new() { Timeout = 15000 });
        var block = page.Locator("[data-tm-block-id]").First;
        await block.ClickAsync();
        (await page.EvaluateAsync<bool>("() => document.activeElement?.hasAttribute('data-tm-block-id') === true"))
            .Should().BeTrue("the tap put the focus on the block (the sheet then reopened on the inspector)");

        await page.Locator(".tm-editor-shell__sheet-close").First.ClickAsync();
        await page.WaitForSelectorAsync("[data-tm-canvas-doc]", new() { Timeout = 15000 });
        await page.Locator("[data-tm-block-id]").First.FocusAsync();
        await page.Keyboard.PressAsync("Escape");

        // Review item 18b: the canvas is not inside any overlay, so Escape changes nothing — the focus stays
        // exactly where the user put it (no focus theft, no scroll jump).
        (await page.EvaluateAsync<bool>("() => document.activeElement?.hasAttribute('data-tm-block-id') === true"))
            .Should().BeTrue("Escape on the canvas must not move the focus");
    }

    [TestMethod]
    public async Task Mobile_overflow_lists_import_export_validate_and_shortcuts_without_a_second_preview()
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
        // Review item 14: Preview is one of the four action-bar commands, so it is NOT offered twice here.
        (await panel.Locator("[data-tm-preview-btn]").CountAsync()).Should().Be(0);

        // Review item 12: every row is a finger-sized, full-width menu item.
        var row = panel.Locator("[data-tm-import-btn]").First;
        var rowBox = await row.BoundingBoxAsync();
        var panelBox = await panel.BoundingBoxAsync();
        rowBox.Should().NotBeNull();
        panelBox.Should().NotBeNull();
        rowBox!.Height.Should().BeGreaterThanOrEqualTo(44, "a coarse pointer needs a 44px row");
        (panelBox!.Width - rowBox.Width).Should().BeLessThan(24,
            "the rows fill the panel instead of hugging their label");

        await SaveNamedViewportScreenshotAsync(page, "f8-editor-mobile-overflow-390.png");
    }

    [TestMethod]
    public async Task Mobile_tabs_presentation_replaces_the_sheet_with_a_full_region_tablist()
    {
        var page = await OpenEditorAsync(390, 844, touch: true);
        await WaitForLayoutAsync(page, "mobile");

        await OpenDemoControlsAsync(page);
        await page.SelectOptionAsync("[data-tm-demo-presentation]", "Tabs");
        await page.WaitForSelectorAsync(".tm-editor-shell__tabs--region", new() { Timeout = 15000 });

        (await page.Locator(".tm-editor-shell__sheet").CountAsync()).Should().Be(0, "Tabs has no sheet");
        var tabs = await page.Locator(".tm-editor-shell__tabs--region .tm-editor-shell__tab").AllInnerTextsAsync();
        tabs.Should().HaveCount(3, "Blocks | Canvas | Properties");
        // U3 (jádro): a tab is primary navigation on a phone and is finger-sized under a coarse pointer.
        var tabBox = await page.Locator(".tm-editor-shell__tabs--region .tm-editor-shell__tab").First.BoundingBoxAsync();
        tabBox.Should().NotBeNull();
        tabBox!.Height.Should().BeGreaterThanOrEqualTo(44, "a sheet/Tabs tab is a --tm-touch-target control");
        (await InlineOverflowAsync(page, "[data-tm-email-editor]")).Should().BeLessThanOrEqualTo(0);

        await SaveNamedViewportScreenshotAsync(page, "f8-editor-mobile-tabs-390.png");
    }

    // ── 320 px edge case ───────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task Mobile_320_keeps_every_control_reachable_without_horizontal_overflow()
    {
        var page = await OpenEditorAsync(320, 720, touch: true);
        await WaitForLayoutAsync(page, "mobile");

        (await InlineOverflowAsync(page, "[data-tm-email-editor]"))
            .Should().BeLessThanOrEqualTo(0, "320 px is the narrowest supported phone");
        (await page.EvaluateAsync<bool>(
            "() => document.documentElement.scrollWidth <= document.documentElement.clientWidth + 1"))
            .Should().BeTrue("the demo page itself must not scroll sideways at 320 px (item 17)");

        (await page.Locator("[data-region='header'] [data-tm-save]").IsVisibleAsync()).Should().BeTrue();
        (await page.Locator("[data-tm-mobile-overflow]").IsVisibleAsync()).Should().BeTrue();

        // U3 (jádro): every sheet tab is a finger-sized target, not a 32px word.
        var tab = await page.Locator(".tm-editor-shell__sheet .tm-editor-shell__tab").First.BoundingBoxAsync();
        tab.Should().NotBeNull();
        tab!.Height.Should().BeGreaterThanOrEqualTo(44,
            "--tm-touch-target under a coarse pointer (2.10.0: .tm-editor-shell__tab min-block-size)");

        // Item 12: Save is finger-sized too.
        var save = await page.Locator("[data-region='header'] [data-tm-save]").First.BoundingBoxAsync();
        save.Should().NotBeNull();
        save!.Height.Should().BeGreaterThanOrEqualTo(44);

        await SettleAsync(page);
        await SaveNamedViewportScreenshotAsync(page, "f8-editor-mobile-320.png");
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

        await SaveNamedViewportScreenshotAsync(page, "f8-editor-desktop-200pct-zoom.png");
    }
}
