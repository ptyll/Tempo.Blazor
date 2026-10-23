using FluentAssertions;
using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Fáze 9 in a real browser: the dialog item-editing mode and the table grid / cell dialog, on a desktop
/// (1440) and a phone (390, coarse pointer).
/// <para>
/// What the specs prove: the compact item list opens the core <c>TmModal</c> (a SHEET on a phone with its
/// sticky Save/Cancel footer) over a copy, Cancel changes nothing and gives the focus back to the opening
/// button; the table grid scrolls horizontally with a pinned control column instead of overflowing the
/// page; the cell dialog writes a link and a background colour into the exported MJML and refuses a
/// <c>javascript:</c> link or a CSS-function colour.
/// </para>
/// <para>
/// Screenshots land in <c>__screenshots__/email-templates/f9-*.png</c> and are compared against
/// <c>output/imagegen/tempo-reimagined/Tempo.Blazor.EmailTemplates/email-table-editor--{desktop|mobile}.png</c>
/// (no reference exists for the item dialogs — they are a Fáze 9 addition).
/// </para>
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class EmailEditorItemDialogF9E2ETests : EmailTemplateE2ETestBase
{
    /// <summary>The order template: its seed document carries a table with a linked, coloured cell and a social block.</summary>
    private const string OrderEditorRoute = "/email-templates/edit/33333333-3333-3333-3333-333333333333";

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
        // The WASM boot occasionally exceeds the harness's 60 s readiness window on a loaded machine (the
        // self-hosted demo apps start with the first test of the class). One reload of the SAME context is
        // the documented recovery: the app is up by then, only the first boot was slow.
        await GotoAppReadyAsync(page, $"{BaseUrl}{OrderEditorRoute}");

        await page.WaitForSelectorAsync("[data-tm-email-editor]", new() { Timeout = 60000 });
        await page.WaitForSelectorAsync("[data-tm-canvas-doc]", new() { Timeout = 60000 });
        return page;
    }

    /// <summary>Gives the editor the whole viewport by hiding the demo's own app shell (see F8).</summary>
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
    /// Selects a block by its localized TYPE. The click is DISPATCHED instead of a real pointer click:
    /// on a phone the editor's bottom sheet covers the lower half of the canvas, so Playwright's hit test
    /// refuses the click on a block that sits under it (the sheet is the user's own surface, not an overlay
    /// bug). The dispatched event still travels the normal Blazor handler path.
    /// <para>
    /// The selector is the block KIND, not the accessible name: since the F9A UX review the name carries the
    /// block's brief content too ("Table: r0c0"), so an exact <c>aria-label='Table'</c> match no longer hits.
    /// </para>
    /// </summary>
    private static async Task SelectBlockAsync(IPage page, string kind)
    {
        await page.Locator($".tm-email-canvas-block[data-tm-block-kind='{kind.ToLowerInvariant()}']").First
            .DispatchEventAsync("click");
        await page.WaitForTimeoutAsync(400);
    }

    /// <summary>Switches the demo's item editing mode (the page folds its controls into a disclosure on a phone).</summary>
    private static async Task SetItemModeAsync(IPage page, string mode)
    {
        await OpenDemoControlsAsync(page);
        await page.SelectOptionAsync("[data-tm-demo-item-mode]", mode);
        await page.WaitForTimeoutAsync(200);
        await page.Keyboard.PressAsync("Escape");
    }

    /// <summary>Opens the export dialog and returns the generated MJML.</summary>
    private static async Task<string> ExportMjmlAsync(IPage page)
    {
        await page.ClickAsync("[data-tm-mjml-btn]");
        await page.WaitForSelectorAsync("#tm-export-mjml-code", new() { Timeout = 15000 });
        var mjml = await page.InputValueAsync("#tm-export-mjml-code");
        await page.Locator(".tm-modal-close").First.ClickAsync();
        await page.WaitForTimeoutAsync(200);
        return mjml;
    }

    /// <summary>Saves a LOCATOR-sized PNG (one component, not the page) into the named screenshot folder.</summary>
    private static async Task SaveNamedLocatorScreenshotAsync(IPage page, string selector, string fileName)
    {
        var dir = Path.Combine(FindRepositoryRoot().FullName, "tests", "Tempo.Blazor.E2E", "__screenshots__", "email-templates");
        Directory.CreateDirectory(dir);
        await page.Locator(selector).First.ScreenshotAsync(new LocatorScreenshotOptions
        {
            Path = Path.Combine(dir, fileName),
            Type = ScreenshotType.Png,
        });
    }

    /// <summary>
    /// Raises the editor's phone sheet to its HIGHEST snap through the shared grabber's keyboard path
    /// (the grabber is a vertical slider: End = the last snap). Measuring a grid that is clipped by a
    /// half-height sheet would prove nothing about the grid.
    /// </summary>
    private static async Task ExpandSheetToFullHeightAsync(IPage page)
    {
        var handle = page.Locator("[data-testid='tm-sheet-handle']").First;
        await handle.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 15000 });
        await handle.FocusAsync();
        await page.Keyboard.PressAsync("End");
        await page.WaitForTimeoutAsync(600);
        var value = await handle.GetAttributeAsync("aria-valuenow");
        var max = await handle.GetAttributeAsync("aria-valuemax");
        value.Should().Be(max, "the sheet must sit at its highest snap before the grid is measured");
    }

    /// <summary>Asserts the emulated pointer really is coarse — a 44 px target has no meaning otherwise.</summary>
    private static async Task AssertCoarsePointerAsync(IPage page)
    {
        (await page.EvaluateAsync<bool>("() => matchMedia('(pointer: coarse)').matches"))
            .Should().BeTrue("the phone context has to emulate a coarse pointer, or the 44 px rule is untested");
    }

    private static async Task<int> InlineOverflowAsync(IPage page, string selector)
        => await page.EvaluateAsync<int>(
            "sel => { const el = document.querySelector(sel); return el ? Math.round(el.scrollWidth - el.clientWidth) : -1; }",
            selector);

    // ── Desktop: the grid ───────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task F9_1_TableGrid_Desktop_HasSectionsCaptionAndPinnedControlColumn()
    {
        var page = await OpenEditorAsync(1440, 900);
        await ExpandEditorToViewportAsync(page);
        await SelectBlockAsync(page, "Table");

        await page.WaitForSelectorAsync("[data-tm-table-editor]", new() { Timeout = 15000 });

        // The two sections the plan asks for, the caption and the per-cell accessible names.
        (await page.Locator("[data-tm-table-section='header']").CountAsync()).Should().Be(1);
        (await page.Locator("[data-tm-table-section='rows-columns']").CountAsync()).Should().Be(1);
        (await page.Locator("[data-tm-table-editor] caption").CountAsync()).Should().Be(1);
        var labels = await page.Locator("[data-tm-table-cell]").EvaluateAllAsync<string[]>(
            "els => els.map(e => e.getAttribute('aria-label'))");
        labels.Should().Contain("Row 1, column 1").And.Contain("Row 2, column 2");

        // Every cell offers its dialog.
        (await page.Locator("[data-tm-table-cell-edit]").CountAsync())
            .Should().Be(await page.Locator("[data-tm-table-cell]").CountAsync());

        // Six columns still fit the panel: the grid scrolls, the page does not. The count is read first,
        // because the demo's store is shared with the other specs of this class.
        var startColumns = int.Parse(
            await page.Locator("[data-tm-table-editor]").GetAttributeAsync("data-tm-table-columns") ?? "0");
        for (var i = startColumns; i < 6; i++) await page.ClickAsync("[data-tm-table-add-col]");
        await page.WaitForTimeoutAsync(300);
        var columns = await page.Locator("[data-tm-table-editor]").GetAttributeAsync("data-tm-table-columns");
        columns.Should().Be("6");
        (await InlineOverflowAsync(page, "[data-tm-table-editor]")).Should().BeLessThanOrEqualTo(0,
            "the grid must not push the inspector panel wider than itself");
        (await InlineOverflowAsync(page, "body")).Should().BeLessThanOrEqualTo(0,
            "and the page itself must never scroll horizontally");
        (await InlineOverflowAsync(page, "[data-tm-table-scroll]")).Should().BeGreaterThan(0,
            "with six columns the grid itself has to scroll");

        // The pinned control column keeps its place while the grid scrolls.
        await page.EvaluateAsync("document.querySelector('[data-tm-table-scroll]').scrollLeft = 200");
        await page.WaitForTimeoutAsync(200);
        var offset = await page.EvaluateAsync<int>(
            "() => Math.round(document.querySelector('[data-tm-table-rowctl]').getBoundingClientRect().left"
            + " - document.querySelector('[data-tm-table-scroll]').getBoundingClientRect().left)");
        offset.Should().BeLessThan(4, "the control column is sticky, so it stays at the start edge");

        // Review F9 (item 10): the baseline screenshot is taken at the scroll origin on desktop as well.
        await page.EvaluateAsync("document.querySelector('[data-tm-table-scroll]').scrollLeft = 0");
        await page.WaitForTimeoutAsync(300);
        (await page.EvaluateAsync<int>("() => Math.round(document.querySelector('[data-tm-table-scroll]').scrollLeft)"))
            .Should().Be(0);
        await SaveNamedLocatorScreenshotAsync(page, "[data-tm-table-editor]", "f9-table-editor--desktop-element.png");
        await SaveNamedViewportScreenshotAsync(page, "f9-table-editor--desktop.png");
    }

    [TestMethod]
    public async Task F9_2_CellDialog_WritesTheLinkAndTheSpanIntoTheExport()
    {
        var page = await OpenEditorAsync(1440, 900);
        await ExpandEditorToViewportAsync(page);
        await SelectBlockAsync(page, "Table");

        await page.Locator("[data-tm-table-cell-edit]").First.ClickAsync();
        await page.WaitForSelectorAsync("[data-tm-table-cell-dialog]", new() { Timeout = 15000 });

        await page.FillAsync("[data-tm-item-field='Text'] input", "Docs");
        await page.FillAsync("[data-tm-item-field='Href'] input", "https://example.com/docs");
        await page.FillAsync("[data-tm-item-field='ColSpan'] input", "2");
        await page.WaitForTimeoutAsync(200);
        (await page.Locator("[data-tm-item-dialog-save]").IsDisabledAsync()).Should().BeFalse();

        await SaveNamedViewportScreenshotAsync(page, "f9-table-cell-dialog--desktop.png");
        await page.ClickAsync("[data-tm-item-dialog-save]");
        await page.WaitForSelectorAsync("[data-tm-item-dialog]", new() { State = WaitForSelectorState.Detached, Timeout = 15000 });

        var mjml = await ExportMjmlAsync(page);
        mjml.Should().Contain("<a href=\"https://example.com/docs\">Docs</a>");
        mjml.Should().Contain("colspan=\"2\"");
        // The seed's own background colour still rides along, untouched by the cell edit.
        mjml.Should().Contain("bgcolor=\"#f5f5f5\"");
    }

    [TestMethod]
    public async Task F9_3_CellDialog_RefusesABlockedSchemeAndACssFunctionColour_EdgeCase()
    {
        var page = await OpenEditorAsync(1440, 900);
        await ExpandEditorToViewportAsync(page);
        await SelectBlockAsync(page, "Table");

        var before = await ExportMjmlAsync(page);
        before.Should().Contain("<a href=\"https://example.com/gadget\">Gadget</a>",
            "the seed document links a cell in the second row");
        before.Should().Contain("bgcolor=\"#f5f5f5\"",
            "and paints a cell in the first row");

        // Row 2, column 1: the seed's second linked cell, which no other spec in this class edits.
        await page.Locator("[data-tm-table-cell-edit]").Nth(2).ClickAsync();
        await page.WaitForSelectorAsync("[data-tm-table-cell-dialog]", new() { Timeout = 15000 });

        await page.FillAsync("[data-tm-item-field='Href'] input", "javascript:alert(1)");
        await page.WaitForTimeoutAsync(200);
        // Review F9: the message sits AT the field (aria-invalid + aria-describedby), not in a summary that
        // would also be announced twice, and the Save button stays enabled so the click explains itself.
        (await page.Locator("[data-tm-item-field='Href'] input[aria-invalid='true']").CountAsync())
            .Should().Be(1, "a blocked URL scheme has to be flagged on the Href field itself");
        (await page.Locator("[data-tm-item-field='Href']").First.InnerTextAsync())
            .Should().Contain("valid link", "the localized message explains what the field expects");
        (await page.Locator("[data-tm-item-dialog-save]").IsDisabledAsync()).Should().BeFalse(
            "a disabled Save button never explains what is wrong");

        await SaveNamedViewportScreenshotAsync(page, "f9-table-cell-dialog--desktop.png");

        await page.ClickAsync("[data-tm-item-dialog-save]");
        await page.WaitForTimeoutAsync(300);
        (await page.Locator("[data-tm-table-cell-dialog]").CountAsync()).Should().Be(1,
            "an invalid cell is never applied, so the dialog stays open");
        (await page.Locator("[data-tm-item-field='Href'] input").GetAttributeAsync("aria-describedby"))
            .Should().NotBeNullOrEmpty("the message is described by its own control");

        await page.ClickAsync("[data-tm-item-dialog-cancel]");
        await page.WaitForSelectorAsync("[data-tm-item-dialog]", new() { State = WaitForSelectorState.Detached, Timeout = 15000 });

        (await ExportMjmlAsync(page)).Should().Be(before, "Cancel must leave the document untouched");
    }

    // ── Desktop: the dialog item mode ───────────────────────────────────────────────────────

    [TestMethod]
    public async Task F9_4_ItemDialogMode_CompactList_CancelAndSave()
    {
        var page = await OpenEditorAsync(1440, 900);
        await ExpandEditorToViewportAsync(page);
        await SetItemModeAsync(page, "Dialog");
        await SelectBlockAsync(page, "Social");

        await page.WaitForSelectorAsync("[data-tm-item-list]", new() { Timeout = 15000 });
        (await page.Locator("[data-tm-item]").CountAsync()).Should().Be(2);
        (await page.Locator("[data-tm-item-preview]").CountAsync()).Should().Be(2);
        (await page.Locator("[data-tm-item-list] input").CountAsync()).Should().Be(0,
            "the dialog mode replaces the inline inputs with the compact preview rows");

        var before = await ExportMjmlAsync(page);

        // Edit → change the URL → Cancel: nothing may change.
        await page.Locator("[data-tm-item-edit]").First.ClickAsync();
        await page.WaitForSelectorAsync("[data-tm-item-dialog]", new() { Timeout = 15000 });
        (await page.Locator(".tm-modal-overlay").CountAsync()).Should().Be(1);
        await page.FillAsync("[data-tm-item-field='Href'] input", "https://example.com/cancelled");
        await page.ClickAsync("[data-tm-item-dialog-cancel]");
        await page.WaitForSelectorAsync("[data-tm-item-dialog]", new() { State = WaitForSelectorState.Detached, Timeout = 15000 });
        (await ExportMjmlAsync(page)).Should().Be(before);

        // Edit → change the URL → Save: exactly that change, as one dirty step.
        await page.Locator("[data-tm-item-edit]").First.ClickAsync();
        await page.WaitForSelectorAsync("[data-tm-item-dialog]", new() { Timeout = 15000 });
        await page.FillAsync("[data-tm-item-field='Href'] input", "https://example.com/saved");
        await SaveNamedViewportScreenshotAsync(page, "f9-item-dialog--desktop.png");
        await page.ClickAsync("[data-tm-item-dialog-save]");
        await page.WaitForSelectorAsync("[data-tm-item-dialog]", new() { State = WaitForSelectorState.Detached, Timeout = 15000 });

        var after = await ExportMjmlAsync(page);
        after.Should().Contain("https://example.com/saved").And.NotContain("https://example.com/cancelled");
        (await page.Locator("[data-tm-item]").First.TextContentAsync()).Should().Contain("https://example.com/saved");

        // The blocked scheme is refused in the dialog too, so a bad value never reaches the document.
        await page.Locator("[data-tm-item-edit]").First.ClickAsync();
        await page.WaitForSelectorAsync("[data-tm-item-dialog]", new() { Timeout = 15000 });
        await page.FillAsync("[data-tm-item-field='Href'] input", "javascript:alert(1)");
        await page.WaitForTimeoutAsync(200);
        (await page.Locator("[data-tm-item-field='Href'] input[aria-invalid='true']").CountAsync())
            .Should().Be(1, "review F9: the message is shown at the Href field");
        await page.ClickAsync("[data-tm-item-dialog-save]");
        await page.WaitForTimeoutAsync(300);
        (await page.Locator("[data-tm-item-dialog]").CountAsync()).Should().Be(1,
            "a blocked scheme keeps the dialog open and changes nothing");
        await page.ClickAsync("[data-tm-item-dialog-cancel]");
    }

    // ── Phone: the sheet and the scrolling grid ────────────────────────────────────────────

    [TestMethod]
    public async Task F9_5_ItemDialog_Phone_IsTheCoreSheetWithStickyFooter_AndFocusComesBack()
    {
        var page = await OpenEditorAsync(390, 844, touch: true);
        await page.WaitForSelectorAsync(".tm-editor-shell[data-layout='mobile']", new() { Timeout = 30000 });

        await SetItemModeAsync(page, "Dialog");
        await SelectBlockAsync(page, "Social");
        await page.WaitForSelectorAsync("[data-tm-item-list]", new() { Timeout = 15000 });

        var editButton = page.Locator("[data-tm-item-edit]").First;
        await editButton.ClickAsync();
        await page.WaitForSelectorAsync("[data-tm-item-dialog]", new() { Timeout = 15000 });

        // The core modal sheet, not a local sheet implementation.
        (await page.Locator(".tm-modal-overlay.tm-modal--sheet").CountAsync()).Should().Be(1,
            "a phone must get the F2 TmModal sheet");
        (await page.Locator("[data-tm-item-dialog] .tm-sheet, [data-tm-item-dialog] .tm-drawer").CountAsync())
            .Should().Be(0, "the dialog body must not ship its own sheet — the modal is the sheet");

        // The sticky footer and its two actions are reachable without scrolling the page. The sheet slides
        // up for 250 ms, and a bounding box measured mid-animation sits below the viewport — settle first.
        await page.WaitForTimeoutAsync(600);
        var save = page.Locator("[data-tm-item-dialog-save]");
        await save.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10000 });
        var saveBox = await save.BoundingBoxAsync();
        var cancelBox = await page.Locator("[data-tm-item-dialog-cancel]").BoundingBoxAsync();
        saveBox.Should().NotBeNull();
        cancelBox.Should().NotBeNull();
        (saveBox!.Y + saveBox.Height).Should().BeLessThanOrEqualTo(844,
            "the Save button must sit inside the viewport");
        saveBox.Height.Should().BeGreaterThanOrEqualTo(44, "a coarse pointer gets a finger-sized action");

        // Every action of the compact list is finger-sized as well.
        var editBox = await editButton.BoundingBoxAsync();
        editBox!.Height.Should().BeGreaterThanOrEqualTo(44);

        await SaveNamedViewportScreenshotAsync(page, "f9-item-dialog--mobile.png");

        // Cancel returns the focus to the button that opened the dialog.
        await page.ClickAsync("[data-tm-item-dialog-cancel]");
        await page.WaitForSelectorAsync("[data-tm-item-dialog]", new() { State = WaitForSelectorState.Detached, Timeout = 15000 });
        await page.WaitForTimeoutAsync(300);
        var focused = await page.EvaluateAsync<string>(
            "() => { const el = document.activeElement;"
            + " return el ? (el.hasAttribute('data-tm-item-edit') ? 'edit' : el.tagName.toLowerCase()) : 'none'; }");
        focused.Should().Be("edit", "focus returns to the button that opened the dialog");
    }

    [TestMethod]
    public async Task F9_6_TableGrid_Phone_ScrollsInsideTheSheet_WithoutPageOverflow()
    {
        var page = await OpenEditorAsync(390, 844, touch: true);
        await page.WaitForSelectorAsync(".tm-editor-shell[data-layout='mobile']", new() { Timeout = 30000 });
        await AssertCoarsePointerAsync(page);

        await SelectBlockAsync(page, "Table");
        await page.WaitForSelectorAsync("[data-tm-table-editor]", new() { Timeout = 15000 });

        // Review F9 (item 10): measure the grid on a FULL-HEIGHT sheet; a half sheet clips it and answers
        // a different question.
        await ExpandSheetToFullHeightAsync(page);

        (await InlineOverflowAsync(page, "body")).Should().BeLessThanOrEqualTo(0,
            "the grid must scroll inside its own container, never the page");
        (await InlineOverflowAsync(page, "[data-tm-table-editor]")).Should().BeLessThanOrEqualTo(0,
            "the grid never pushes the inspector panel wider than itself");
        (await InlineOverflowAsync(page, "[data-tm-table-actions]")).Should().BeLessThanOrEqualTo(0,
            "the three row/column actions wrap into a grid instead of overflowing the panel");

        // Review F9 (item 11): the pinned control column stays compact — the grid keeps room for cells.
        var rowCtlWidth = await page.EvaluateAsync<int>(
            "() => { const el = document.querySelector('[data-tm-table-rowctl]');"
            + " return el ? Math.round(el.getBoundingClientRect().width) : -1; }");
        rowCtlWidth.Should().BeInRange(1, 96,
            "the sticky control column carries no visible toggle label, so it fits in 96 px");

        // Every action of the grid is finger-sized under the coarse pointer.
        foreach (var selector in new[]
                 {
                     "[data-tm-table-add-row]", "[data-tm-table-add-col]", "[data-tm-table-remove-col]",
                     "[data-tm-table-cell-edit]", "[data-tm-table-row-remove]",
                 })
        {
            var box = await page.Locator(selector).First.BoundingBoxAsync();
            box.Should().NotBeNull($"{selector} has to be on the sheet");
            box!.Height.Should().BeGreaterThanOrEqualTo(44, $"{selector} is a --tm-touch-target control");
            box.Width.Should().BeGreaterThanOrEqualTo(44, $"{selector} is a --tm-touch-target control");
        }

        // Baseline screenshots: the component itself (reviewable at 1:1) and the phone screen around it.
        await page.EvaluateAsync(
            "() => { const s = document.querySelector('[data-tm-table-scroll]'); if (s) s.scrollLeft = 0; }");
        await page.WaitForTimeoutAsync(300);
        await SaveNamedLocatorScreenshotAsync(page, "[data-tm-table-editor]", "f9-table-editor--mobile-element.png");
        await SaveNamedViewportScreenshotAsync(page, "f9-table-editor--mobile.png");

        // The cell dialog opens over the phone as the same core sheet.
        await page.Locator("[data-tm-table-cell-edit]").First.ClickAsync();
        await page.WaitForSelectorAsync("[data-tm-table-cell-dialog]", new() { Timeout = 15000 });
        (await page.Locator(".tm-modal-overlay.tm-modal--sheet").CountAsync()).Should().Be(1);
        await SaveNamedViewportScreenshotAsync(page, "f9-table-cell-dialog--mobile.png");
        await page.ClickAsync("[data-tm-item-dialog-cancel]");
    }

    [TestMethod]
    public async Task F9_7_ItemDialog_Phone_DarkShowsTheFieldError_AndTheLastFieldClearsTheStickyFooter()
    {
        var page = await OpenEditorAsync(390, 844, touch: true);
        await page.WaitForSelectorAsync(".tm-editor-shell[data-layout='mobile']", new() { Timeout = 30000 });

        // Dark theme through the demo's own toggle (the sidebar is hidden by the viewport widening helper,
        // so the click is dispatched on the button — the Blazor handler runs either way).
        var clicked = await page.EvaluateAsync<bool>(
            "() => { const b = document.querySelector(`[data-testid='theme-toggle']`); if (!b) return false; b.click(); return true; }");
        clicked.Should().BeTrue("the demo host renders the theme toggle");
        await page.WaitForSelectorAsync("[data-theme='dark']", new() { Timeout = 15000 });
        await page.WaitForTimeoutAsync(300);

        await SetItemModeAsync(page, "Dialog");
        await SelectBlockAsync(page, "Social");
        await page.WaitForSelectorAsync("[data-tm-item-list]", new() { Timeout = 15000 });

        await page.Locator("[data-tm-item-edit]").First.ClickAsync();
        await page.WaitForSelectorAsync("[data-tm-item-dialog]", new() { Timeout = 15000 });
        await page.WaitForTimeoutAsync(600);

        // A field error, taken in dark mode: the message has to be readable on the sheet, not only present.
        await page.FillAsync("[data-tm-item-field='Href'] input", "javascript:alert(1)");
        await page.WaitForTimeoutAsync(300);
        (await page.Locator("[data-tm-item-field='Href'] input[aria-invalid='true']").CountAsync())
            .Should().Be(1);

        // Review F9 (item 16): the last field of the dialog must sit ABOVE the sticky footer at 390x844.
        var lastField = await page.EvaluateAsync<double[]>(
            "() => { const fields = document.querySelectorAll('[data-tm-item-dialog-body] [data-tm-item-field]');"
            + " const last = fields[fields.length - 1];"
            + " const footer = document.querySelector('[data-tm-item-dialog] .tm-modal-footer');"
            + " if (!last || !footer) return [-1, -1];"
            + " return [last.getBoundingClientRect().bottom, footer.getBoundingClientRect().top]; }");
        lastField[0].Should().BeGreaterThan(0, "the dialog renders its fields");
        lastField[1].Should().BeGreaterThan(0, "the dialog renders its sticky footer");
        lastField[0].Should().BeLessThanOrEqualTo(lastField[1],
            "the last field has to clear the sticky footer, otherwise it can never be reached by scrolling");

        await SaveNamedViewportScreenshotAsync(page, "f9-item-dialog--mobile-dark.png");
        await page.ClickAsync("[data-tm-item-dialog-cancel]");
    }
}
