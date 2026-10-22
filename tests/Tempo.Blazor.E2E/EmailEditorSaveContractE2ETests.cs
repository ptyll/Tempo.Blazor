using FluentAssertions;
using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Fáze 7 — the editor's save contract in a real browser, on the live WASM demo (7106) + demo API (5100).
/// <para>
/// The editor may never show "Saved" without a confirmed host write, so every state here is produced by the
/// demo host's own answer: the demo page's "Host save result" switch simulates a failure and a conflict
/// (§3.2, the simulated Failed/Conflict host), everything else goes through the real API. Screenshots land
/// in <c>__screenshots__/email-templates/</c> for the UX review.
/// </para>
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class EmailEditorSaveContractE2ETests : EmailTemplateE2ETestBase
{
    /// <summary>Opens a FRESH template so no other spec's seed is mutated, and returns the editor page.</summary>
    private async Task<IPage> OpenFreshEditorAsync()
    {
        var page = await OpenAsync("/email-templates");
        await page.Locator("[data-tm-new-template]").ClickAsync();
        await page.WaitForSelectorAsync("[data-tm-email-editor]", new() { Timeout = 30000 });
        await page.WaitForSelectorAsync("[data-tm-canvas-doc]", new() { Timeout = 30000 });
        // These cases are about the SAVE contract on the desktop chrome; the narrow-layout case switches the
        // editor back to Auto itself.
        await ForceEditorLayoutAsync(page, "Desktop");
        return page;
    }

    /// <summary>
    /// Inserts a text block through whatever palette surface the current layout shows — the docked toolbox,
    /// the rail, or the mobile panels sheet (which an insert collapses again). Leaves the document dirty.
    /// </summary>
    private static async Task AddBlockFromPaletteAsync(IPage page)
    {
        var tile = page.Locator("[data-tm-toolbox-list] [data-tm-block='text']");
        if (await tile.CountAsync() == 0)
        {
            // Collapsed to a rail: expand it first.
            var railToggle = page.Locator("[data-region='rail-left'] button.tm-editor-shell__panel-toggle");
            if (await railToggle.CountAsync() > 0)
            {
                await railToggle.First.ClickAsync();
                await page.WaitForTimeoutAsync(400);
            }
        }

        if (await tile.CountAsync() == 0)
        {
            // Mobile: open the panels sheet (it starts on the palette).
            var bar = page.Locator(".tm-editor-shell__panel-toggle--mobile");
            if (await bar.CountAsync() > 0)
            {
                await bar.First.ClickAsync();
                await page.WaitForTimeoutAsync(400);
            }
        }

        await page.Locator("[data-tm-block='text']").First.ClickAsync();
        await page.WaitForFunctionAsync(
            "() => document.querySelector('[data-tm-email-editor]')?.getAttribute('data-tm-dirty') === 'true'",
            null, new() { Timeout = 15000 });
    }

    private static async Task EditSubjectAsync(IPage page, string value)
    {
        var subject = page.Locator("[data-tm-prop='Subject'] input");
        await subject.FillAsync(value);
        await subject.BlurAsync();
        await page.WaitForFunctionAsync(
            "() => document.querySelector('[data-tm-email-editor]')?.getAttribute('data-tm-dirty') === 'true'",
            null, new() { Timeout = 15000 });
    }

    private static async Task<string> BadgeStateAsync(IPage page)
        => await page.Locator("[data-tm-save-badge]").GetAttributeAsync("data-tm-save-state") ?? "(none)";

    private static async Task ChooseHostResultAsync(IPage page, string value)
    {
        // The demo folds its switches into a disclosure while the editor is a PHONE layout (item 9).
        await OpenDemoControlsAsync(page);
        await page.Locator("[data-tm-demo-simulate]").SelectOptionAsync(value);
    }

    // ── manual save → Saved ─────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task F7_1_ManualSave_ShowsSavedBadge_AndClearsTheDirtyFlag()
    {
        var page = await OpenFreshEditorAsync();

        await EditSubjectAsync(page, $"Saved by F7.1 {Guid.NewGuid():N}");
        (await BadgeStateAsync(page)).Should().Be("dirty", "an edit is announced before any save happens");
        (await page.Locator("[data-tm-save-badge]").InnerTextAsync())
            .Should().Contain("Unsaved changes", "the badge is a localized phrase, not a key");
        await SaveNamedScreenshotAsync(page, "30-save-dirty.png");

        await page.Locator("[data-tm-save]").ClickAsync();

        await page.WaitForSelectorAsync("[data-tm-save-badge][data-tm-save-state='saved']", new() { Timeout = 20000 });
        (await page.Locator("[data-tm-email-editor]").GetAttributeAsync("data-tm-dirty"))
            .Should().Be("false", "a confirmed write clears the dirty flag");
        (await page.Locator("[data-tm-save-badge]").InnerTextAsync())
            .Should().Contain("Saved", "the badge reports the host's confirmation");
        (await page.Locator("[data-tm-save]").IsDisabledAsync())
            .Should().BeFalse("the save button is only disabled while a request is in flight");
        await SaveNamedScreenshotAsync(page, "31-save-saved.png");
    }

    // ── failed save ─────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task F7_2_FailedSave_ShowsTheHostMessage_AndStaysDirty()
    {
        var page = await OpenFreshEditorAsync();
        await ChooseHostResultAsync(page, "failed");

        await EditSubjectAsync(page, $"Rejected by F7.2 {Guid.NewGuid():N}");
        await page.Locator("[data-tm-save]").ClickAsync();

        await page.WaitForSelectorAsync("[data-tm-save-badge][data-tm-save-state='failed']", new() { Timeout = 20000 });
        (await page.Locator("[data-tm-save-badge]").InnerTextAsync())
            .Should().Contain("Simulated failure", "the host's reason is what the user acts on");
        (await page.Locator("[data-tm-email-editor]").GetAttributeAsync("data-tm-dirty"))
            .Should().Be("true", "a failed save leaves the document dirty — never 'Saved'");
        (await page.Locator("[data-tm-demo-dirty]").GetAttributeAsync("data-tm-demo-dirty"))
            .Should().Be("true", "the host also sees IsDirtyChanged(true)");
        await SaveNamedScreenshotAsync(page, "32-save-failed.png");

        // Edge case: the host recovers. The next save must clear the failure badge.
        await ChooseHostResultAsync(page, "real");
        await page.Locator("[data-tm-save]").ClickAsync();
        await page.WaitForSelectorAsync("[data-tm-save-badge][data-tm-save-state='saved']", new() { Timeout = 20000 });
        await SaveNamedScreenshotAsync(page, "33-save-after-failure.png");
    }

    // ── conflict ────────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task F7_3_Conflict_ShowsTheConflict_AndPausesAutoSave()
    {
        var page = await OpenFreshEditorAsync();
        await ChooseHostResultAsync(page, "conflict");

        await EditSubjectAsync(page, $"Stale by F7.3 {Guid.NewGuid():N}");
        await page.Locator("[data-tm-save]").ClickAsync();

        await page.WaitForSelectorAsync("[data-tm-save-badge][data-tm-save-state='conflict']", new() { Timeout = 20000 });
        (await page.Locator("[data-tm-save-badge]").InnerTextAsync())
            .Should().Contain("Simulated conflict");
        (await page.Locator("[data-tm-email-editor]").GetAttributeAsync("data-tm-dirty")).Should().Be("true");
        await SaveNamedScreenshotAsync(page, "34-save-conflict.png");

        // Auto-save (3 s debounce in the demo) is paused after a conflict: another edit must NOT produce
        // more requests. The badge staying on "conflict" is the observable proof.
        await EditSubjectAsync(page, $"Stale again by F7.3 {Guid.NewGuid():N}");
        await page.WaitForTimeoutAsync(7000);
        (await BadgeStateAsync(page)).Should().Be("conflict",
            "a conflict pauses auto-save until a successful manual save or a new document instance");
    }

    // ── Ctrl/⌘+S ────────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task F7_4_CtrlS_Saves_AndCancelsTheBrowserSavePageDefault()
    {
        var page = await OpenFreshEditorAsync();

        // Record the browser's view of the chord: the package's JS module must preventDefault it, so the
        // browser never opens its own "save page" dialog on top of the editor's save.
        await page.EvaluateAsync(
            """
            () => {
                window.__tmSaveChord = null;
                window.addEventListener('keydown', (event) => {
                    if (event.key === 's' || event.key === 'S') {
                        setTimeout(() => { window.__tmSaveChord = event.defaultPrevented; }, 0);
                    }
                }, true);
            }
            """);

        await EditSubjectAsync(page, $"Shortcut by F7.4 {Guid.NewGuid():N}");
        (await BadgeStateAsync(page)).Should().Be("dirty");

        // A keystroke goes to the FOCUSED element: after the blur above focus is on <body>, outside the
        // editor root that owns the chord. Click into the canvas first (a real user is editing there), then
        // send the chord and prove it reached the host with the PUT it caused.
        var saveRequest = page.WaitForRequestAsync(
            request => request.Method == "PUT" && request.Url.Contains("/api/email-templates", StringComparison.Ordinal),
            new() { Timeout = 20000 });
        await page.Locator("[data-tm-canvas-doc]").ClickAsync();
        await page.Keyboard.PressAsync("Control+s");

        var put = await saveRequest;
        put.Method.Should().Be("PUT", "Ctrl+S must reach OnSave, i.e. the host's write");
        await page.WaitForSelectorAsync("[data-tm-save-badge][data-tm-save-state='saved']", new() { Timeout = 20000 });
        await page.WaitForFunctionAsync("() => window.__tmSaveChord !== null", null, new() { Timeout = 10000 });
        (await page.EvaluateAsync<bool>("() => window.__tmSaveChord === true"))
            .Should().BeTrue("Ctrl+S must not reach the browser's own Save-page default");
        await SaveNamedScreenshotAsync(page, "35-save-shortcut.png");
    }

    // ── ReadOnly ────────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task F7_5_ReadOnly_BlocksEveryEditingPath_ButKeepsPreviewAndExport()
    {
        var page = await OpenFreshEditorAsync();

        // A brand-new template has no blocks, so give the read-only assertions something to protect (and the
        // editor a selection) before locking it.
        await page.Locator("[data-tm-block='text']").First.ClickAsync();
        await page.WaitForSelectorAsync("[data-tm-block-id]", new() { Timeout = 15000 });

        // The demo's read-only switch is a TmToggle: the checkbox is visually hidden behind the track, so the
        // click goes to the visible label area (which forwards it to the input natively).
        await page.Locator("[data-tm-demo-readonly] .tm-toggle-track").ClickAsync();
        await page.WaitForFunctionAsync(
            "() => document.querySelector('[data-tm-email-editor]')?.getAttribute('data-tm-readonly') === 'true'",
            null, new() { Timeout = 15000 });

        var blocksBefore = await page.Locator("[data-tm-block-id]").CountAsync();
        // The block added above already made the document dirty; the point is that read-only interaction
        // does not change that (no further edit lands anywhere).
        var dirtyBefore = await page.Locator("[data-tm-email-editor]").GetAttributeAsync("data-tm-dirty");

        // Toolbox: visible but inert.
        var tile = page.Locator("[data-tm-toolbox-list] [data-tm-block='text']").First;
        (await tile.IsDisabledAsync()).Should().BeTrue("a read-only palette tile is natively disabled");
        (await tile.GetAttributeAsync("aria-disabled")).Should().Be("true");
        (await tile.GetAttributeAsync("draggable")).Should().BeNull("no drag affordance may be advertised");
        await tile.DispatchEventAsync("click");
        await page.WaitForTimeoutAsync(300);
        (await page.Locator("[data-tm-block-id]").CountAsync())
            .Should().Be(blocksBefore, "a read-only editor cannot add a block");

        // Canvas: no inline actions, no drag handle.
        (await page.Locator("[data-tm-block-action]").CountAsync()).Should().Be(0);
        (await page.Locator("[data-tm-section-action]").CountAsync()).Should().Be(0);
        (await page.Locator("[data-tm-block-id]").First.GetAttributeAsync("draggable")).Should().BeNull();

        // Inspector: every control is inside a natively disabled fieldset. Since Fáze 7A the panel has
        // Content/Style tabs, and a text block's Content tab holds only the rich editor (no <input>), so the
        // Style tab is where a text control proves the disabled state — and switching tabs must still work,
        // because reading a document is not editing it.
        await page.Locator("#tab-style").ClickAsync();
        await page.WaitForSelectorAsync("#tab-style[aria-selected='true']", new() { Timeout = 15000 });
        (await page.Locator("[data-tm-properties-panel] fieldset[disabled]").CountAsync())
            .Should().Be(1, "a disabled fieldset is what makes a whole control group read-only");
        (await page.Locator("[data-tm-properties-panel] input").First.IsDisabledAsync())
            .Should().BeTrue();

        // A disabled control must stay legible: the browser's own grey is ~2.6:1, the design token is not.
        // A TEXT field: a checkbox's "colour" says nothing about the value's readability.
        var contrast = await page.Locator("[data-tm-properties-panel] fieldset[disabled] input[type='text']").First
            .EvaluateAsync<double>(ContrastScript);
        contrast.Should().BeGreaterThanOrEqualTo(4.5, "a read-only value must still meet WCAG AA");

        // Import, undo/redo and save are unavailable.
        (await page.Locator("[data-tm-import-btn]").IsDisabledAsync()).Should().BeTrue();
        (await page.Locator("[data-tm-undo]").IsDisabledAsync()).Should().BeTrue();
        (await page.Locator("[data-tm-save]").CountAsync())
            .Should().Be(0, "a read-only editor renders no Save button at all — there is nothing to save");
        (await page.Locator("[data-tm-readonly-badge]").InnerTextAsync())
            .Should().Contain("Read-only", "the mode is stated, not implied by a disabled control");
        await SaveNamedScreenshotAsync(page, "36-readonly-editor.png");

        // Preview and export stay available: reading the template is not editing.
        await page.Locator("[data-tm-mjml-btn]").ClickAsync();
        await page.WaitForSelectorAsync("#tab-export-mjml", new() { Timeout = 20000 });
        await SaveNamedScreenshotAsync(page, "37-readonly-export.png");
        await page.Keyboard.PressAsync("Escape");
        await page.WaitForSelectorAsync(".tm-modal-overlay", new() { State = WaitForSelectorState.Detached, Timeout = 15000 });

        await page.Locator("[data-tm-preview-btn]").ClickAsync();
        await page.WaitForSelectorAsync("[data-tm-preview-frame]", new() { Timeout = 30000 });
        (await page.Locator("[data-tm-email-editor]").GetAttributeAsync("data-tm-dirty"))
            .Should().Be(dirtyBefore, "no read-only interaction may change the document");
        (await page.Locator("[data-tm-block-id]").CountAsync())
            .Should().Be(blocksBefore, "the block count is untouched by every read-only interaction");
    }

    // ── wide vs narrow command bar ──────────────────────────────────────────────────────────

    [TestMethod]
    public async Task F7_6_HistoryButtons_SpellTheirLabelOnlyOnAWideToolbar()
    {
        var page = await OpenFreshEditorAsync();

        // Fáze 8: the command row is the CORE TmToolbar, whose label decision is its own (ToolbarLabels.Auto
        // hides the text below 640 px of TOOLBAR width). The editor no longer carries a 1200 px container rule,
        // so the contract is now: the label follows the core toolbar, and the accessible name never depends on
        // the visible text.
        var barWidth = await ToolbarWidthAsync(page);
        barWidth.Should().BeGreaterThan(640, "a docked desktop command row is wider than the core label step");
        (await page.Locator("[data-tm-undo] .tm-toolbar-btn-text").IsVisibleAsync())
            .Should().BeTrue("above the core label step the action is spelled out");
        (await page.Locator("[data-tm-undo]").GetAttributeAsync("aria-label"))
            .Should().NotBeNullOrWhiteSpace("the accessible name must not depend on the visible label");
        await SaveNamedScreenshotAsync(page, "39-toolbar-labels.png");

        // A narrow editor (a phone) drops the command row entirely — the actions live in the header's action
        // bar instead — so the icon-only state is now the ACTION BAR, where the label always accompanies the
        // icon and the accessible name is the tile label. The harness pinned the desktop mode for the cases
        // above; this half needs the editor to follow its container again.
        await OpenDemoControlsAsync(page);
        await page.SelectOptionAsync("[data-tm-demo-layout]", "Auto");
        await page.SetViewportSizeAsync(390, 800);
        await page.WaitForSelectorAsync(".tm-editor-shell[data-layout='mobile']", new() { Timeout = 30000 });
        (await page.Locator(".tm-mobile-action-bar").CountAsync()).Should().BeGreaterThan(0);
        (await page.Locator("[data-region='toolbar']").CountAsync()).Should().Be(0,
            "the mobile action bar replaces the command row");
        await SaveNamedScreenshotAsync(page, "38-toolbar-icon-only.png");
    }

    // ── narrow layouts ──────────────────────────────────────────────────────────────────────

    /// <summary>
    /// 768 px and 390 px with a ~200-character failure: the alert wraps its text, the badge truncates, and the
    /// Save button stays VISIBLE and on the same row as the badge (a primary action that jumps under the
    /// pointer between states is the bug the save-chrome group was built to prevent).
    /// </summary>
    [TestMethod]
    public async Task F7_7_NarrowLayout_KeepsSaveBesideTheBadge_WithALongFailureMessage()
    {
        foreach (var (width, height, file) in new[] { (768, 900, "40a-toolbar-narrow-768.png"), (390, 780, "40b-toolbar-narrow-390.png") })
        {
            var page = await OpenFreshEditorAsync();
            await page.SetViewportSizeAsync(width, height);
            // This case is about the NARROW chrome, so let the editor follow its container again (the base
            // harness seeds the desktop mode for the specs that need the docked layout).
            await OpenDemoControlsAsync(page);
        await page.SelectOptionAsync("[data-tm-demo-layout]", "Auto");
            await page.WaitForSelectorAsync(".tm-editor-shell[data-layout='mobile']", new() { Timeout = 30000 });
            await ChooseHostResultAsync(page, "failed-long");

            // A dirty document without depending on an inspector field: inserting a block is available on
            // every layout (docked palette on desktop, the panels sheet on a phone), and it is what a user
            // does before saving anyway.
            await AddBlockFromPaletteAsync(page);
            await page.Locator("[data-tm-save]").ClickAsync();
            await page.WaitForSelectorAsync("[data-tm-save-badge][data-tm-save-state='failed']", new() { Timeout = 20000 });

            var save = page.Locator("[data-tm-save]");
            (await save.IsVisibleAsync()).Should().BeTrue($"the primary action must stay visible at {width} px");
            (await page.Locator("[data-tm-save-badge]").GetAttributeAsync("title"))
                .Should().NotBeNullOrWhiteSpace("a truncated badge has to explain itself on hover");

            // Fáze 8: the badge is chrome of the SHELL's status bar, not of the save chrome, so it can no
            // longer push the primary action aside — the invariant became "Save keeps its own row and the
            // host's whole reason stays readable in the status bar's alert".
            var saveBox = await save.BoundingBoxAsync();
            saveBox.Should().NotBeNull();
            saveBox!.Height.Should().BeGreaterThan(0);
            (await page.Locator("[data-region='status-bar'] [data-tm-save-badge]").CountAsync())
                .Should().Be(1, "the badge lives in the status bar");

            // The full host reason is readable in the alert, not clipped in the pill.
            (await page.Locator("[data-tm-save-alert]").InnerTextAsync())
                .Should().Contain("the stored sample data no longer matches");

            // No horizontal overflow at either narrow width (the F7A review #12 criterion).
            var overflow = await page.EvaluateAsync<int>(
                "() => { const el = document.querySelector('[data-tm-email-editor]'); return Math.round(el.scrollWidth - el.clientWidth); }");
            overflow.Should().BeLessThanOrEqualTo(0, $"no horizontal overflow at {width} px");

            await SaveNamedScreenshotAsync(page, file);
            await page.CloseAsync();
        }
    }

    /// <summary>Relative luminance based contrast ratio of the first input's text against its background.</summary>
    private const string ContrastScript = """
        (element) => {
            const parse = (value) => {
                const match = value.match(/rgba?\(([^)]+)\)/);
                if (!match) return null;
                const parts = match[1].split(',').map((part) => parseFloat(part));
                return { r: parts[0], g: parts[1], b: parts[2], a: parts.length > 3 ? parts[3] : 1 };
            };
            const blend = (top, bottom) => ({
                r: top.r * top.a + bottom.r * (1 - top.a),
                g: top.g * top.a + bottom.g * (1 - top.a),
                b: top.b * top.a + bottom.b * (1 - top.a),
                a: 1,
            });
            const luminance = (c) => {
                const channel = (v) => {
                    const s = v / 255;
                    return s <= 0.03928 ? s / 12.92 : Math.pow((s + 0.055) / 1.055, 2.4);
                };
                return 0.2126 * channel(c.r) + 0.7152 * channel(c.g) + 0.0722 * channel(c.b);
            };
            const style = getComputedStyle(element);
            let background = parse(style.backgroundColor);
            let ancestor = element;
            while ((!background || background.a === 0) && ancestor.parentElement) {
                ancestor = ancestor.parentElement;
                background = parse(getComputedStyle(ancestor).backgroundColor);
            }
            const foreground = blend(parse(style.color), background ?? { r: 255, g: 255, b: 255, a: 1 });
            const l1 = luminance(foreground);
            const l2 = luminance(background ?? { r: 255, g: 255, b: 255, a: 1 });
            const ratio = (Math.max(l1, l2) + 0.05) / (Math.min(l1, l2) + 0.05);
            return Math.round(ratio * 100) / 100;
        }
        """;

    /// <summary>The toolbar's rendered inline size — the value the container query reacts to.</summary>
    private static Task<double> ToolbarWidthAsync(IPage page)
        => page.Locator(".tm-email-editor__toolbar").EvaluateAsync<double>("e => e.getBoundingClientRect().width");
}
