using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Tempo.Blazor.E2E.CanvasEngine;

namespace Tempo.Blazor.E2E;

/// <summary>Phase 14 E2E coverage for canvas table layout, caret navigation, table commands, and save/reload.</summary>
[TestClass]
[TestCategory("DocumentEditor")]
[TestCategory("DocumentEditor:CanvasEngine")]
[DoNotParallelize]
public sealed class DocumentEditorCanvasTableE2ETests : WasmTestBase
{
    private const string Phase14DocumentId = "phase-14-canvas-tables";

    [TestMethod]
    public async Task Phase14_CanvasTables_RenderEditNavigateAndPersist()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(1440, 1000);
        await OpenPhase14DocumentAsync(page);

        var output = CreateOutputDirectory("desktop-1440x1000");
        var beforePath = Path.Combine(output, "00-phase14-table-before.png");
        var afterPath = Path.Combine(output, "01-phase14-table-after.png");

        await page.GetByTestId("document-editor-demo").ScreenshotAsync(new LocatorScreenshotOptions
        {
            Path = beforePath,
            Type = ScreenshotType.Png
        });

        var firstCell = await ReadCellRectAsync(page, "canvas-table-phase14-c-layout");
        var rangeTargetCell = await ReadCellRectAsync(page, "canvas-table-phase14-c-undo");
        await page.Mouse.MoveAsync((float)firstCell.CenterX, (float)firstCell.CenterY);
        await page.Mouse.DownAsync();
        await page.Mouse.MoveAsync((float)rangeTargetCell.CenterX, (float)rangeTargetCell.CenterY, new MouseMoveOptions { Steps = 6 });
        await page.Mouse.UpAsync();
        await WaitForPredicateAsync(page, """
            () => Number(document.querySelector('[data-testid="document-canvas-engine-root"]')?.getAttribute('data-canvas-selection-table-cell-range-count') || '0') >= 4
                && document.querySelectorAll('[data-canvas-table-cell-selection="true"]').length >= 4
            """, 10_000);

        var widthBeforeResize = firstCell.Width;
        await page.Mouse.MoveAsync((float)(firstCell.X + firstCell.Width), (float)firstCell.CenterY);
        await page.Mouse.DownAsync();
        await page.Mouse.MoveAsync((float)(firstCell.X + firstCell.Width + 36), (float)firstCell.CenterY, new MouseMoveOptions { Steps = 5 });
        await page.Mouse.UpAsync();
        await WaitForPredicateAsync(
            page,
            """
            ([cellId, widthBeforeResize]) => {
                const cell = document.querySelector(`[data-canvas-table-cell][data-cell-id="${cellId}"]`);
                const root = document.querySelector('[data-testid="document-canvas-engine-root"]');
                return cell?.getBoundingClientRect().width > Number(widthBeforeResize) + 20
                    && root?.getAttribute('data-canvas-table-resize-active') === 'false';
            }
            """,
            new object[] { "canvas-table-phase14-c-layout", widthBeforeResize },
            10_000);

        firstCell = await ReadCellRectAsync(page, "canvas-table-phase14-c-layout");
        await page.Mouse.ClickAsync((float)firstCell.CenterX, (float)firstCell.CenterY);
        await page.Keyboard.TypeAsync(" typed");
        await WaitForTypedCellAsync(page);

        await page.Keyboard.PressAsync("Tab");
        await WaitForPredicateAsync(page, """
            () => document.querySelector('[data-testid="document-canvas-engine-root"]')?.getAttribute('data-canvas-selection-cell-id') === 'canvas-table-phase14-c-state'
            """, 10_000);

        var activeCell = await ReadCellRectAsync(page, "canvas-table-phase14-c-state");
        await page.Mouse.ClickAsync((float)activeCell.CenterX, (float)activeCell.CenterY, new MouseClickOptions { Button = MouseButton.Right });
        await Assertions.Expect(page.GetByTestId("document-table-context-menu")).ToBeVisibleAsync(new() { Timeout = 10_000 });
        await page.GetByTestId("document-table-insert-row").ClickAsync();
        await WaitForPredicateAsync(page, """
            () => Number(document.querySelector('[data-testid="document-canvas-page"]')?.getAttribute('data-canvas-table-cell-count') || '0') >= 12
            """, 10_000);

        var rowCell = await ReadCellRectAsync(page, "canvas-table-phase14-c-state");
        await page.Mouse.ClickAsync((float)rowCell.CenterX, (float)rowCell.CenterY, new MouseClickOptions { Button = MouseButton.Right });
        await Assertions.Expect(page.GetByTestId("document-table-context-menu")).ToBeVisibleAsync(new() { Timeout = 10_000 });
        await page.GetByTestId("document-table-insert-column").ClickAsync();
        await WaitForPredicateAsync(page, """
            () => Number(document.querySelector('[data-testid="document-canvas-page"]')?.getAttribute('data-canvas-table-cell-count') || '0') >= 16
            """, 10_000);

        await page.GetByTestId("document-save").ClickAsync();
        await WaitForSaveBoundaryAsync(page);
        await NavigateWithinBlazorAsync(page, "/canvas-engine-host?documentId=phase-5-canvas-render");
        await WaitForPredicateAsync(page, """
            () => document.querySelector('[data-testid="document-canvas-page"]')?.getAttribute('data-canvas-model-document-id') === 'phase-5-canvas-render'
            """, 20_000);
        await NavigateWithinBlazorAsync(page, $"/canvas-engine-host?documentId={Phase14DocumentId}&showToolbar=true");
        await WaitForPhase14ReadyAsync(page);
        await WaitForPredicateAsync(page, """
            () => document.querySelector('[data-testid="document-canvas-a11y-mirror"]')?.textContent?.includes('Canvas grid typed') === true
                && Number(document.querySelector('[data-testid="document-canvas-page"]')?.getAttribute('data-canvas-table-cell-count') || '0') >= 16
            """, 10_000);

        await DocumentEditorCanvasVisualAssert.AssertNoTextOverlapAsync(page);
        await DocumentEditorCanvasVisualAssert.AssertNoUiOverlapAsync(page);
        await DocumentEditorCanvasVisualAssert.AssertCanvasNonBlankAsync(page.Locator("[data-canvas-layer='content']").First);
        await page.GetByTestId("document-editor-demo").ScreenshotAsync(new LocatorScreenshotOptions
        {
            Path = afterPath,
            Type = ScreenshotType.Png
        });

        var probe = await page.EvaluateAsync<Phase14Probe>(
            """
            () => {
                const root = document.querySelector('[data-testid="document-canvas-engine-root"]');
                const page = document.querySelector('[data-testid="document-canvas-page"]');
                return {
                    modelDocumentId: page?.getAttribute('data-canvas-model-document-id') || '',
                    tableCount: Number(page?.getAttribute('data-canvas-table-count') || '0'),
                    tableCellCount: Number(page?.getAttribute('data-canvas-table-cell-count') || '0'),
                    textRunCount: Number(page?.getAttribute('data-canvas-text-run-count') || '0'),
                    lastCommand: root?.getAttribute('data-canvas-command-last') || '',
                    inTable: root?.getAttribute('data-canvas-selection-in-table') || '',
                    mirrorContainsTypedCell: document.querySelector('[data-testid="document-canvas-a11y-mirror"]')?.textContent?.includes('Canvas grid typed') === true
                };
            }
            """);

        Assert.AreEqual(Phase14DocumentId, probe.ModelDocumentId);
        Assert.AreEqual(1, probe.TableCount);
        Assert.IsTrue(probe.TableCellCount >= 16);
        Assert.IsTrue(probe.TextRunCount >= 12);
        Assert.IsTrue(probe.MirrorContainsTypedCell);

        var manifestPath = Path.Combine(output, "manifest.json");
        await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(new
        {
            testName = nameof(Phase14_CanvasTables_RenderEditNavigateAndPersist),
            seedDocumentId = Phase14DocumentId,
            userActions = new[]
            {
                "Open the phase 14 canvas table seed document.",
                "Drag across cells to create a table cell range selection and drag the first column border to resize it.",
                "Click into a table cell, type text, and navigate to the next cell with Tab.",
                "Use the table context menu to insert a row and a column.",
                "Save and reload the document."
            },
            expectedVisibleChanges = "The canvas paints table cells and cell text, multi-cell range selection is highlighted, column resize changes rendered cell geometry, the caret stays inside table cells, row and column insertions increase the rendered cell count, and the typed cell text survives save/reload.",
            screenshotPaths = new[] { beforePath, afterPath },
            probe
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));

        TestContext.AddResultFile(beforePath);
        TestContext.AddResultFile(afterPath);
        TestContext.AddResultFile(manifestPath);
    }

    [TestMethod]
    public async Task Phase4_InsertTableFromToolbarGrid_RendersTypesAndPersists()
    {
        // Command-layer plan phase 4: the toolbar "Insert table" grid used to route an insertTable
        // command the engine never registered — the grid was a silent no-op (earlier table E2E worked
        // on seeds that already contained tables, masking it). This drives the REAL grid picker.
        await DocumentEditorE2EReset.ResetAsync();
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(1440, 1000);
        await GotoAppReadyAsync(page, $"{BaseUrl}/canvas-engine-host?documentId=phase-12-canvas-history-save&showToolbar=true", WaitUntilState.DOMContentLoaded);
        await WaitForPredicateAsync(page, """
            () => document.querySelector('[data-testid="document-canvas-engine-host"][data-canvas-engine-ready="true"]')
                && document.querySelector('[data-testid="document-ribbon-tab-insert"]')
                && Number(document.querySelector('[data-testid="document-canvas-page"]')?.getAttribute('data-canvas-model-table-block-count') || '0') >= 1
            """, 30_000);

        var output = CreateOutputDirectory("phase4-insert-table-grid");
        var afterInsertPath = Path.Combine(output, "00-inserted-table-typed.png");
        var reloadPath = Path.Combine(output, "01-inserted-table-after-reload.png");

        var baselineTableBlocks = await page.EvaluateAsync<int>(
            "() => Number(document.querySelector('[data-testid=\"document-canvas-page\"]')?.getAttribute('data-canvas-model-table-block-count') || '0')");

        // Insert ribbon → table grid → 3 columns × 2 rows (grid cell row index 1, column index 2).
        await page.GetByTestId("document-ribbon-tab-insert").ClickAsync();
        await page.GetByTestId("document-toolbar-table").ClickAsync();
        await page.GetByTestId("document-table-grid-cell-1-2").WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10_000 });
        await page.GetByTestId("document-table-grid-cell-1-2").ClickAsync();

        await WaitForPredicateAsync(page, """
            baseline => Number(document.querySelector('[data-testid="document-canvas-page"]')?.getAttribute('data-canvas-model-table-block-count') || '0') === baseline + 1
            """, baselineTableBlocks, 10_000);

        // The engine renders the new table (canvas cell selectors) and moves the caret into its
        // first cell, so typing lands there without any extra click.
        await WaitForPredicateAsync(page, """
            () => document.querySelector('[data-canvas-table-cell][data-cell-id="inserted-table-r1c1"]')
                && document.querySelector('[data-testid="document-canvas-engine-root"]')?.getAttribute('data-canvas-selection-cell-id') === 'inserted-table-r1c1'
            """, 10_000);

        // Edge case FIRST (before typing adds its own transactions): undo removes the whole inserted
        // table atomically, redo restores it including the caret in the first cell.
        await page.GetByTestId("document-undo").ClickAsync();
        await WaitForPredicateAsync(page, """
            baseline => Number(document.querySelector('[data-testid="document-canvas-page"]')?.getAttribute('data-canvas-model-table-block-count') || '0') === baseline
            """, baselineTableBlocks, 10_000);
        await page.GetByTestId("document-redo").ClickAsync();
        await WaitForPredicateAsync(page, """
            baseline => Number(document.querySelector('[data-testid="document-canvas-page"]')?.getAttribute('data-canvas-model-table-block-count') || '0') === baseline + 1
                && document.querySelector('[data-canvas-table-cell][data-cell-id="inserted-table-r1c1"]')
            """, baselineTableBlocks, 10_000);

        // Deterministic caret: click the first inserted cell and verify the engine resolved it
        // (post-layout click race — see ClickCanvasBlockAsync learning) before typing.
        for (var attempt = 0; ; attempt++)
        {
            var cell = await ReadCellRectAsync(page, "inserted-table-r1c1");
            await page.Mouse.ClickAsync((float)cell.CenterX, (float)cell.CenterY);
            var selected = await page.EvaluateAsync<string>(
                "() => document.querySelector('[data-testid=\"document-canvas-engine-root\"]')?.getAttribute('data-canvas-selection-cell-id') || ''");
            if (selected == "inserted-table-r1c1")
            {
                break;
            }

            if (attempt >= 9)
            {
                Assert.Fail($"Click kept resolving to cell '{selected}' instead of inserted-table-r1c1.");
            }

            await page.WaitForTimeoutAsync(250);
        }

        await page.EvaluateAsync("() => document.querySelector('[data-testid=\"document-canvas-hidden-input\"]')?.focus()");
        await page.Keyboard.TypeAsync("Grid inserted cell");
        await WaitForPredicateAsync(page, "() => document.querySelector('[data-testid=\"document-canvas-a11y-mirror\"]')?.textContent?.includes('Grid inserted cell') === true", 10_000);

        await DocumentEditorCanvasVisualAssert.AssertNoTextOverlapAsync(page);
        await page.GetByTestId("document-editor-demo").ScreenshotAsync(new LocatorScreenshotOptions { Path = afterInsertPath, Type = ScreenshotType.Png });

        await page.GetByTestId("document-save").ClickAsync();
        await WaitForSaveBoundaryAsync(page);
        await NavigateWithinBlazorAsync(page, "/canvas-engine-host?documentId=phase-5-canvas-render");
        await WaitForPredicateAsync(page, """
            () => document.querySelector('[data-testid="document-canvas-page"]')?.getAttribute('data-canvas-model-document-id') === 'phase-5-canvas-render'
            """, 20_000);
        await NavigateWithinBlazorAsync(page, "/canvas-engine-host?documentId=phase-12-canvas-history-save&showToolbar=true");
        await WaitForPredicateAsync(page, """
            baseline => document.querySelector('[data-testid="document-canvas-engine-host"][data-canvas-engine-ready="true"]')
                && Number(document.querySelector('[data-testid="document-canvas-page"]')?.getAttribute('data-canvas-model-table-block-count') || '0') === baseline + 1
            """, baselineTableBlocks, 30_000);

        // Both the table structure and the typed cell text must survive the save/reload boundary.
        await WaitForPredicateAsync(page, """
            () => document.querySelector('[data-canvas-table-cell][data-cell-id="inserted-table-r1c1"]')
                && document.querySelector('[data-testid="document-canvas-a11y-mirror"]')?.textContent?.includes('Grid inserted cell') === true
            """, 10_000);

        await DocumentEditorCanvasVisualAssert.AssertNoTextOverlapAsync(page);
        await DocumentEditorCanvasVisualAssert.AssertCanvasNonBlankAsync(page.Locator("[data-canvas-layer='content']").First);
        await page.GetByTestId("document-editor-demo").ScreenshotAsync(new LocatorScreenshotOptions { Path = reloadPath, Type = ScreenshotType.Png });

        var manifestPath = Path.Combine(output, "manifest.json");
        await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(new
        {
            testName = nameof(Phase4_InsertTableFromToolbarGrid_RendersTypesAndPersists),
            seedDocumentId = "phase-12-canvas-history-save",
            userActions = new[]
            {
                "Open the phase 12 canvas document through the production TmDocumentEditor route.",
                "Insert ribbon tab, open the table grid picker, and pick 3×2.",
                "Undo (whole table disappears atomically) and redo (table returns).",
                "Click into the first cell of the inserted table and type text.",
                "Save, navigate away, navigate back, and verify the inserted table and typed text persisted."
            },
            expectedVisibleChanges = "A new 2-row × 3-column table renders at the end of the body, typing lands in its first cell, undo/redo treat the insert as one transaction, and the inserted table survives save/reload.",
            screenshotPaths = new[] { afterInsertPath, reloadPath },
            baselineTableBlocks
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));

        TestContext.AddResultFile(afterInsertPath);
        TestContext.AddResultFile(reloadPath);
        TestContext.AddResultFile(manifestPath);
    }

    [TestMethod]
    public async Task Phase5_TableContextMenu_DeleteAndHeaderRowToggle_WorkAndPersist()
    {
        // Command-layer plan phase 5: the table context menu's Delete table and Header row entries
        // routed deleteTable / toggleTableHeaderRow — ids the engine never registered (silent
        // no-ops). Runs on a FRESHLY INSERTED table (plain cells without explicit backgrounds) so
        // the header-row styling change is pixel-visible; seed tables carry explicit cell colors
        // which by design override the header style.
        await DocumentEditorE2EReset.ResetAsync();
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(1440, 1000);
        await GotoAppReadyAsync(page, $"{BaseUrl}/canvas-engine-host?documentId=phase-12-canvas-history-save&showToolbar=true", WaitUntilState.DOMContentLoaded);
        await WaitForPredicateAsync(page, """
            () => document.querySelector('[data-testid="document-canvas-engine-host"][data-canvas-engine-ready="true"]')
                && document.querySelector('[data-testid="document-ribbon-tab-insert"]')
            """, 30_000);

        var output = CreateOutputDirectory("phase5-delete-header-toggle");
        var headerOnPath = Path.Combine(output, "00-header-row-on.png");
        var afterDeleteUndoPath = Path.Combine(output, "01-after-delete-undo.png");
        var reloadPath = Path.Combine(output, "02-after-reload.png");

        var baselineTableBlocks = await page.EvaluateAsync<int>(
            "() => Number(document.querySelector('[data-testid=\"document-canvas-page\"]')?.getAttribute('data-canvas-model-table-block-count') || '0')");

        await page.GetByTestId("document-ribbon-tab-insert").ClickAsync();
        await page.GetByTestId("document-toolbar-table").ClickAsync();
        await page.GetByTestId("document-table-grid-cell-1-2").WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10_000 });
        await page.GetByTestId("document-table-grid-cell-1-2").ClickAsync();
        await WaitForPredicateAsync(page, "() => document.querySelector('[data-canvas-table-cell][data-cell-id=\"inserted-table-r1c1\"]')", 10_000);

        var backgroundBefore = await SampleCellBackgroundAsync(page, "inserted-table-r1c2");
        Assert.IsTrue(backgroundBefore[0] > 245 && backgroundBefore[2] > 245,
            $"A plain inserted cell must start near-white. Sampled: {string.Join(",", backgroundBefore)}");

        // Context menu → Header row: layout.headerRow flips and row 0 repaints with the header tint.
        await RightClickCellAsync(page, "inserted-table-r1c1");
        await page.GetByTestId("document-table-toggle-header").ClickAsync();
        await WaitForPredicateAsync(page, """
            async () => {
                const host = document.querySelector('[data-testid="document-canvas-engine-host"]');
                const handle = host?.getAttribute('data-canvas-engine-handle') || '';
                const module = await import('/_content/Tempo.Blazor.DocumentEditor/js/document-editor-canvas/interop.mjs');
                const model = JSON.parse(module.getModelJson(handle) || '{}');
                const table = (model.body?.blocks || []).find(block => String(block?.id || '') === 'inserted-table');
                return table?.content?.table?.layout?.headerRow === true;
            }
            """, 10_000);
        await WaitForPredicateAsync(page, """
            () => {
                const cell = document.querySelector('[data-canvas-table-cell][data-cell-id="inserted-table-r1c2"]');
                return !!cell;
            }
            """, 10_000);

        var backgroundAfter = await SampleCellBackgroundAsync(page, "inserted-table-r1c2");
        Assert.IsTrue(backgroundBefore[0] - backgroundAfter[0] > 10,
            $"Enabling the header row must visibly tint row 0. Before: {string.Join(",", backgroundBefore)}, after: {string.Join(",", backgroundAfter)}");
        await page.GetByTestId("document-editor-demo").ScreenshotAsync(new LocatorScreenshotOptions { Path = headerOnPath, Type = ScreenshotType.Png });

        // Context menu → Delete table: the whole table disappears; undo brings it back with the flag.
        await RightClickCellAsync(page, "inserted-table-r1c1");
        await page.GetByTestId("document-table-delete-table").ClickAsync();
        await WaitForPredicateAsync(page, """
            baseline => Number(document.querySelector('[data-testid="document-canvas-page"]')?.getAttribute('data-canvas-model-table-block-count') || '0') === baseline
                && !document.querySelector('[data-canvas-table-cell][data-cell-id="inserted-table-r1c1"]')
            """, baselineTableBlocks, 10_000);

        await page.GetByTestId("document-undo").ClickAsync();
        await WaitForPredicateAsync(page, """
            baseline => Number(document.querySelector('[data-testid="document-canvas-page"]')?.getAttribute('data-canvas-model-table-block-count') || '0') === baseline + 1
                && document.querySelector('[data-canvas-table-cell][data-cell-id="inserted-table-r1c1"]')
            """, baselineTableBlocks, 10_000);
        await page.GetByTestId("document-editor-demo").ScreenshotAsync(new LocatorScreenshotOptions { Path = afterDeleteUndoPath, Type = ScreenshotType.Png });

        await page.GetByTestId("document-save").ClickAsync();
        await WaitForSaveBoundaryAsync(page);
        await NavigateWithinBlazorAsync(page, "/canvas-engine-host?documentId=phase-5-canvas-render");
        await WaitForPredicateAsync(page, """
            () => document.querySelector('[data-testid="document-canvas-page"]')?.getAttribute('data-canvas-model-document-id') === 'phase-5-canvas-render'
            """, 20_000);
        await NavigateWithinBlazorAsync(page, "/canvas-engine-host?documentId=phase-12-canvas-history-save&showToolbar=true");
        await WaitForPredicateAsync(page, """
            baseline => document.querySelector('[data-testid="document-canvas-engine-host"][data-canvas-engine-ready="true"]')
                && Number(document.querySelector('[data-testid="document-canvas-page"]')?.getAttribute('data-canvas-model-table-block-count') || '0') === baseline + 1
                && document.querySelector('[data-canvas-table-cell][data-cell-id="inserted-table-r1c2"]')
            """, baselineTableBlocks, 30_000);

        // The headerRow flag survives the canvas↔C# round-trip (TableLayoutContent.HeaderRow) and
        // the reloaded row 0 still paints with the header tint.
        var backgroundReloaded = await SampleCellBackgroundAsync(page, "inserted-table-r1c2");
        Assert.IsTrue(backgroundBefore[0] - backgroundReloaded[0] > 10,
            $"The header tint must survive save/reload. Before-insert: {string.Join(",", backgroundBefore)}, reloaded: {string.Join(",", backgroundReloaded)}");

        await DocumentEditorCanvasVisualAssert.AssertNoTextOverlapAsync(page);
        await page.GetByTestId("document-editor-demo").ScreenshotAsync(new LocatorScreenshotOptions { Path = reloadPath, Type = ScreenshotType.Png });

        var manifestPath = Path.Combine(output, "manifest.json");
        await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(new
        {
            testName = nameof(Phase5_TableContextMenu_DeleteAndHeaderRowToggle_WorkAndPersist),
            seedDocumentId = "phase-12-canvas-history-save",
            userActions = new[]
            {
                "Insert a fresh 3×2 table from the toolbar grid (plain cells).",
                "Right-click a cell and toggle Header row — row 0 repaints with the header tint (pixel-sampled).",
                "Right-click and Delete table — the whole table disappears; undo restores it including the header flag.",
                "Save, navigate away and back — the table and its header styling persist."
            },
            expectedVisibleChanges = "Header row toggle visibly tints the first row of a plain table; Delete table removes the whole table and undo restores it; both survive save/reload.",
            screenshotPaths = new[] { headerOnPath, afterDeleteUndoPath, reloadPath },
            backgroundBefore,
            backgroundAfter,
            backgroundReloaded
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));

        TestContext.AddResultFile(headerOnPath);
        TestContext.AddResultFile(afterDeleteUndoPath);
        TestContext.AddResultFile(reloadPath);
        TestContext.AddResultFile(manifestPath);
    }

    [TestMethod]
    public async Task Phase7_TablePropertiesPanel_ApplyChangesRenderAndPersist()
    {
        // Command-layer plan phase 7: the properties side panel routed setTableProperties /
        // setCellProperties — composite commands the engine never registered, so Apply was a
        // silent no-op.
        await DocumentEditorE2EReset.ResetAsync();
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(1440, 1000);
        await GotoAppReadyAsync(page, $"{BaseUrl}/canvas-engine-host?documentId=phase-12-canvas-history-save&showToolbar=true", WaitUntilState.DOMContentLoaded);
        await WaitForPredicateAsync(page, """
            () => document.querySelector('[data-testid="document-canvas-engine-host"][data-canvas-engine-ready="true"]')
                && document.querySelector('[data-canvas-table-cell][data-cell-id="canvas-history-table-h-category"]')
            """, 30_000);

        var output = CreateOutputDirectory("phase7-table-properties");
        var appliedPath = Path.Combine(output, "00-properties-applied.png");
        var reloadPath = Path.Combine(output, "01-after-reload.png");

        // The phase-12 seed table is center-aligned; remember the first header cell geometry.
        var cellBefore = await ReadCellRectAsync(page, "canvas-history-table-h-category");

        // The properties panel resolves the active cell from the SELECTION — click into the cell
        // first (verified), then open the context menu.
        for (var attempt = 0; ; attempt++)
        {
            var target = await ReadCellRectAsync(page, "canvas-history-table-h-category");
            await page.Mouse.ClickAsync((float)target.CenterX, (float)target.CenterY);
            var selected = await page.EvaluateAsync<string>(
                "() => document.querySelector('[data-testid=\"document-canvas-engine-root\"]')?.getAttribute('data-canvas-selection-cell-id') || ''");
            if (selected == "canvas-history-table-h-category")
            {
                break;
            }

            if (attempt >= 9)
            {
                Assert.Fail($"Click kept resolving to cell '{selected}'.");
            }

            await page.WaitForTimeoutAsync(250);
        }

        await RightClickCellAsync(page, "canvas-history-table-h-category");
        await page.GetByTestId("document-table-table-properties").ClickAsync();
        await Assertions.Expect(page.GetByTestId("document-table-properties-panel")).ToBeVisibleAsync(new() { Timeout = 10_000 });

        // Alignment Center → Left: the table visibly shifts left.
        await page.GetByTestId("document-table-properties-align-left").ClickAsync();
        await WaitForPredicateAsync(page, """
            beforeX => {
                const cell = document.querySelector('[data-canvas-table-cell][data-cell-id="canvas-history-table-h-category"]');
                return cell && cell.getBoundingClientRect().x < Number(beforeX) - 20;
            }
            """, cellBefore.X, 10_000);

        // Background: applies to the table layout (seed cells carry explicit colors which win by
        // design — assert the model flag; a plain-cell visual check is covered by the phase-5 test).
        await page.GetByTestId("document-table-properties-background").FillAsync("#ffedd5");
        await page.GetByTestId("document-table-properties-background").DispatchEventAsync("change");
        await WaitForPredicateAsync(page, """
            async () => {
                const host = document.querySelector('[data-testid="document-canvas-engine-host"]');
                const module = await import('/_content/Tempo.Blazor.DocumentEditor/js/document-editor-canvas/interop.mjs');
                const model = JSON.parse(module.getModelJson(host.getAttribute('data-canvas-engine-handle')) || '{}');
                const table = (model.body?.blocks || []).find(block => String(block?.id || '') === 'canvas-history-table');
                const layout = table?.content?.table?.layout || {};
                return String(layout.alignment || '').toLowerCase() === 'left'
                    && String(layout.backgroundColor || '').toLowerCase() === '#ffedd5';
            }
            """, 10_000);

        await DocumentEditorCanvasVisualAssert.AssertNoTextOverlapAsync(page);
        await page.GetByTestId("document-editor-demo").ScreenshotAsync(new LocatorScreenshotOptions { Path = appliedPath, Type = ScreenshotType.Png });

        await page.GetByTestId("document-save").ClickAsync();
        await WaitForSaveBoundaryAsync(page);
        await NavigateWithinBlazorAsync(page, "/canvas-engine-host?documentId=phase-5-canvas-render");
        await WaitForPredicateAsync(page, """
            () => document.querySelector('[data-testid="document-canvas-page"]')?.getAttribute('data-canvas-model-document-id') === 'phase-5-canvas-render'
            """, 20_000);
        await NavigateWithinBlazorAsync(page, "/canvas-engine-host?documentId=phase-12-canvas-history-save&showToolbar=true");
        await WaitForPredicateAsync(page, """
            beforeX => document.querySelector('[data-testid="document-canvas-engine-host"][data-canvas-engine-ready="true"]')
                && (() => {
                    const cell = document.querySelector('[data-canvas-table-cell][data-cell-id="canvas-history-table-h-category"]');
                    return cell && cell.getBoundingClientRect().x < Number(beforeX) - 20;
                })()
            """, cellBefore.X, 30_000);
        await WaitForPredicateAsync(page, """
            async () => {
                const host = document.querySelector('[data-testid="document-canvas-engine-host"]');
                const module = await import('/_content/Tempo.Blazor.DocumentEditor/js/document-editor-canvas/interop.mjs');
                const model = JSON.parse(module.getModelJson(host.getAttribute('data-canvas-engine-handle')) || '{}');
                const layout = (model.body?.blocks || []).find(block => String(block?.id || '') === 'canvas-history-table')?.content?.table?.layout || {};
                return String(layout.alignment || '').toLowerCase() === 'left'
                    && String(layout.backgroundColor || '').toLowerCase() === '#ffedd5';
            }
            """, 10_000);
        await page.GetByTestId("document-editor-demo").ScreenshotAsync(new LocatorScreenshotOptions { Path = reloadPath, Type = ScreenshotType.Png });

        TestContext.AddResultFile(appliedPath);
        TestContext.AddResultFile(reloadPath);
    }

    private static async Task RightClickCellAsync(IPage page, string cellId)
    {
        var cell = await ReadCellRectAsync(page, cellId);
        await page.Mouse.ClickAsync((float)cell.CenterX, (float)cell.CenterY, new MouseClickOptions { Button = MouseButton.Right });
        await Assertions.Expect(page.GetByTestId("document-table-context-menu")).ToBeVisibleAsync(new() { Timeout = 10_000 });
    }

    /// <summary>Samples the painted background [r,g,b] near the top edge of an (empty) table cell
    /// from the content-layer canvas of the page that contains it.</summary>
    private static Task<int[]> SampleCellBackgroundAsync(IPage page, string cellId)
        => page.EvaluateAsync<int[]>(
            """
            cellId => {
                const cell = document.querySelector(`[data-canvas-table-cell][data-cell-id="${cellId}"]`);
                if (!cell) throw new Error(`cell ${cellId} not found`);
                const cellRect = cell.getBoundingClientRect();
                const centerX = cellRect.left + cellRect.width / 2;
                const probeY = cellRect.top + Math.min(6, cellRect.height / 4);
                const canvas = Array.from(document.querySelectorAll('[data-canvas-layer="content"]')).find(candidate => {
                    const rect = candidate.getBoundingClientRect();
                    return centerX >= rect.left && centerX <= rect.right && probeY >= rect.top && probeY <= rect.bottom;
                });
                if (!canvas) throw new Error(`no content canvas under cell ${cellId}`);
                const canvasRect = canvas.getBoundingClientRect();
                const x = Math.round((centerX - canvasRect.left) * (canvas.width / canvasRect.width));
                const y = Math.round((probeY - canvasRect.top) * (canvas.height / canvasRect.height));
                const data = canvas.getContext('2d').getImageData(x, y, 1, 1).data;
                return [data[0], data[1], data[2]];
            }
            """,
            cellId);

    private async Task OpenPhase14DocumentAsync(IPage page)
    {
        await GotoAppReadyAsync(page, $"{BaseUrl}/canvas-engine-host?documentId={Phase14DocumentId}&showToolbar=true");
        await WaitForPhase14ReadyAsync(page);
    }

    private static Task WaitForPhase14ReadyAsync(IPage page)
        => WaitForPredicateAsync(page, """
            () => document.querySelector('[data-testid="document-canvas-engine-host"]')?.getAttribute('data-canvas-engine-ready') === 'true'
                && Number(document.querySelector('[data-testid="document-canvas-page"]')?.getAttribute('data-canvas-table-cell-count') || '0') >= 9
                && document.querySelector('[data-canvas-table-cell][data-cell-id="canvas-table-phase14-c-layout"]')
            """, 30_000);

    private static Task<CellRect> ReadCellRectAsync(IPage page, string cellId)
        => page.EvaluateAsync<CellRect>(
            """
            cellId => {
                const node = document.querySelector(`[data-canvas-table-cell][data-cell-id="${cellId}"]`);
                if (!node) {
                    throw new Error(`Canvas table cell metadata not found: ${cellId}`);
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
            cellId);

    private static async Task WaitForTypedCellAsync(IPage page)
    {
        try
        {
            await WaitForPredicateAsync(page, """
                () => document.querySelector('[data-testid="document-canvas-a11y-mirror"]')?.textContent?.includes('Canvas grid typed') === true
                    && document.querySelector('[data-testid="document-canvas-engine-root"]')?.getAttribute('data-canvas-selection-in-table') === 'true'
                """, 10_000);
        }
        catch (TimeoutException ex)
        {
            var state = await page.EvaluateAsync<Phase14DebugState>(
                """
                () => {
                    const root = document.querySelector('[data-testid="document-canvas-engine-root"]');
                    const mirror = document.querySelector('[data-testid="document-canvas-a11y-mirror"]');
                    const active = document.activeElement;
                    return {
                        mirrorText: mirror?.textContent || '',
                        selectionInTable: root?.getAttribute('data-canvas-selection-in-table') || '',
                        selectionCellId: root?.getAttribute('data-canvas-selection-cell-id') || '',
                        selectionFocusBlockId: root?.getAttribute('data-canvas-selection-focus-block-id') || '',
                        selectionFocusOffset: root?.getAttribute('data-canvas-selection-focus-offset') || '',
                        commandLast: root?.getAttribute('data-canvas-command-last') || '',
                        activeTagName: active?.tagName || '',
                        activeTestId: active?.getAttribute?.('data-testid') || ''
                    };
                }
                """);

            Assert.Fail($"Timed out typing into the first canvas table cell. State: {JsonSerializer.Serialize(state, new JsonSerializerOptions(JsonSerializerDefaults.Web))}. {ex.Message}");
        }
    }

    private static async Task WaitForSaveBoundaryAsync(IPage page)
    {
        try
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
        catch (TimeoutException ex)
        {
            var state = await page.EvaluateAsync<Phase14SaveDebugState>(
                """
                () => ({
                    saveMessage: document.querySelector('[data-testid="document-save-message"]')?.textContent || '',
                    lastSaved: document.querySelector('[data-testid="document-last-saved"]')?.textContent || '',
                    pending: document.querySelector('[data-testid="document-pending-status"]')?.textContent || '',
                    dirty: document.querySelector('[data-testid="document-dirty-status"]')?.textContent || '',
                    saveDisabled: document.querySelector('[data-testid="document-save"]')?.hasAttribute('disabled') === true,
                    statusBar: document.querySelector('[data-testid="document-status-bar"]')?.textContent || '',
                    bodyHasSaved: /Saved|Autosaved/i.test(document.body.textContent || '')
                })
                """);

            Assert.Fail($"Timed out waiting for the canvas table save boundary. State: {JsonSerializer.Serialize(state, new JsonSerializerOptions(JsonSerializerDefaults.Web))}. {ex.Message}");
        }
    }

    private static Task NavigateWithinBlazorAsync(IPage page, string url)
        => page.EvaluateAsync(
            """
            url => window.Blazor?.navigateTo
                ? window.Blazor.navigateTo(url)
                : window.history.pushState({}, '', url)
            """,
            url);

    private sealed class CellRect
    {
        public double X { get; set; }

        public double Y { get; set; }

        public double Width { get; set; }

        public double Height { get; set; }

        public double CenterX { get; set; }

        public double CenterY { get; set; }
    }

    private sealed class Phase14Probe
    {
        public string ModelDocumentId { get; set; } = string.Empty;

        public int TableCount { get; set; }

        public int TableCellCount { get; set; }

        public int TextRunCount { get; set; }

        public string LastCommand { get; set; } = string.Empty;

        public string InTable { get; set; } = string.Empty;

        public bool MirrorContainsTypedCell { get; set; }
    }

    private sealed class Phase14DebugState
    {
        public string MirrorText { get; set; } = string.Empty;

        public string SelectionInTable { get; set; } = string.Empty;

        public string SelectionCellId { get; set; } = string.Empty;

        public string SelectionFocusBlockId { get; set; } = string.Empty;

        public string SelectionFocusOffset { get; set; } = string.Empty;

        public string CommandLast { get; set; } = string.Empty;

        public string ActiveTagName { get; set; } = string.Empty;

        public string ActiveTestId { get; set; } = string.Empty;
    }

    private sealed class Phase14SaveDebugState
    {
        public string SaveMessage { get; set; } = string.Empty;

        public string LastSaved { get; set; } = string.Empty;

        public string Pending { get; set; } = string.Empty;

        public string Dirty { get; set; } = string.Empty;

        public bool SaveDisabled { get; set; }

        public string StatusBar { get; set; } = string.Empty;

        public bool BodyHasSaved { get; set; }
    }

    private static string CreateOutputDirectory(string viewport)
    {
        var output = Path.Combine(
            FindRepositoryRoot().FullName,
            "tests",
            "Tempo.Blazor.E2E",
            "TestResults",
            "document-editor-canvas",
            "phase14-tables",
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

        throw new DirectoryNotFoundException("Could not locate TempoBlazor.slnx from the E2E test output directory.");
    }
}
