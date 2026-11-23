using System.Text.Json;
using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Tempo.Blazor.E2E.CanvasEngine;

namespace Tempo.Blazor.E2E;

/// <summary>Phase 16 E2E coverage for canvas headers, footers, fields, notes, and page setup.</summary>
[TestClass]
[TestCategory("DocumentEditor")]
[TestCategory("DocumentEditor:CanvasEngine")]
[DoNotParallelize]
public sealed class DocumentEditorCanvasHeadersFootersNotesE2ETests : WasmTestBase
{
    private const string Phase16DocumentId = "phase-16-canvas-headers-footers-notes";

    [TestInitialize]
    public Task ResetDocumentEditorDemoAsync()
        => DocumentEditorE2EReset.ResetAsync();

    [TestMethod]
    public async Task Phase16_CanvasHeadersFootersFieldsNotesAndPageSetupPersist()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(1440, 1000);
        await OpenPhase16DocumentAsync(page);

        var output = CreateOutputDirectory("desktop-1440x1000");
        var beforePath = Path.Combine(output, "00-phase16-headers-footers-notes-before.png");
        var editingPath = Path.Combine(output, "01-phase16-header-editing.png");
        var commandPath = Path.Combine(output, "02-phase16-commands-page-setup.png");
        var afterPath = Path.Combine(output, "03-phase16-headers-footers-notes-after-save.png");
        var reloadPath = Path.Combine(output, "04-phase16-headers-footers-notes-after-reload.png");

        await page.GetByTestId("document-editor-demo").ScreenshotAsync(new LocatorScreenshotOptions
        {
            Path = beforePath,
            Type = ScreenshotType.Png
        });

        var initialProbe = await ReadPhase16ProbeAcrossDocumentAsync(page);
        Assert.AreEqual(Phase16DocumentId, initialProbe.ModelDocumentId);
        Assert.IsTrue(initialProbe.PageCount >= 3, $"Expected at least three pages for first/even/odd header-footer scopes. Actual: {initialProbe.PageCount}.");
        Assert.IsTrue(initialProbe.FirstPageHasFirstScope, "The first rendered page must use FirstPage header/footer scope.");
        Assert.IsTrue(initialProbe.HasEvenScope, "An even rendered page must use EvenPages header/footer scope.");
        Assert.IsTrue(initialProbe.HasOddScopeAfterFirst, "An odd rendered page after the first page must use OddPages header/footer scope.");
        Assert.IsTrue(initialProbe.TotalFieldCount >= 4, $"Expected resolved field commands in headers and footers. Actual: {initialProbe.TotalFieldCount}.");
        Assert.IsTrue(initialProbe.HasFootnoteRegion, "Footnote region must render on the reference page.");
        Assert.IsTrue(initialProbe.HasEndnoteRegion, "Endnote region must render at document end.");
        Assert.IsTrue(initialProbe.DifferentFirstPage, "The seed section must start with different first page enabled.");
        Assert.IsTrue(initialProbe.DifferentOddAndEvenPages, "The seed section must start with different odd/even enabled.");

        var firstHeader = await ReadHeaderFooterRegionRectAsync(page, "FirstPage", "Header");
        await page.Mouse.DblClickAsync((float)firstHeader.CenterX, (float)firstHeader.CenterY);
        await WaitForPredicateAsync(page, """
            () => document.querySelector('[data-testid="document-canvas-engine-root"]')?.getAttribute('data-canvas-header-footer-editing') === 'true'
                && document.querySelector('[data-testid="document-canvas-engine-root"]')?.getAttribute('data-canvas-header-footer-edit-region') === 'Header'
            """, 10_000);
        var headerEditResult = await ExecuteCanvasCommandAsync(page, "replacerange", new
        {
            blockId = "canvas-phase16-header-first-block",
            start = 29,
            end = 29,
            text = " edited"
        });
        Assert.IsTrue(headerEditResult.Handled, headerEditResult.Debug);
        Assert.IsTrue(headerEditResult.Changed, headerEditResult.Debug);
        await WaitForLastCanvasCommandAsync(page, "replacerange");
        await WaitForPredicateAsync(page, """
            () => Array.from(document.querySelectorAll('[data-canvas-text-rect]'))
                .some(rect => (rect.getAttribute('data-canvas-text') || '').includes('edited'))
            """, 10_000);
        await page.GetByTestId("document-editor-demo").ScreenshotAsync(new LocatorScreenshotOptions
        {
            Path = editingPath,
            Type = ScreenshotType.Png
        });
        await ClickTextRectAsync(page, "canvas-phase16-intro");
        await WaitForPredicateAsync(page, """
            () => document.querySelector('[data-testid="document-canvas-engine-root"]')?.getAttribute('data-canvas-header-footer-editing') === 'false'
            """, 10_000);

        var toggleFirstOffResult = await ExecuteCanvasCommandAsync(page, "differentFirstPage", new { });
        Assert.IsTrue(toggleFirstOffResult.Handled, toggleFirstOffResult.Debug);
        Assert.IsTrue(toggleFirstOffResult.Changed, toggleFirstOffResult.Debug);
        await WaitForLastCanvasCommandAsync(page, "differentFirstPage");
        var firstOffProbe = await ReadPhase16ProbeAcrossDocumentAsync(page);
        Assert.IsFalse(firstOffProbe.DifferentFirstPage, "differentFirstPage command should disable first-page header/footer scope.");
        Assert.IsTrue(firstOffProbe.FirstPageHasPrimaryScope, "First page should fall back to Primary scope while different-first-page is disabled.");

        var toggleOddEvenOffResult = await ExecuteCanvasCommandAsync(page, "differentOddEven", new { });
        Assert.IsTrue(toggleOddEvenOffResult.Handled, toggleOddEvenOffResult.Debug);
        Assert.IsTrue(toggleOddEvenOffResult.Changed, toggleOddEvenOffResult.Debug);
        await WaitForLastCanvasCommandAsync(page, "differentOddEven");
        var oddEvenOffProbe = await ReadPhase16ProbeAcrossDocumentAsync(page);
        Assert.IsFalse(oddEvenOffProbe.DifferentOddAndEvenPages, "differentOddEven command should disable odd/even header/footer scope.");
        Assert.IsFalse(oddEvenOffProbe.HasEvenScope, "Even-page scope should not render while different-odd-even is disabled.");
        Assert.IsFalse(oddEvenOffProbe.HasOddScopeAfterFirst, "Odd-page scope should not render while different-odd-even is disabled.");

        var toggleFirstOnResult = await ExecuteCanvasCommandAsync(page, "differentFirstPage", new { });
        Assert.IsTrue(toggleFirstOnResult.Handled, toggleFirstOnResult.Debug);
        Assert.IsTrue(toggleFirstOnResult.Changed, toggleFirstOnResult.Debug);
        await WaitForLastCanvasCommandAsync(page, "differentFirstPage");
        var toggleOddEvenOnResult = await ExecuteCanvasCommandAsync(page, "differentOddEven", new { });
        Assert.IsTrue(toggleOddEvenOnResult.Handled, toggleOddEvenOnResult.Debug);
        Assert.IsTrue(toggleOddEvenOnResult.Changed, toggleOddEvenOnResult.Debug);
        await WaitForLastCanvasCommandAsync(page, "differentOddEven");
        var togglesRestoredProbe = await ReadPhase16ProbeAcrossDocumentAsync(page);
        Assert.IsTrue(togglesRestoredProbe.FirstPageHasFirstScope);
        Assert.IsTrue(togglesRestoredProbe.HasEvenScope);
        Assert.IsTrue(togglesRestoredProbe.HasOddScopeAfterFirst);

        var pageNumberResult = await ExecuteCanvasCommandAsync(page, "insertPageNumber", new
        {
            blockId = "canvas-phase16-header-first-block",
            offset = 22
        });
        Assert.IsTrue(pageNumberResult.Handled, pageNumberResult.Debug);
        Assert.IsTrue(pageNumberResult.Changed, pageNumberResult.Debug);
        await WaitForLastCanvasCommandAsync(page, "insertPageNumber");
        await WaitForPredicateAsync(page, """
            () => Array.from(document.querySelectorAll('[data-canvas-text-rect][data-block-id="canvas-phase16-header-first-block"]'))
                .some(rect => /^[0-9]+$/.test((rect.getAttribute('data-canvas-text') || '').trim()))
            """, 10_000);

        var noteNumberingResult = await ExecuteCanvasCommandAsync(page, "setPageSettings", new
        {
            sectionId = "canvas-phase16-section-main",
            noteNumbering = new
            {
                style = "lowerRoman",
                startAt = 4,
                restartEachSection = true
            }
        });
        Assert.IsTrue(noteNumberingResult.Handled, noteNumberingResult.Debug);
        Assert.IsTrue(noteNumberingResult.Changed, noteNumberingResult.Debug);
        await WaitForLastCanvasCommandAsync(page, "setPageSettings");
        var afterNoteNumberingProbe = await ReadPhase16ProbeAcrossDocumentAsync(page);
        Assert.AreEqual("lowerRoman", afterNoteNumberingProbe.NoteNumberingStyle);
        Assert.AreEqual(4, afterNoteNumberingProbe.NoteNumberingStartAt);

        var footnoteResult = await ExecuteCanvasCommandAsync(page, "insertFootnote", new
        {
            blockId = "canvas-phase16-note-source",
            offset = 14,
            text = "E2E command footnote body"
        });
        Assert.IsTrue(footnoteResult.Handled, footnoteResult.Debug);
        Assert.IsTrue(footnoteResult.Changed, footnoteResult.Debug);
        await WaitForLastCanvasCommandAsync(page, "insertFootnote");

        var endnoteResult = await ExecuteCanvasCommandAsync(page, "insertEndnote", new
        {
            blockId = "canvas-phase16-note-source",
            offset = 15,
            text = "E2E command endnote body"
        });
        Assert.IsTrue(endnoteResult.Handled, endnoteResult.Debug);
        Assert.IsTrue(endnoteResult.Changed, endnoteResult.Debug);
        await WaitForLastCanvasCommandAsync(page, "insertEndnote");

        var pageBreakResult = await ExecuteCanvasCommandAsync(page, "insertPageBreak", new
        {
            id = "canvas-phase16-command-page-break",
            blockId = "canvas-phase16-intro"
        });
        Assert.IsTrue(pageBreakResult.Handled, pageBreakResult.Debug);
        Assert.IsTrue(pageBreakResult.Changed, pageBreakResult.Debug);
        await WaitForLastCanvasCommandAsync(page, "insertPageBreak");
        var introPageIndex = await WaitForBlockPageIndexAsync(page, "canvas-phase16-intro");
        var noteSourcePageIndex = await WaitForBlockPageIndexAsync(page, "canvas-phase16-note-source");
        Assert.IsTrue(noteSourcePageIndex > introPageIndex, $"insertPageBreak should move the following block to a later page. Intro page: {introPageIndex}, note source page: {noteSourcePageIndex}.");

        var pageSetupResult = await ExecuteCanvasCommandAsync(page, "setPageSettings", new
        {
            sectionId = "canvas-phase16-section-main",
            pageSettings = new
            {
                size = new { name = "Letter", width = 612, height = 792 },
                margins = new { top = 48, right = 54, bottom = 52, left = 54 },
                headerDistanceFromTop = 28,
                footerDistanceFromBottom = 30,
                landscape = true
            }
        });
        Assert.IsTrue(pageSetupResult.Handled, pageSetupResult.Debug);
        Assert.IsTrue(pageSetupResult.Changed, pageSetupResult.Debug);
        await WaitForLastCanvasCommandAsync(page, "setPageSettings");
        await WaitForFirstPageLandscapeAsync(page);

        var afterCommandsProbe = await ReadPhase16ProbeAcrossDocumentAsync(page);
        Assert.IsTrue(afterCommandsProbe.TotalNoteCount >= initialProbe.TotalNoteCount + 2, $"Expected footnote/endnote command insertion to add note markers. Before: {initialProbe.TotalNoteCount}, after: {afterCommandsProbe.TotalNoteCount}.");
        Assert.IsTrue(afterCommandsProbe.HasInsertedHeaderPageNumber);
        Assert.IsTrue(afterCommandsProbe.HasInsertedFootnoteText);
        Assert.IsTrue(afterCommandsProbe.HasInsertedEndnoteText);
        Assert.IsTrue(afterCommandsProbe.HasConfiguredFootnoteMarker, "Inserted footnote marker must follow lower-roman note numbering settings.");
        Assert.IsTrue(afterCommandsProbe.HasInsertedPageBreak, "Page break command must persist in the model.");
        Assert.IsTrue(afterCommandsProbe.FirstPageLogicalWidth > afterCommandsProbe.FirstPageLogicalHeight, $"Expected landscape page setup. Width: {afterCommandsProbe.FirstPageLogicalWidth}, height: {afterCommandsProbe.FirstPageLogicalHeight}.");
        Assert.IsTrue(afterCommandsProbe.FirstPageHasFirstScope);
        Assert.IsTrue(afterCommandsProbe.HasEvenScope);
        Assert.IsTrue(afterCommandsProbe.HasOddScopeAfterFirst);
        Assert.AreEqual("lowerRoman", afterCommandsProbe.NoteNumberingStyle);
        Assert.AreEqual(4, afterCommandsProbe.NoteNumberingStartAt);

        await page.GetByTestId("document-editor-demo").ScreenshotAsync(new LocatorScreenshotOptions
        {
            Path = commandPath,
            Type = ScreenshotType.Png
        });

        await DocumentEditorCanvasVisualAssert.AssertNoTextOverlapAsync(page, "[data-testid='document-canvas-page'][data-page-index='0'] [data-canvas-text-rect]");
        await DocumentEditorCanvasVisualAssert.AssertNoUiOverlapAsync(page);
        var contentMetrics = await DocumentEditorCanvasVisualAssert.AssertCanvasNonBlankAsync(page.Locator("[data-canvas-layer='content']").First);
        var annotationMetrics = await DocumentEditorCanvasVisualAssert.AssertCanvasNonBlankAsync(page.Locator("[data-canvas-layer='annotations']").First);

        await page.GetByTestId("document-save").ClickAsync();
        await WaitForSaveBoundaryAsync(page);
        await page.GetByTestId("document-editor-demo").ScreenshotAsync(new LocatorScreenshotOptions
        {
            Path = afterPath,
            Type = ScreenshotType.Png
        });

        await NavigateWithinBlazorAsync(page, "/canvas-engine-host?documentId=phase-5-canvas-render");
        await WaitForPredicateAsync(page, "() => document.querySelector('[data-testid=\"document-canvas-page\"]')?.getAttribute('data-canvas-model-document-id') === 'phase-5-canvas-render'", 20_000);
        await NavigateWithinBlazorAsync(page, $"/canvas-engine-host?documentId={Phase16DocumentId}&showToolbar=true");
        await WaitForPhase16ReadyAsync(page);
        var reloadedProbe = await ReadPhase16ProbeAcrossDocumentAsync(page);
        Assert.IsTrue(reloadedProbe.HasEditedHeaderText);
        Assert.IsTrue(reloadedProbe.HasInsertedHeaderPageNumber);
        Assert.IsTrue(reloadedProbe.HasInsertedFootnoteText);
        Assert.IsTrue(reloadedProbe.HasInsertedEndnoteText);
        Assert.IsTrue(reloadedProbe.HasConfiguredFootnoteMarker);
        Assert.IsTrue(reloadedProbe.HasInsertedPageBreak);
        Assert.IsTrue(reloadedProbe.FirstPageLogicalWidth > reloadedProbe.FirstPageLogicalHeight, $"Expected saved landscape page setup after reload. Width: {reloadedProbe.FirstPageLogicalWidth}, height: {reloadedProbe.FirstPageLogicalHeight}.");
        Assert.IsTrue(reloadedProbe.DifferentFirstPage);
        Assert.IsTrue(reloadedProbe.DifferentOddAndEvenPages);
        Assert.AreEqual("lowerRoman", reloadedProbe.NoteNumberingStyle);
        Assert.AreEqual(4, reloadedProbe.NoteNumberingStartAt);
        Assert.IsTrue(reloadedProbe.NoteNumberingRestartEachSection);

        await page.GetByTestId("document-editor-demo").ScreenshotAsync(new LocatorScreenshotOptions
        {
            Path = reloadPath,
            Type = ScreenshotType.Png
        });

        var manifestPath = Path.Combine(output, "manifest.json");
        await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(new
        {
            testName = nameof(Phase16_CanvasHeadersFootersFieldsNotesAndPageSetupPersist),
            seedDocumentId = Phase16DocumentId,
            userActions = new[]
            {
                "Open the phase 16 canvas seed document.",
                "Verify first/even/odd header and footer scopes, automatic field commands, footnote regions, and endnote regions.",
                "Double-click the first page header, insert header text through the shared production canvas command runtime, and verify the header editing context.",
                "Click back into body text to close the header/footer edit context.",
                "Toggle different-first-page and different-odd-even scopes off and back on through the shared production canvas command runtime.",
                "Insert a page number field into the active first-page header through the shared production canvas command runtime.",
                "Set lower-roman note numbering through the shared production page setup command runtime.",
                "Insert a footnote and endnote through the shared production canvas command runtime and verify their rendered note bodies.",
                "Insert a page break through the shared production canvas command runtime and verify hard pagination.",
                "Change the section page setup to landscape Letter geometry through the shared production canvas command runtime.",
                "Save through the production Save command, navigate away, reload the document, and verify persisted header/footer, note, page break, and page setup state."
            },
            expectedVisibleChanges = "The canvas paints professional header and footer bands on every page, resolves document fields into content-layer text, allows first-page header editing through the canvas engine, closes header/footer editing back to the body, toggles first/odd-even scopes, inserts a real page-number field into the header, reserves note regions for seeded and command-inserted footnotes/endnotes with lower-roman numbering, applies a hard page break and landscape page setup, and preserves the state through save/reload.",
            screenshotPaths = new[] { beforePath, editingPath, commandPath, afterPath, reloadPath },
            initialProbe,
            headerEditResult,
            toggleFirstOffResult,
            toggleOddEvenOffResult,
            toggleFirstOnResult,
            toggleOddEvenOnResult,
            pageNumberResult,
            noteNumberingResult,
            footnoteResult,
            endnoteResult,
            pageBreakResult,
            pageSetupResult,
            afterNoteNumberingProbe,
            afterCommandsProbe,
            reloadedProbe,
            contentMetrics,
            annotationMetrics
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));

        TestContext.AddResultFile(beforePath);
        TestContext.AddResultFile(editingPath);
        TestContext.AddResultFile(commandPath);
        TestContext.AddResultFile(afterPath);
        TestContext.AddResultFile(reloadPath);
        TestContext.AddResultFile(manifestPath);
    }

    [TestMethod]
    public async Task Phase3_RibbonHeaderFooterToggles_RouteEngineCommandsAndPersist()
    {
        // Regression (command-layer plan phase 3): the ribbon Different-first-page / Different-odd-even
        // buttons routed command ids the engine never registered (toggleDifferentFirstPage /
        // toggleDifferentOddEven), so both were silent no-ops. This drives the toggles through the REAL
        // ribbon (not the command runtime directly) and asserts the layout, the ribbon state, and
        // save/reload persistence.
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(1440, 1000);
        await OpenPhase16DocumentAsync(page);

        var output = CreateOutputDirectory("phase3-ribbon-header-footer-toggles");
        var beforePath = Path.Combine(output, "00-first-page-header-active.png");
        var afterTogglePath = Path.Combine(output, "01-first-page-header-disabled.png");
        var reloadPath = Path.Combine(output, "02-after-reload-persisted.png");

        // Seed starts with differentFirstPage=true → page 1 renders the FirstPage-scope header text.
        var initialProbe = await ReadPhase16ProbeAcrossDocumentAsync(page);
        Assert.IsTrue(initialProbe.DifferentFirstPage, "Seed must start with different first page enabled.");
        StringAssert.Contains(await ReadFirstPageLayoutTextsAsync(page), "First page header");
        await page.GetByTestId("document-editor-demo").ScreenshotAsync(new LocatorScreenshotOptions { Path = beforePath, Type = ScreenshotType.Png });

        await page.GetByTestId("document-ribbon-tab-layout").ClickAsync();
        var firstPageToggle = page.GetByTestId("document-different-first-page");
        await Assertions.Expect(firstPageToggle).ToBeVisibleAsync(new() { Timeout = 10_000 });
        await Assertions.Expect(firstPageToggle).ToHaveAttributeAsync("aria-pressed", "true");

        // Turn OFF through the ribbon: page 1 must fall back to the Primary header (layout snapshot is
        // the contract here — header text is NOT in the canvas a11y mirror).
        await firstPageToggle.ClickAsync();
        await WaitForPredicateAsync(page, """
            async () => {
                const host = document.querySelector('[data-testid="document-canvas-engine-host"]');
                const handle = host?.getAttribute('data-canvas-engine-handle') || '';
                const module = await import('/_content/Tempo.Blazor.DocumentEditor/js/document-editor-canvas/interop.mjs');
                const model = JSON.parse(module.getModelJson(handle) || '{}');
                const section = (model.sections || [])[0];
                return section?.properties?.differentFirstPage !== true;
            }
            """, 10_000);
        await Assertions.Expect(firstPageToggle).ToHaveAttributeAsync("aria-pressed", "false", new() { Timeout = 10_000 });

        var disabledTexts = await ReadFirstPageLayoutTextsAsync(page);
        Assert.IsFalse(disabledTexts.Contains("First page header", StringComparison.Ordinal),
            $"With different-first-page off, page 1 must not render the FirstPage header. Page 1 texts: {disabledTexts}");
        await page.GetByTestId("document-editor-demo").ScreenshotAsync(new LocatorScreenshotOptions { Path = afterTogglePath, Type = ScreenshotType.Png });

        // Edge case: the odd/even ribbon toggle routes too, and re-clicking is idempotent set-mode —
        // the ribbon state and engine flag stay in lockstep in both directions.
        var oddEvenToggle = page.GetByTestId("document-different-odd-even");
        await Assertions.Expect(oddEvenToggle).ToHaveAttributeAsync("aria-pressed", "true");
        await oddEvenToggle.ClickAsync();
        await Assertions.Expect(oddEvenToggle).ToHaveAttributeAsync("aria-pressed", "false", new() { Timeout = 10_000 });
        await oddEvenToggle.ClickAsync();
        await Assertions.Expect(oddEvenToggle).ToHaveAttributeAsync("aria-pressed", "true", new() { Timeout = 10_000 });

        // Persist: save with different-first-page OFF, reload, and verify both the model flag and the
        // first-page layout stayed off.
        await page.GetByTestId("document-save").ClickAsync();
        await WaitForSaveBoundaryAsync(page);

        await NavigateWithinBlazorAsync(page, "/canvas-engine-host?documentId=phase-5-canvas-render");
        await WaitForPredicateAsync(page, "() => document.querySelector('[data-testid=\"document-canvas-page\"]')?.getAttribute('data-canvas-model-document-id') === 'phase-5-canvas-render'", 20_000);
        await NavigateWithinBlazorAsync(page, $"/canvas-engine-host?documentId={Phase16DocumentId}&showToolbar=true");
        await WaitForPhase16ReloadReadyAsync(page);

        var reloadedProbe = await ReadPhase16ProbeAcrossDocumentAsync(page);
        Assert.IsFalse(reloadedProbe.DifferentFirstPage, "The disabled different-first-page flag must survive save/reload.");
        Assert.IsTrue(reloadedProbe.DifferentOddAndEvenPages, "The re-enabled different-odd-even flag must survive save/reload.");
        var reloadedTexts = await ReadFirstPageLayoutTextsAsync(page);
        Assert.IsFalse(reloadedTexts.Contains("First page header", StringComparison.Ordinal),
            $"After reload, page 1 must still not render the FirstPage header. Page 1 texts: {reloadedTexts}");

        await page.GetByTestId("document-ribbon-tab-layout").ClickAsync();
        await Assertions.Expect(page.GetByTestId("document-different-first-page"))
            .ToHaveAttributeAsync("aria-pressed", "false", new() { Timeout = 10_000 });
        await page.GetByTestId("document-editor-demo").ScreenshotAsync(new LocatorScreenshotOptions { Path = reloadPath, Type = ScreenshotType.Png });

        TestContext.AddResultFile(beforePath);
        TestContext.AddResultFile(afterTogglePath);
        TestContext.AddResultFile(reloadPath);
    }

    /// <summary>Text drawn on page 1 according to the print layout snapshot (headers are not mirrored).</summary>
    private static Task<string> ReadFirstPageLayoutTextsAsync(IPage page)
        => page.EvaluateAsync<string>(
            """
            async () => {
                const host = document.querySelector('[data-testid="document-canvas-engine-host"]');
                const handle = host?.getAttribute('data-canvas-engine-handle') || '';
                const module = await import('/_content/Tempo.Blazor.DocumentEditor/js/document-editor-canvas/interop.mjs');
                const snapshot = JSON.parse(module.getLayoutSnapshotJson(handle) || '{}');
                const first = (snapshot.pages || [])[0];
                const texts = [];
                for (const command of first?.commands || []) {
                    if (typeof command.text === 'string') {
                        texts.push(command.text);
                    }
                }
                return texts.join('\n');
            }
            """);

    /// <summary>Reload readiness without assuming the first-page header scope (the toggle test disables it).</summary>
    private static Task WaitForPhase16ReloadReadyAsync(IPage page)
        => WaitForPredicateAsync(page, """
            () => {
                const host = document.querySelector('[data-testid="document-canvas-engine-host"]');
                const first = document.querySelector('[data-testid="document-canvas-page"][data-canvas-model-document-id="phase-16-canvas-headers-footers-notes"]');
                return (host?.getAttribute('data-canvas-engine-handle') || '').length > 0
                    && first
                    && Number(first.getAttribute('data-canvas-header-footer-count') || '0') >= 1;
            }
            """, 90_000);

    private async Task OpenPhase16DocumentAsync(IPage page)
    {
        await GotoAppReadyAsync(page, $"{BaseUrl}/canvas-engine-host?documentId={Phase16DocumentId}&showToolbar=true");
        await WaitForPhase16ReadyAsync(page);
    }

    private static Task WaitForPhase16ReadyAsync(IPage page)
        => WaitForPredicateAsync(page, """
            () => {
                const host = document.querySelector('[data-testid="document-canvas-engine-host"]');
                const root = document.querySelector('[data-testid="document-canvas-engine-root"]');
                const first = document.querySelector('[data-testid="document-canvas-page"][data-canvas-model-document-id="phase-16-canvas-headers-footers-notes"]');
                const pageCount = Number(root?.getAttribute('data-canvas-page-count') || '0');
                return (host?.getAttribute('data-canvas-engine-handle') || '').length > 0
                    && first
                    && pageCount >= 1
                    && Number(first.getAttribute('data-canvas-header-footer-count') || '0') >= 2
                    && document.querySelector('[data-canvas-header-footer-region][data-scope="FirstPage"]')
                    && Array.from(document.querySelectorAll('[data-canvas-text-rect][data-block-id="canvas-phase16-intro"]'))
                        .some(rect => (rect.getAttribute('data-canvas-text') || '') === 'verifies');
            }
            """, 90_000);

    private static async Task<Phase16Probe> ReadPhase16ProbeAcrossDocumentAsync(IPage page)
    {
        var probe = await ReadPhase16ProbeAsync(page);
        var model = await ReadPhase16ModelProbeAsync(page);
        probe.PageCount = Math.Max(probe.PageCount, model.RenderedPageCount);
        probe.ModelFieldCount = model.FieldCount;
        probe.TotalFieldCount = Math.Max(probe.TotalFieldCount, model.FieldCount);
        probe.TotalNoteCount = model.NoteCount;
        probe.FirstPageHasFirstScope = probe.FirstPageHasFirstScope || model.HasFirstScope;
        probe.FirstPageHasPrimaryScope = probe.FirstPageHasPrimaryScope || model.FirstPageUsesPrimaryFallback;
        probe.HasEvenScope = probe.HasEvenScope || model.HasEvenScope;
        probe.HasOddScopeAfterFirst = probe.HasOddScopeAfterFirst || model.HasOddScope;
        probe.HasFootnoteRegion = probe.HasFootnoteRegion || model.HasFootnote;
        probe.HasEndnoteRegion = probe.HasEndnoteRegion || model.HasEndnote;
        probe.DifferentFirstPage = model.DifferentFirstPage;
        probe.DifferentOddAndEvenPages = model.DifferentOddAndEvenPages;
        probe.NoteNumberingStyle = model.NoteNumberingStyle;
        probe.NoteNumberingStartAt = model.NoteNumberingStartAt;
        probe.NoteNumberingRestartEachSection = model.NoteNumberingRestartEachSection;
        probe.HasEditedHeaderText = probe.HasEditedHeaderText || model.HasEditedHeaderText;
        probe.HasInsertedHeaderPageNumber = probe.HasInsertedHeaderPageNumber || model.HasInsertedHeaderPageNumber;
        probe.HasInsertedFootnoteText = probe.HasInsertedFootnoteText || model.HasInsertedFootnoteText;
        probe.HasInsertedEndnoteText = probe.HasInsertedEndnoteText || model.HasInsertedEndnoteText;
        probe.HasConfiguredFootnoteMarker = probe.HasConfiguredFootnoteMarker || model.HasConfiguredFootnoteMarker;
        probe.HasInsertedPageBreak = model.HasInsertedPageBreak;
        return probe;
    }

    private static Task<Phase16Probe> ReadPhase16ProbeAsync(IPage page)
        => page.EvaluateAsync<Phase16Probe>(
            """
            () => {
                const root = document.querySelector('[data-testid="document-canvas-engine-root"]');
                const pages = Array.from(document.querySelectorAll('[data-testid="document-canvas-page"][data-canvas-model-document-id]'));
                const first = pages[0];
                const summaries = pages.map((page, index) => {
                    const scopes = Array.from(page.querySelectorAll('[data-canvas-header-footer-region]'))
                        .map(region => region.getAttribute('data-scope') || '');
                    const noteTypes = Array.from(page.querySelectorAll('[data-canvas-note-region]'))
                        .map(region => region.getAttribute('data-note-type') || '');
                    return {
                        index: Number(page.getAttribute('data-page-index') || index),
                        headerFooterCount: Number(page.getAttribute('data-canvas-header-footer-count') || '0'),
                        fieldCount: Number(page.getAttribute('data-canvas-field-count') || '0'),
                        noteCount: Number(page.getAttribute('data-canvas-note-count') || '0'),
                        scopes,
                        noteTypes
                    };
                });
                return {
                    modelDocumentId: first?.getAttribute('data-canvas-model-document-id') || '',
                    pageCount: Number(root?.getAttribute('data-canvas-page-count') || pages.length),
                    mountedPageCount: pages.length,
                    modelFieldCount: Number(first?.getAttribute('data-canvas-model-field-count') || '0'),
                    totalHeaderFooterCount: summaries.reduce((total, item) => total + item.headerFooterCount, 0),
                    totalFieldCount: summaries.reduce((total, item) => total + item.fieldCount, 0),
                    totalNoteCount: summaries.reduce((total, item) => total + item.noteCount, 0),
                    firstPageLogicalWidth: Number(first?.getAttribute('data-canvas-page-logical-width') || '0'),
                    firstPageLogicalHeight: Number(first?.getAttribute('data-canvas-page-logical-height') || '0'),
                    firstPageHasFirstScope: summaries.some(item => item.index === 0 && item.scopes.includes('FirstPage')),
                    firstPageHasPrimaryScope: summaries.some(item => item.index === 0 && item.scopes.includes('Primary')),
                    hasEvenScope: summaries.some(item => item.scopes.includes('EvenPages')),
                    hasOddScopeAfterFirst: summaries.some(item => item.index > 0 && item.scopes.includes('OddPages')),
                    hasFootnoteRegion: summaries.some(item => item.noteTypes.includes('Footnote')),
                    hasEndnoteRegion: summaries.some(item => item.noteTypes.includes('Endnote')),
                    hasEditedHeaderText: Array.from(document.querySelectorAll('[data-canvas-text-rect]'))
                        .some(rect => (rect.getAttribute('data-canvas-text') || '').includes('edited')),
                    hasInsertedHeaderPageNumber: Array.from(document.querySelectorAll('[data-canvas-text-rect][data-block-id="canvas-phase16-header-first-block"]'))
                        .some(rect => /^[0-9]+$/.test((rect.getAttribute('data-canvas-text') || '').trim())),
                    hasInsertedFootnoteText: Array.from(document.querySelectorAll('[data-canvas-text-rect]'))
                        .some(rect => (rect.getAttribute('data-canvas-text') || '').includes('E2E command footnote body')),
                    hasInsertedEndnoteText: Array.from(document.querySelectorAll('[data-canvas-text-rect]'))
                        .some(rect => (rect.getAttribute('data-canvas-text') || '').includes('E2E command endnote body')),
                    pages: summaries
                };
            }
            """);

    private static Task<Phase16ModelProbe> ReadPhase16ModelProbeAsync(IPage page)
        => page.EvaluateAsync<Phase16ModelProbe>(
            """
            async () => {
                const host = document.querySelector('[data-testid="document-canvas-engine-host"]');
                const handle = host?.getAttribute('data-canvas-engine-handle') || '';
                const module = await import('/_content/Tempo.Blazor.DocumentEditor/js/document-editor-canvas/interop.mjs');
                const model = JSON.parse(module.getModelJson(handle) || '{}');
                const arrayOf = value => Array.isArray(value) ? value : [];
                const headersFooters = arrayOf(model.headersFooters || model.HeadersFooters);
                const notes = arrayOf(model.notes || model.Notes);
                const sections = arrayOf(model.sections || model.Sections);
                const body = model.body || model.Body || {};
                const bodyBlocks = arrayOf(body.blocks || body.Blocks);
                const section = sections.find(item => String(item?.id || item?.Id || '') === 'canvas-phase16-section-main') || sections[0] || null;
                const properties = section?.properties || section?.Properties || {};
                const renderedPageCount = Number(document.querySelector('[data-testid="document-canvas-engine-root"]')?.getAttribute('data-canvas-page-count') || '0');

                const blockRuns = block => Array.isArray(block?.content?.runs)
                    ? block.content.runs
                    : (Array.isArray(block?.content?.inlines)
                        ? block.content.inlines
                        : (Array.isArray(block?.Content?.Runs)
                            ? block.Content.Runs
                            : (Array.isArray(block?.Content?.Inlines) ? block.Content.Inlines : [])));
                const headerFirst = headersFooters.flatMap(item => arrayOf(item?.blocks || item?.Blocks))
                    .find(block => String(block?.id || block?.Id || '') === 'canvas-phase16-header-first-block');
                const headerFirstFieldCount = blockRuns(headerFirst).filter(run => String(run?.type || run?.Type || '').toLowerCase() === 'field' || run?.field || run?.Field).length;
                const allBlocks = [
                    ...bodyBlocks,
                    ...headersFooters.flatMap(item => arrayOf(item?.blocks || item?.Blocks)),
                    ...notes.flatMap(note => arrayOf(note?.blocks || note?.Blocks)),
                ];
                const fieldCount = allBlocks.reduce((total, block) => total + blockRuns(block).filter(run => String(run?.type || run?.Type || '').toLowerCase() === 'field' || run?.field || run?.Field).length, 0);
                const noteText = notes.flatMap(note => arrayOf(note?.blocks || note?.Blocks))
                    .flatMap(block => blockRuns(block))
                    .map(run => String(run?.text || run?.Text || ''))
                    .join(' ');
                const headerFooterText = headersFooters.flatMap(item => arrayOf(item?.blocks || item?.Blocks))
                    .flatMap(block => blockRuns(block))
                    .map(run => String(run?.text || run?.Text || ''))
                    .join(' ');
                const scopes = headersFooters.map(item => String(item?.scope || item?.Scope || '').toLowerCase());
                const noteTypes = notes.map(item => String(item?.type ?? item?.Type ?? '').toLowerCase());
                const noteMarkers = notes.map(item => String(item?.marker ?? item?.Marker ?? ''));
                const pageBreaks = bodyBlocks.filter(block => String(block?.type || block?.Type || '').toLowerCase() === 'pagebreak');
                const noteNumbering = properties.noteNumbering || properties.NoteNumbering || {};
                const differentFirstPage = properties.differentFirstPage === true;
                const differentOddAndEvenPages = properties.differentOddAndEvenPages === true;
                return {
                    renderedPageCount,
                    fieldCount,
                    noteCount: notes.length,
                    differentFirstPage,
                    differentOddAndEvenPages,
                    firstPageUsesPrimaryFallback: !differentFirstPage && scopes.includes('primary'),
                    hasFirstScope: differentFirstPage && scopes.includes('firstpage'),
                    hasEvenScope: differentOddAndEvenPages && scopes.includes('evenpages'),
                    hasOddScope: differentOddAndEvenPages && scopes.includes('oddpages'),
                    noteNumberingStyle: String(noteNumbering.style || noteNumbering.Style || 'decimal'),
                    noteNumberingStartAt: Number(noteNumbering.startAt ?? noteNumbering.StartAt ?? 1) || 1,
                    noteNumberingRestartEachSection: (noteNumbering.restartEachSection ?? noteNumbering.RestartEachSection) !== false,
                    hasFootnote: noteTypes.includes('footnote') || noteTypes.includes('0'),
                    hasEndnote: noteTypes.includes('endnote') || noteTypes.includes('1'),
                    hasEditedHeaderText: headerFooterText.includes('edited'),
                    hasInsertedHeaderPageNumber: headerFirstFieldCount >= 2,
                    hasInsertedFootnoteText: noteText.includes('E2E command footnote body'),
                    hasInsertedEndnoteText: noteText.includes('E2E command endnote body'),
                    hasConfiguredFootnoteMarker: noteMarkers.includes('v'),
                    hasInsertedPageBreak: pageBreaks.some(block => String(block?.id || block?.Id || '') === 'canvas-phase16-command-page-break')
                };
            }
            """);

    private static Task<DomRectProbe> ReadHeaderFooterRegionRectAsync(IPage page, string scope, string region)
        => page.EvaluateAsync<DomRectProbe>(
            """
            ([scope, region]) => {
                const node = Array.from(document.querySelectorAll('[data-canvas-header-footer-region]'))
                    .find(item => item.getAttribute('data-scope') === scope && item.getAttribute('data-region') === region);
                if (!node) {
                    throw new Error(`Header/footer region not found: ${scope} ${region}`);
                }

                const rect = node.getBoundingClientRect();
                return {
                    x: rect.x,
                    y: rect.y,
                    width: rect.width,
                    height: rect.height,
                    centerX: rect.x + rect.width / 2,
                    centerY: rect.y + rect.height / 2
                };
            }
            """,
            new[] { scope, region });

    private static async Task WaitForSaveBoundaryAsync(IPage page)
    {
        await WaitForPredicateAsync(page, """
            () => {
                const saveMessage = document.querySelector('[data-testid="document-save-message"]')?.textContent || '';
                const lastSaved = document.querySelector('[data-testid="document-last-saved"]')?.textContent || '';
                const pending = document.querySelector('[data-testid="document-pending-status"]')?.textContent || '';
                const saveButtonDisabled = document.querySelector('[data-testid="document-save"]')?.hasAttribute('disabled') === true;
                return saveButtonDisabled === false
                    && pending.trim().length === 0
                    && (/Saved|Autosaved/i.test(saveMessage) || /saved/i.test(lastSaved));
            }
            """, 10_000);
    }

    private static async Task<Phase16CommandProbe> ExecuteCanvasCommandAsync(IPage page, string commandId, object payload)
    {
        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        return await page.EvaluateAsync<Phase16CommandProbe>(
            """
            async ({ commandId, json }) => {
                const host = document.querySelector('[data-testid="document-canvas-engine-host"]');
                const handle = host?.getAttribute('data-canvas-engine-handle') || '';
                const module = await import('/_content/Tempo.Blazor.DocumentEditor/js/document-editor-canvas/interop.mjs');
                const raw = module.execCommand(handle, commandId, json);
                const parsed = JSON.parse(raw || '{}');
                return {
                    changed: parsed?.result?.changed === true,
                    handled: parsed?.handled === true,
                    noteId: parsed?.result?.noteId || '',
                    debug: JSON.stringify(parsed)
                };
            }
            """,
            new { commandId, json });
    }

    private static Task WaitForLastCanvasCommandAsync(IPage page, string commandId)
        => WaitForPredicateAsync(page, """
            commandId => {
                const last = document.querySelector('[data-testid="document-canvas-engine-root"]')?.getAttribute('data-canvas-command-last') || '';
                return last.toLowerCase() === commandId.toLowerCase();
            }
            """, commandId, 10_000);

    private static async Task ClickTextRectAsync(IPage page, string blockId)
    {
        var rect = await page.EvaluateAsync<DomRectProbe>(
            """
            blockId => {
                const node = Array.from(document.querySelectorAll(`[data-canvas-text-rect][data-block-id="${blockId}"]`))
                    .find(item => item.getBoundingClientRect().width > 0.5 && item.getBoundingClientRect().height > 0.5);
                if (!node) {
                    throw new Error(`Text rect not found for ${blockId}`);
                }

                const rect = node.getBoundingClientRect();
                return {
                    x: rect.x,
                    y: rect.y,
                    width: rect.width,
                    height: rect.height,
                    centerX: rect.x + rect.width / 2,
                    centerY: rect.y + rect.height / 2
                };
            }
            """,
            blockId);
        await page.Mouse.ClickAsync((float)rect.CenterX, (float)rect.CenterY);
    }

    private static async Task<int> WaitForBlockPageIndexAsync(IPage page, string blockId)
    {
        await WaitForPredicateAsync(page, """
            async blockId => {
                const host = document.querySelector('[data-testid="document-canvas-engine-host"]');
                const handle = host?.getAttribute('data-canvas-engine-handle') || '';
                const module = await import('/_content/Tempo.Blazor.DocumentEditor/js/document-editor-canvas/interop.mjs');
                const debug = JSON.parse(module.getRuntimeDebugSnapshotJson(handle) || '{}');
                const blocks = debug?.render?.selectionLayout?.blocks || debug?.layout?.blocks || [];
                const block = blocks.find(candidate => String(candidate?.blockId || candidate?.id || '') === blockId);
                return block && Number(block.pageIndex || 0) >= 0;
            }
            """, blockId, 10_000);

        return await page.EvaluateAsync<int>(
            """
            async blockId => {
                const host = document.querySelector('[data-testid="document-canvas-engine-host"]');
                const handle = host?.getAttribute('data-canvas-engine-handle') || '';
                const module = await import('/_content/Tempo.Blazor.DocumentEditor/js/document-editor-canvas/interop.mjs');
                const debug = JSON.parse(module.getRuntimeDebugSnapshotJson(handle) || '{}');
                const blocks = debug?.render?.selectionLayout?.blocks || debug?.layout?.blocks || [];
                const block = blocks.find(candidate => String(candidate?.blockId || candidate?.id || '') === blockId);
                return Number(block?.pageIndex || 0) || 0;
            }
            """,
            blockId);
    }

    private static Task WaitForFirstPageLandscapeAsync(IPage page)
        => WaitForPredicateAsync(page, """
            () => {
                const first = document.querySelector('[data-testid="document-canvas-page"][data-canvas-model-document-id]');
                const width = Number(first?.getAttribute('data-canvas-page-logical-width') || '0');
                const height = Number(first?.getAttribute('data-canvas-page-logical-height') || '0');
                return width > height && width > 0 && height > 0;
            }
            """, 10_000);

    private static Task NavigateWithinBlazorAsync(IPage page, string url)
        => page.EvaluateAsync(
            """
            url => window.Blazor?.navigateTo
                ? window.Blazor.navigateTo(url)
                : window.history.pushState({}, '', url)
            """,
            url);

    private static string CreateOutputDirectory(string viewport)
    {
        var output = Path.Combine(
            FindRepositoryRoot().FullName,
            "tests",
            "Tempo.Blazor.E2E",
            "TestResults",
            "document-editor-canvas",
            "phase16-headers-footers-notes",
            "2026-06-04",
            viewport);
        Directory.CreateDirectory(output);
        return output;
    }

    private static DirectoryInfo FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "TempoBlazor.slnx")))
            {
                return current;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate TempoBlazor.slnx from test output directory.");
    }

    private sealed class Phase16Probe
    {
        public string ModelDocumentId { get; set; } = string.Empty;

        public int PageCount { get; set; }

        public int MountedPageCount { get; set; }

        public int ModelFieldCount { get; set; }

        public int TotalHeaderFooterCount { get; set; }

        public int TotalFieldCount { get; set; }

        public int TotalNoteCount { get; set; }

        public int FirstPageLogicalWidth { get; set; }

        public int FirstPageLogicalHeight { get; set; }

        public bool FirstPageHasFirstScope { get; set; }

        public bool FirstPageHasPrimaryScope { get; set; }

        public bool HasEvenScope { get; set; }

        public bool HasOddScopeAfterFirst { get; set; }

        public bool DifferentFirstPage { get; set; }

        public bool DifferentOddAndEvenPages { get; set; }

        public string NoteNumberingStyle { get; set; } = string.Empty;

        public int NoteNumberingStartAt { get; set; }

        public bool NoteNumberingRestartEachSection { get; set; }

        public bool HasFootnoteRegion { get; set; }

        public bool HasEndnoteRegion { get; set; }

        public bool HasEditedHeaderText { get; set; }

        public bool HasInsertedHeaderPageNumber { get; set; }

        public bool HasInsertedFootnoteText { get; set; }

        public bool HasInsertedEndnoteText { get; set; }

        public bool HasConfiguredFootnoteMarker { get; set; }

        public bool HasInsertedPageBreak { get; set; }

        public Phase16PageProbe[] Pages { get; set; } = [];
    }

    private sealed class Phase16PageProbe
    {
        public int Index { get; set; }

        public int HeaderFooterCount { get; set; }

        public int FieldCount { get; set; }

        public int NoteCount { get; set; }

        public string[] Scopes { get; set; } = [];

        public string[] NoteTypes { get; set; } = [];
    }

    private sealed class Phase16ModelProbe
    {
        public int RenderedPageCount { get; set; }

        public int FieldCount { get; set; }

        public int NoteCount { get; set; }

        public bool HasFirstScope { get; set; }

        public bool FirstPageUsesPrimaryFallback { get; set; }

        public bool HasEvenScope { get; set; }

        public bool HasOddScope { get; set; }

        public bool DifferentFirstPage { get; set; }

        public bool DifferentOddAndEvenPages { get; set; }

        public string NoteNumberingStyle { get; set; } = string.Empty;

        public int NoteNumberingStartAt { get; set; }

        public bool NoteNumberingRestartEachSection { get; set; }

        public bool HasFootnote { get; set; }

        public bool HasEndnote { get; set; }

        public bool HasEditedHeaderText { get; set; }

        public bool HasInsertedHeaderPageNumber { get; set; }

        public bool HasInsertedFootnoteText { get; set; }

        public bool HasInsertedEndnoteText { get; set; }

        public bool HasConfiguredFootnoteMarker { get; set; }

        public bool HasInsertedPageBreak { get; set; }
    }

    private sealed class DomRectProbe
    {
        public double X { get; set; }

        public double Y { get; set; }

        public double Width { get; set; }

        public double Height { get; set; }

        public double CenterX { get; set; }

        public double CenterY { get; set; }
    }

    private sealed class Phase16CommandProbe
    {
        public bool Changed { get; set; }

        public bool Handled { get; set; }

        public string NoteId { get; set; } = string.Empty;

        public string Debug { get; set; } = string.Empty;
    }
}
