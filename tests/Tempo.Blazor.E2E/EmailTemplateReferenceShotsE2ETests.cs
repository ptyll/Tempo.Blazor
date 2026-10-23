using Microsoft.Playwright;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Fáze 11 — the reference screenshot set, regenerated ONCE for the whole email surface and named after the
/// reference screen ids in <c>output/imagegen/tempo-reimagined/Tempo.Blazor.EmailTemplates/</c>.
/// <para>
/// Why one class and four viewports: the tracked <c>__screenshots__/email-templates</c> set had drifted since
/// Fáze 5 (every run produced a different image than HEAD) because each phase captured at whatever viewport it
/// happened to use, and no shot ever existed at 1024 / 390 / 320 px. A reference id is captured here at ALL
/// supported widths — <c>--desktop</c> (1440), <c>--tablet</c> (1024), <c>--mobile</c> (390) and
/// <c>--mobile-320</c> (320) — so the UX review always has the same four frames per screen and a drift is a
/// visible diff instead of a surprise.
/// </para>
/// <para>
/// The tour per viewport: list → delete confirmation → editor → variable picker → preview dialog → validation
/// dialog → keyboard shortcuts → import dialog → export dialog → table editor → send page. Each method boots
/// ONE page and walks the whole tour, so the four methods cost four boots, not forty-four.
/// </para>
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class EmailTemplateReferenceShotsE2ETests : EmailTemplateE2ETestBase
{
    private const string WelcomeEditorRoute = "/email-templates/edit/11111111-1111-1111-1111-111111111111";
    private const string OrderEditorRoute = "/email-templates/edit/33333333-3333-3333-3333-333333333333";

    /// <summary>1440 × 900 — the desktop reference frame.</summary>
    [TestMethod]
    public async Task ReferenceShots_Desktop1440() => await CaptureAllAsync(1440, 900, "desktop", "Desktop", touch: false);

    /// <summary>1024 × 768 — the tablet reference frame (docked panels, one expanded).</summary>
    [TestMethod]
    public async Task ReferenceShots_Tablet1024() => await CaptureAllAsync(1024, 768, "tablet", "Tablet", touch: true);

    /// <summary>390 × 844 — the phone reference frame (canvas + bottom sheet).</summary>
    [TestMethod]
    public async Task ReferenceShots_Mobile390() => await CaptureAllAsync(390, 844, "mobile", "Mobile", touch: true);

    /// <summary>320 × 720 — the narrow edge case the reference does not show.</summary>
    [TestMethod]
    public async Task ReferenceShots_Mobile320() => await CaptureAllAsync(320, 720, "mobile-320", "Mobile", touch: true);

    private async Task CaptureAllAsync(int width, int height, string size, string layout, bool touch)
    {
        var context = await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = width, Height = height },
            HasTouch = touch,
            IsMobile = touch,
            Locale = "en-US",
            IgnoreHTTPSErrors = true,
            AcceptDownloads = true,
        });
        RegisterContext(context);
        var page = await context.NewPageAsync();

        // ── 1. the template library ─────────────────────────────────────────────────────────
        await GotoAsync(page, "/email-templates");
        await page.WaitForSelectorAsync("[data-tm-template-card]", new() { Timeout = 30000 });
        await SettleAsync(page);
        await SaveNamedViewportScreenshotAsync(page, $"email-templates-page--{size}.png");

        // ── 2. the delete confirmation ──────────────────────────────────────────────────────
        await OpenDeleteConfirmationAsync(page);
        await SettleAsync(page);
        await SaveNamedViewportScreenshotAsync(page, $"email-templates-page--dialog-01--{size}.png");
        await CloseOverlayAsync(page, "[data-tm-confirm-delete]");

        // ── 3. the editor ───────────────────────────────────────────────────────────────────
        await GotoAsync(page, WelcomeEditorRoute);
        await WaitForEditorAsync(page);
        await ExpandEditorToViewportAsync(page);
        await ForceEditorLayoutAsync(page, layout);
        await CloseDemoControlsAsync(page);
        await SettleAsync(page);
        await SaveNamedViewportScreenshotAsync(page, $"email-template-editor--{size}.png");

        // ── 4. the variable picker ──────────────────────────────────────────────────────────
        await OpenVariablePickerAsync(page, layout);
        await SaveNamedViewportScreenshotAsync(page, $"email-variable-picker--{size}.png");
        await CloseOverlayAsync(page, "[data-tm-variable-picker]");

        // ── 5. the preview dialog ───────────────────────────────────────────────────────────
        await OpenCommandAsync(page, layout, "[data-tm-preview-btn]", "preview", actionBar: layout == "Mobile");
        await page.WaitForSelectorAsync("[data-tm-preview-root]", new() { Timeout = 30000 });
        await SettleAsync(page);
        await SaveNamedViewportScreenshotAsync(page, $"email-template-editor--toolbar_preview--{size}.png");
        await CloseOverlayAsync(page, "[data-tm-preview-root]");

        // ── 6. the validation dialog ────────────────────────────────────────────────────────
        await OpenCommandAsync(page, layout, "[data-tm-validate-btn]", "validate", actionBar: layout == "Mobile");
        await page.WaitForSelectorAsync("[data-tm-validation-dialog]", new() { Timeout = 30000 });
        await SettleAsync(page);
        await SaveNamedViewportScreenshotAsync(page, $"email-template-editor--validation_title--{size}.png");
        await CloseOverlayAsync(page, "[data-tm-validation-dialog]");

        // ── 7. the keyboard shortcuts help ──────────────────────────────────────────────────
        await OpenCommandAsync(page, layout, "[data-tm-help-btn]", "Help");
        await page.WaitForSelectorAsync(".tm-keyboard-shortcuts-title", new() { Timeout = 30000 });
        await SettleAsync(page);
        await SaveNamedViewportScreenshotAsync(page, $"email-keyboard-shortcuts--{size}.png");
        await CloseOverlayAsync(page, ".tm-keyboard-shortcuts-title");

        // ── 8. the import dialog ────────────────────────────────────────────────────────────
        await OpenCommandAsync(page, layout, "[data-tm-import-btn]", "Import");
        await page.WaitForSelectorAsync("[data-tm-import-dropzone]", new() { Timeout = 30000 });
        await SettleAsync(page);
        await SaveNamedViewportScreenshotAsync(page, $"email-import-dialog--{size}.png");
        await CloseOverlayAsync(page, "[data-tm-import-dropzone]");

        // ── 9. the export dialog ────────────────────────────────────────────────────────────
        await OpenCommandAsync(page, layout, "[data-tm-mjml-btn]", "Export");
        await page.WaitForSelectorAsync("#tm-export-mjml-code", new() { Timeout = 30000 });
        await SettleAsync(page);
        await SaveNamedViewportScreenshotAsync(page, $"email-export-dialog--{size}.png");
        await CloseOverlayAsync(page, "#tm-export-mjml-code");

        // ── 10. the table grid editor (the order seed carries a table) ──────────────────────
        await GotoAsync(page, OrderEditorRoute);
        await WaitForEditorAsync(page);
        await ExpandEditorToViewportAsync(page);
        await ForceEditorLayoutAsync(page, layout);
        await CloseDemoControlsAsync(page);
        await SelectBlockAsync(page, "table");
        await SettleAsync(page);
        await SaveNamedViewportScreenshotAsync(page, $"email-table-editor--{size}.png");

        // ── 11. the send page ───────────────────────────────────────────────────────────────
        await GotoAsync(page, $"/email-templates/send/{OrderTemplateId}");
        await page.WaitForSelectorAsync("[data-tm-email-send-page]", new() { Timeout = 30000 });
        await SettleAsync(page);
        await SaveNamedViewportScreenshotAsync(page, $"email-template-send-page--{size}.png");
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────

    private async Task GotoAsync(IPage page, string route)
    {
        // Cold boot of a self-hosted WASM app occasionally exceeds the readiness window: one reload of the
        // same context is the documented recovery (see the F9A/F10 specs).
        await GotoAppReadyAsync(page, $"{BaseUrl}{route}");
    }

    private static async Task WaitForEditorAsync(IPage page)
    {
        await page.WaitForSelectorAsync("[data-tm-email-editor]", new() { Timeout = 60000 });
        await page.WaitForSelectorAsync("[data-tm-canvas-doc]", new() { Timeout = 60000 });
    }

    /// <summary>Gives the editor the whole viewport by hiding the demo's own app shell (see F8/F9/F10).</summary>
    private static async Task ExpandEditorToViewportAsync(IPage page)
    {
        await page.AddStyleTagAsync(new()
        {
            Content = "aside.fixed { display: none !important; } "
                + "main { margin-left: 0 !important; width: 100vw !important; max-width: 100vw !important; }",
        });
        await page.WaitForTimeoutAsync(400);
    }

    /// <summary>
    /// Opens the delete confirmation. On a phone the card's secondary actions live in a "⋮" menu, so the
    /// helper takes whichever affordance the current width renders.
    /// </summary>
    private static async Task OpenDeleteConfirmationAsync(IPage page)
    {
        var direct = page.Locator("[data-tm-delete]").First;
        if (await direct.CountAsync() > 0 && await direct.IsVisibleAsync())
        {
            await direct.ClickAsync();
        }
        else
        {
            await page.Locator("[data-tm-card-menu]").First.ClickAsync();
            await page.Locator("[data-tm-card-menu-delete]").First.ClickAsync();
        }

        await page.WaitForSelectorAsync("[data-tm-confirm-delete]", new() { Timeout = 20000 });
    }

    /// <summary>
    /// Folds the demo's own switch bar away again. On a phone the bar lives in a &lt;details&gt; disclosure that
    /// <see cref="EmailTemplateE2ETestBase.ForceEditorLayoutAsync"/> has to open; leaving it open pushes the
    /// editor's mobile header out of the frame, so the reference screenshots — and every later command lookup —
    /// see the editor the user actually gets.
    /// </summary>
    private static async Task CloseDemoControlsAsync(IPage page)
    {
        var summary = page.Locator("[data-tm-demo-controls] > summary");
        if (await summary.CountAsync() == 0) return;
        if (await page.Locator("[data-tm-demo-controls]").GetAttributeAsync("open") is null) return;
        await summary.ClickAsync();
        await page.WaitForTimeoutAsync(300);
    }

    /// <summary>
    /// Opens an editor command wherever the current layout keeps it: the command row (desktop / tablet), the
    /// toolbar's "More" menu when the row folded it, the shell's "⋮" overflow on a phone, or — for the two
    /// commands the phone's action bar owns (preview / validate) — the action bar itself.
    /// </summary>
    private static async Task OpenCommandAsync(
        IPage page, string layout, string hook, string label, bool actionBar = false)
    {
        if (actionBar)
        {
            var action = page.Locator($".tm-mobile-action-bar__action[data-action-id='{label}']").First;
            await action.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 20000 });
            await action.ClickAsync();
            return;
        }

        var direct = page.Locator($"[data-tm-email-toolbar] {hook}").First;
        if (await direct.CountAsync() > 0 && await direct.IsVisibleAsync())
        {
            await direct.ClickAsync();
            return;
        }

        if (layout == "Mobile")
        {
            // The phone's "⋮" opens a content-height sheet; both the trigger and the rows are activated by a
            // dispatched click, because the sheet's grabber covers a real pointer's path to a row and the
            // trigger hides under it while the sheet is closing.
            var trigger = page.Locator("[data-tm-mobile-overflow] .tm-popover__trigger").First;
            await trigger.WaitForAsync(new() { State = WaitForSelectorState.Attached, Timeout = 20000 });
            await trigger.DispatchEventAsync("click");
            try
            {
                await page.WaitForSelectorAsync("[data-tm-mobile-overflow-panel]", new() { Timeout = 8000 });
            }
            catch (TimeoutException)
            {
                await page.WaitForTimeoutAsync(500);
                await trigger.DispatchEventAsync("click");
                await page.WaitForSelectorAsync("[data-tm-mobile-overflow-panel]", new() { Timeout = 15000 });
            }

            await page.Locator($"[data-tm-mobile-overflow-panel] {hook}").First.DispatchEventAsync("click");
            return;
        }

        // Folded into the toolbar's own "More" menu: the entry keeps the command's LABEL, not its hook.
        var more = page.Locator(".tm-toolbar-more").First;
        if (await more.CountAsync() == 0)
        {
            throw new InvalidOperationException(
                $"The editor exposes neither '{hook}' nor a '.tm-toolbar-more' trigger on the {layout} layout.");
        }

        await more.ClickAsync();
        await page.WaitForSelectorAsync(".tm-toolbar-more__item", new() { Timeout = 20000 });
        await page.Locator(".tm-toolbar-more__item", new() { HasTextString = label }).First.ClickAsync();
    }

    /// <summary>Closes whatever overlay is open (dialog, sheet, popover, help) with Escape, as a user would.</summary>
    private static async Task CloseOverlayAsync(IPage page, string rootSelector)
    {
        await page.Keyboard.PressAsync("Escape");
        try
        {
            await page.WaitForSelectorAsync(rootSelector, new() { State = WaitForSelectorState.Detached, Timeout = 8000 });
        }
        catch (TimeoutException)
        {
            // Some surfaces animate out of the DOM after their own delay; the caller only needs the screen
            // back, not a particular teardown moment.
            await page.WaitForTimeoutAsync(300);
        }

        await page.WaitForTimeoutAsync(200);
    }

    /// <summary>
    /// Opens the variable picker. The desktop / tablet inspector exposes it on the document's Subject field;
    /// a phone hides the inspector behind the sheet, which opens on the palette, so the helper selects a text
    /// block first (the sheet follows the selection) and uses the rich content's insert trigger instead.
    /// </summary>
    private static async Task OpenVariablePickerAsync(IPage page, string layout)
    {
        if (string.Equals(layout, "Mobile", StringComparison.Ordinal))
        {
            if (await page.Locator(".tm-editor-shell__sheet-close").CountAsync() > 0)
            {
                await page.Locator(".tm-editor-shell__sheet-close").First.ClickAsync();
                await page.WaitForTimeoutAsync(300);
            }

            await SelectBlockAsync(page, null);
            await page.WaitForSelectorAsync(".tm-editor-shell__sheet [data-tm-properties]", new() { Timeout = 20000 });
        }

        var desktopTrigger = page.Locator("[data-tm-variable-trigger='Subject'] .tm-popover__trigger");
        var trigger = await desktopTrigger.CountAsync() > 0
            ? desktopTrigger.First
            : page.Locator("[data-tm-variables='text'] .tm-popover__trigger").First;
        await trigger.WaitForAsync(new() { State = WaitForSelectorState.Attached, Timeout = 20000 });

        if (await trigger.IsVisibleAsync())
        {
            await trigger.ClickAsync();
        }
        else
        {
            // Behind the sheet's scroller: dispatch the activation, the Blazor handler runs either way.
            await trigger.DispatchEventAsync("click");
        }

        await page.WaitForSelectorAsync("[data-tm-variable-picker] input[type='search']",
            new() { State = WaitForSelectorState.Visible, Timeout = 20000 });
        await page.WaitForTimeoutAsync(300);
    }

    /// <summary>
    /// Selects a canvas block. <paramref name="kind"/> null picks the first block of any kind; a phone sheet
    /// would otherwise refuse a real pointer click on a block that sits under it, so the click is dispatched.
    /// </summary>
    private static async Task SelectBlockAsync(IPage page, string? kind)
    {
        var selector = kind is null
            ? "[data-tm-canvas] [data-tm-block-id]"
            : $"[data-tm-canvas] [data-tm-block-kind='{kind}']";
        await page.Locator(selector).First.DispatchEventAsync("click");
        await page.WaitForTimeoutAsync(500);
    }

    /// <summary>Escape / pointer parking so no hover chrome or toast lands in the frame.</summary>
    private static async Task SettleAsync(IPage page)
    {
        await page.Mouse.MoveAsync(0, 0);
        await page.WaitForTimeoutAsync(400);
    }
}
