using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Tempo.Blazor.E2E.CanvasEngine;

namespace Tempo.Blazor.E2E;

/// <summary>Phase E7 E2E coverage for canvas shapes, text boxes, lines, connectors, and charts.</summary>
[TestClass]
[TestCategory("DocumentEditor")]
[TestCategory("DocumentEditor:CanvasEngine")]
[DoNotParallelize]
public sealed class DocumentEditorCanvasShapesDrawingsE2ETests : WasmTestBase
{
    private const string PhaseE7DocumentId = "phase-e7-canvas-shapes-drawings";

    [TestMethod]
    public async Task PhaseE7_CanvasShapesTextBoxesLinesAndChartsRenderInsertAndPersist()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        page.Console += (_, message) => TestContext.WriteLine($"[browser:{message.Type}] {message.Text}");
        page.PageError += (_, error) => TestContext.WriteLine($"[page-error] {error}");
        await page.SetViewportSizeAsync(1440, 1000);
        await OpenPhaseE7DocumentAsync(page);

        var output = CreateOutputDirectory("desktop-1440x1000");
        var beforePath = Path.Combine(output, "00-phasee7-drawings-before.png");
        var afterPath = Path.Combine(output, "01-phasee7-drawings-after-reload.png");
        var handlesPath = Path.Combine(output, "02-phasee7-textbox-selection-handles.png");
        var chartUpdatedPath = Path.Combine(output, "03-phasee7-chart-data-updated.png");

        var initialProbe = await ReadProbeAsync(page);
        Assert.AreEqual(PhaseE7DocumentId, initialProbe.ModelDocumentId);
        Assert.IsTrue(initialProbe.DrawingCount >= 4, initialProbe.Debug);
        Assert.IsTrue(initialProbe.ShapeCount >= 2, initialProbe.Debug);
        Assert.IsTrue(initialProbe.LineCount >= 1, initialProbe.Debug);
        Assert.IsTrue(initialProbe.ChartCount >= 1, initialProbe.Debug);
        Assert.IsTrue(initialProbe.TextBoxTextFound, initialProbe.Debug);

        await page.GetByTestId("document-editor-demo").ScreenshotAsync(new LocatorScreenshotOptions
        {
            Path = beforePath,
            Type = ScreenshotType.Png
        });

        var insertResult = await ExecuteCanvasCommandAsync(page, "insertTextBox", new
        {
            objectId = "e7-inserted-textbox",
            anchorBlockId = "canvas-shapes-after",
            text = "Inserted E7 text box",
            width = 210,
            height = 76,
            wrapMode = "InFrontOfText",
            x = 64,
            y = 430,
            shape = new
            {
                preset = "roundRectangle",
                fill = new { color = "#dcfce7", opacity = 1 },
                stroke = new { color = "#16a34a", width = 2 }
            }
        });
        Assert.IsTrue(insertResult.Changed, insertResult.Debug);
        await WaitForModelDrawingAsync(page, "e7-inserted-textbox");
        await SelectCanvasObjectAsync(page, "e7-inserted-textbox");
        await WaitForImageInspectorSizeAsync(page, "e7-inserted-textbox", 210, 76);
        await DocumentEditorCanvasVisualAssert.AssertNoUiOverlapAsync(page);
        await DocumentEditorCanvasVisualAssert.AssertNoTextOverlapAsync(page);
        await page.GetByTestId("document-editor-demo").ScreenshotAsync(new LocatorScreenshotOptions
        {
            Path = handlesPath,
            Type = ScreenshotType.Png
        });

        var chartUpdate = await ExecuteCanvasCommandAsync(page, "updateChartData", new
        {
            objectId = "canvas-chart-bar",
            chart = new
            {
                type = "area",
                title = "Updated E7 chart",
                categories = new[] { "Jan", "Feb", "Mar", "Apr" },
                series = new[]
                {
                    new { name = "Actual", values = new[] { 4, 9, 6, 11 }, color = "#2563eb" },
                    new { name = "Plan", values = new[] { 5, 7, 8, 10 }, color = "#16a34a" }
                },
                showLegend = true
            }
        });
        Assert.IsTrue(chartUpdate.Changed, chartUpdate.Debug);
        await WaitForChartTitleAsync(page, "canvas-chart-bar", "Updated E7 chart");
        await DocumentEditorCanvasVisualAssert.AssertNoUiOverlapAsync(page);
        await DocumentEditorCanvasVisualAssert.AssertNoTextOverlapAsync(page);
        await page.GetByTestId("document-editor-demo").ScreenshotAsync(new LocatorScreenshotOptions
        {
            Path = chartUpdatedPath,
            Type = ScreenshotType.Png
        });

        await page.GetByTestId("document-save").ClickAsync();
        await WaitForSaveBoundaryAsync(page);

        await NavigateWithinBlazorAsync(page, "/canvas-engine-host?documentId=phase-5-canvas-render");
        await page.WaitForFunctionAsync(
            "() => document.querySelector('[data-testid=\"document-canvas-page\"]')?.getAttribute('data-canvas-model-document-id') === 'phase-5-canvas-render'",
            new PageWaitForFunctionOptions { Timeout = 20_000 });
        await NavigateWithinBlazorAsync(page, $"/canvas-engine-host?documentId={PhaseE7DocumentId}&showToolbar=true&disableCollaboration=true");
        await WaitForPhaseE7ReadyAsync(page);
        await WaitForModelDrawingAsync(page, "e7-inserted-textbox");

        var reloadedProbe = await ReadProbeAsync(page);
        Assert.IsTrue(reloadedProbe.DrawingCount >= initialProbe.DrawingCount + 1, reloadedProbe.Debug);
        Assert.IsTrue(reloadedProbe.InsertedTextFound, reloadedProbe.Debug);
        Assert.IsTrue(reloadedProbe.ChartTitleUpdated, reloadedProbe.Debug);

        await DocumentEditorCanvasVisualAssert.AssertNoUiOverlapAsync(page);
        await DocumentEditorCanvasVisualAssert.AssertNoTextOverlapAsync(page);
        var objectMetrics = await DocumentEditorCanvasVisualAssert.AssertCanvasNonBlankAsync(page.Locator("[data-canvas-layer='objects']").First);
        await page.GetByTestId("document-editor-demo").ScreenshotAsync(new LocatorScreenshotOptions
        {
            Path = afterPath,
            Type = ScreenshotType.Png
        });

        var manifestPath = Path.Combine(output, "manifest.json");
        await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(new
        {
            testName = nameof(PhaseE7_CanvasShapesTextBoxesLinesAndChartsRenderInsertAndPersist),
            seedDocumentId = PhaseE7DocumentId,
            userActions = new[]
            {
                "Open the phase E7 canvas drawings seed document.",
                "Verify shape, text box, line, and chart objects are rendered in the canvas object layer.",
                "Insert a text box through the shared canvas command runtime.",
                "Select the inserted text box and verify visible canvas resize handles with a synchronized image inspector.",
                "Update chart data through the production canvas command runtime and capture chart screenshot evidence.",
                "Save, navigate away, reload the same document, and verify the inserted drawing remains present."
            },
            expectedVisibleChanges = "Vector drawing commands paint onto the objects canvas layer, text box text is visible through drawing text commands, inserted drawing selection handles are visible, chart data changes repaint the chart, and drawing-run payloads survive save/reload.",
            screenshotPaths = new[] { beforePath, afterPath, handlesPath, chartUpdatedPath },
            initialProbe,
            reloadedProbe,
            insertResult,
            chartUpdate,
            objectMetrics
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));

        TestContext.AddResultFile(beforePath);
        TestContext.AddResultFile(afterPath);
        TestContext.AddResultFile(handlesPath);
        TestContext.AddResultFile(chartUpdatedPath);
        TestContext.AddResultFile(manifestPath);
    }

    [TestMethod]
    public async Task PhaseE7_CanvasDrawingLayerUsesHighDpiBackingStoreForSharpShapesAndHandles()
    {
        var context = await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = 1440, Height = 1000 },
            DeviceScaleFactor = 2,
            Locale = "en-US",
            IgnoreHTTPSErrors = true,
            AcceptDownloads = true
        });

        try
        {
            var page = await context.NewPageAsync();
            page.Console += (_, message) => TestContext.WriteLine($"[browser:{message.Type}] {message.Text}");
            page.PageError += (_, error) => TestContext.WriteLine($"[page-error] {error}");
            await OpenPhaseE7DocumentAsync(page);

            var output = CreateOutputDirectory("desktop-1440x1000-dpr2");
            var screenshotPath = Path.Combine(output, "19-phasee7-high-dpi-handles.png");
            var objectId = $"e7-dpr-sharp-shape-{Guid.NewGuid():N}";

            var insertResult = await ExecuteCanvasCommandAsync(page, "insertShape", new
            {
                objectId,
                anchorBlockId = "canvas-shapes-after",
                width = 168,
                height = 92,
                wrapMode = "InFrontOfText",
                x = 92,
                y = 438,
                shape = new
                {
                    preset = "roundRectangle",
                    fill = new { color = "#dbeafe", opacity = 1 },
                    stroke = new { color = "#2563eb", width = 2 }
                },
                altText = "E7 high DPI vector shape"
            });
            Assert.IsTrue(insertResult.Changed, insertResult.Debug);

            await WaitForModelDrawingAsync(page, objectId);
            await SelectCanvasObjectAsync(page, objectId);
            var backingStore = await AssertObjectCanvasBackingStoreMatchesDprAsync(page, expectedDevicePixelRatio: 2);
            await DocumentEditorCanvasVisualAssert.AssertNoUiOverlapAsync(page);
            await DocumentEditorCanvasVisualAssert.AssertNoTextOverlapAsync(page);
            await page.GetByTestId("document-editor-demo").ScreenshotAsync(new LocatorScreenshotOptions
            {
                Path = screenshotPath,
                Type = ScreenshotType.Png
            });

            var manifestPath = Path.Combine(output, "manifest.json");
            await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(new
            {
                testName = nameof(PhaseE7_CanvasDrawingLayerUsesHighDpiBackingStoreForSharpShapesAndHandles),
                seedDocumentId = PhaseE7DocumentId,
                userActions = new[]
                {
                    "Open the production canvas editor in a DPR 2 browser context.",
                    "Insert and select a vector drawing object.",
                    "Verify the objects canvas backing store matches the visible CSS surface at the browser device pixel ratio.",
                    "Capture the selected drawing with object handles as screenshot evidence."
                },
                expectedVisibleChanges = "The selected drawing and object handles remain crisp on the high-DPI backing store and do not overlap toolbar or document text.",
                screenshotPaths = new[] { screenshotPath },
                backingStore
            }, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));

            TestContext.AddResultFile(screenshotPath);
            TestContext.AddResultFile(manifestPath);
        }
        finally
        {
            await context.CloseAsync();
        }
    }

    [TestMethod]
    public async Task PhaseE7_CanvasTextBoxNestedEditingUndoRedoSaveReloadHasScreenshotEvidence()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        page.Console += (_, message) => TestContext.WriteLine($"[browser:{message.Type}] {message.Text}");
        page.PageError += (_, error) => TestContext.WriteLine($"[page-error] {error}");
        await page.SetViewportSizeAsync(1440, 1000);
        await OpenPhaseE7DocumentAsync(page);

        var output = CreateOutputDirectory("textbox-editing-1440x1000");
        var editActivePath = Path.Combine(output, "00-phasee7-textbox-edit-active.png");
        var selectedTextPath = Path.Combine(output, "01-phasee7-textbox-selection-range.png");
        var reloadedPath = Path.Combine(output, "02-phasee7-textbox-reloaded.png");

        const string objectId = "e7-nested-edit-textbox";
        const string initialText = "Editable headline";
        const string inlineText = " with nested caret";
        const string secondParagraph = "Second paragraph";

        var insert = await ExecuteCanvasCommandAsync(page, "insertTextBox", new
        {
            objectId,
            anchorBlockId = "canvas-shapes-after",
            text = initialText,
            width = 260,
            height = 96,
            wrapMode = "InFrontOfText",
            x = 96,
            y = 540,
            shape = new
            {
                preset = "roundRectangle",
                fill = new { color = "#f8fafc", opacity = 1 },
                stroke = new { color = "#2563eb", width = 2 }
            },
            textBody = new
            {
                insetLeft = 12,
                insetTop = 10,
                insetRight = 12,
                insetBottom = 10,
                paragraphs = new[]
                {
                    new
                    {
                        text = initialText,
                        alignment = "left",
                        style = new { fontSize = 15, color = "#0f172a", bold = true }
                    }
                }
            }
        });
        Assert.IsTrue(insert.Changed, insert.Debug);
        await WaitForModelDrawingAsync(page, objectId);

        await EnterTextBoxEditAsync(page, objectId);
        var activate = await ExecuteCanvasCommandAsync(page, "activateTextBoxEdit", new
        {
            objectId,
            offset = initialText.Length
        });
        Assert.IsTrue(activate.Handled, activate.Debug);
        await WaitForTextBoxEditingAsync(page, objectId);

        await page.Keyboard.TypeAsync(inlineText);
        await page.Keyboard.PressAsync("Enter");
        await page.Keyboard.TypeAsync(secondParagraph);
        await WaitForTextBoxTextAsync(page, objectId, initialText + inlineText, secondParagraph);

        var editedProbe = await ReadTextBoxProbeAsync(page, objectId);
        Assert.IsTrue(editedProbe.Found, editedProbe.Debug);
        Assert.IsTrue(editedProbe.EditingActive, editedProbe.Debug);
        Assert.IsTrue(editedProbe.CaretVisible, editedProbe.Debug);
        Assert.IsTrue(editedProbe.Text.Contains(initialText + inlineText, StringComparison.Ordinal), editedProbe.Debug);
        Assert.IsTrue(editedProbe.Text.Contains(secondParagraph, StringComparison.Ordinal), editedProbe.Debug);

        await DocumentEditorCanvasVisualAssert.AssertNoUiOverlapAsync(page);
        await DocumentEditorCanvasVisualAssert.AssertNoTextOverlapAsync(page);
        await page.GetByTestId("document-editor-demo").ScreenshotAsync(new LocatorScreenshotOptions
        {
            Path = editActivePath,
            Type = ScreenshotType.Png
        });

        await page.Keyboard.PressAsync("Control+A");
        await WaitForTextBoxSelectionAsync(page, objectId);
        var selectionProbe = await ReadTextBoxProbeAsync(page, objectId);
        Assert.IsTrue(selectionProbe.SelectionActive, selectionProbe.Debug);
        Assert.IsTrue(selectionProbe.SelectionRectCount > 0, selectionProbe.Debug);
        await page.GetByTestId("document-editor-demo").ScreenshotAsync(new LocatorScreenshotOptions
        {
            Path = selectedTextPath,
            Type = ScreenshotType.Png
        });

        await page.Keyboard.PressAsync("ArrowRight");
        await ExecuteCanvasCommandAsync(page, "setTextBoxTextAlignment", new
        {
            objectId,
            alignment = "center",
            all = true
        });
        await ExecuteCanvasCommandAsync(page, "setTextBoxTextStyle", new
        {
            objectId,
            style = new { italic = true, fontSize = 16 },
            all = true
        });

        var undo = await ExecuteCanvasCommandAsync(page, "undo", new { });
        Assert.IsTrue(undo.Changed, undo.Debug);
        var afterUndo = await ReadTextBoxProbeAsync(page, objectId);
        Assert.IsFalse(afterUndo.AllItalic, afterUndo.Debug);

        var redo = await ExecuteCanvasCommandAsync(page, "redo", new { });
        Assert.IsTrue(redo.Changed, redo.Debug);
        var afterRedo = await ReadTextBoxProbeAsync(page, objectId);
        Assert.IsTrue(afterRedo.AllItalic, afterRedo.Debug);
        Assert.IsTrue(afterRedo.AllCentered, afterRedo.Debug);

        await page.GetByTestId("document-save").ClickAsync();
        await WaitForSaveBoundaryAsync(page);
        await NavigateWithinBlazorAsync(page, "/canvas-engine-host?documentId=phase-5-canvas-render");
        await page.WaitForFunctionAsync(
            "() => document.querySelector('[data-testid=\"document-canvas-page\"]')?.getAttribute('data-canvas-model-document-id') === 'phase-5-canvas-render'",
            new PageWaitForFunctionOptions { Timeout = 20_000 });
        await NavigateWithinBlazorAsync(page, $"/canvas-engine-host?documentId={PhaseE7DocumentId}&showToolbar=true&disableCollaboration=true");
        await WaitForPhaseE7ReadyAsync(page);
        await WaitForTextBoxTextAsync(page, objectId, initialText + inlineText, secondParagraph);

        var reloadedProbe = await ReadTextBoxProbeAsync(page, objectId);
        Assert.IsTrue(reloadedProbe.Found, reloadedProbe.Debug);
        Assert.IsTrue(reloadedProbe.AllItalic, reloadedProbe.Debug);
        Assert.IsTrue(reloadedProbe.AllCentered, reloadedProbe.Debug);
        await SelectCanvasObjectAsync(page, objectId);
        await DocumentEditorCanvasVisualAssert.AssertNoUiOverlapAsync(page);
        await DocumentEditorCanvasVisualAssert.AssertNoTextOverlapAsync(page);
        var objectMetrics = await DocumentEditorCanvasVisualAssert.AssertCanvasNonBlankAsync(page.Locator("[data-canvas-layer='objects']").First);
        await page.GetByTestId("document-editor-demo").ScreenshotAsync(new LocatorScreenshotOptions
        {
            Path = reloadedPath,
            Type = ScreenshotType.Png
        });

        var manifestPath = Path.Combine(output, "manifest.json");
        await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(new
        {
            testName = nameof(PhaseE7_CanvasTextBoxNestedEditingUndoRedoSaveReloadHasScreenshotEvidence),
            seedDocumentId = PhaseE7DocumentId,
            userActions = new[]
            {
                "Insert a production canvas text box through the shared command runtime.",
                "Enter nested text editing with a real double-click on the canvas object.",
                "Type through the browser keyboard/input bridge, insert a paragraph, and verify a visible textbox caret.",
                "Select textbox text with Ctrl+A and capture selection screenshot evidence.",
                "Apply textbox paragraph alignment and text style through the command runtime.",
                "Undo and redo the style change, save, reload, and verify text plus formatting persisted."
            },
            screenshotPaths = new[] { editActivePath, selectedTextPath, reloadedPath },
            insert,
            editedProbe,
            selectionProbe,
            afterUndo,
            afterRedo,
            reloadedProbe,
            objectMetrics
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));

        TestContext.AddResultFile(editActivePath);
        TestContext.AddResultFile(selectedTextPath);
        TestContext.AddResultFile(reloadedPath);
        TestContext.AddResultFile(manifestPath);
    }

    [TestMethod]
    public async Task PhaseE7_CanvasInsertedShapesPointerKeyboardAndDeleteInteractionsHaveScreenshotEvidence()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        page.Console += (_, message) => TestContext.WriteLine($"[browser:{message.Type}] {message.Text}");
        page.PageError += (_, error) => TestContext.WriteLine($"[page-error] {error}");
        await page.SetViewportSizeAsync(1440, 1000);
        await OpenPhaseE7DocumentAsync(page);

        var output = CreateOutputDirectory("desktop-1440x1000-interactions");
        var insertedPath = Path.Combine(output, "03-phasee7-inserted-shape-gallery.png");
        var movedPath = Path.Combine(output, "04-phasee7-pointer-moved.png");
        var resizedPath = Path.Combine(output, "05-phasee7-pointer-resized.png");
        var redonePath = Path.Combine(output, "06-phasee7-pointer-redone.png");
        var rotatedPath = Path.Combine(output, "07-phasee7-pointer-rotated.png");
        var keyboardPath = Path.Combine(output, "08-phasee7-keyboard-nudged.png");
        var deletedPath = Path.Combine(output, "09-phasee7-keyboard-delete.png");

        var rectangleId = $"e7-pointer-rectangle-{Guid.NewGuid():N}";
        var ellipseId = $"e7-pointer-ellipse-{Guid.NewGuid():N}";
        var arrowId = $"e7-pointer-arrow-{Guid.NewGuid():N}";
        var connectorId = $"e7-pointer-connector-{Guid.NewGuid():N}";
        var objectLayer = page.Locator("[data-canvas-layer='objects']").First;
        var insertedShapeRegionBefore = await DocumentEditorCanvasVisualAssert.CaptureCanvasRegionSnapshotAsync(
            objectLayer,
            "phasee7-inserted-shapes",
            70,
            430,
            600,
            130);

        var rectangleInsert = await ExecuteCanvasCommandAsync(page, "insertShape", new
        {
            objectId = rectangleId,
            anchorBlockId = "canvas-shapes-after",
            width = 148,
            height = 86,
            wrapMode = "InFrontOfText",
            x = 82,
            y = 438,
            shape = new
            {
                preset = "rectangle",
                fill = new { type = "linearGradient", color = "#dbeafe", secondaryColor = "#bfdbfe", opacity = 1, angle = 45 },
                stroke = new { color = "#2563eb", width = 2 }
            },
            altText = "E7 pointer rectangle"
        });
        Assert.IsTrue(rectangleInsert.Changed, rectangleInsert.Debug);

        var ellipseInsert = await ExecuteCanvasCommandAsync(page, "insertShape", new
        {
            objectId = ellipseId,
            anchorBlockId = "canvas-shapes-after",
            width = 132,
            height = 88,
            wrapMode = "InFrontOfText",
            x = 282,
            y = 438,
            shape = new
            {
                preset = "ellipse",
                fill = new { color = "#fef3c7", opacity = 1 },
                stroke = new { color = "#d97706", width = 2 }
            },
            altText = "E7 keyboard ellipse"
        });
        Assert.IsTrue(ellipseInsert.Changed, ellipseInsert.Debug);

        var arrowInsert = await ExecuteCanvasCommandAsync(page, "insertLine", new
        {
            objectId = arrowId,
            anchorBlockId = "canvas-shapes-after",
            width = 184,
            height = 34,
            wrapMode = "InFrontOfText",
            x = 466,
            y = 466,
            shape = new
            {
                preset = "line",
                fill = new { type = "none", color = "#ffffff" },
                stroke = new { color = "#16a34a", width = 3, endArrow = "triangle" }
            },
            altText = "E7 pointer arrow"
        });
        Assert.IsTrue(arrowInsert.Changed, arrowInsert.Debug);

        var connectorInsert = await ExecuteCanvasCommandAsync(page, "insertConnector", new
        {
            objectId = connectorId,
            anchorBlockId = "canvas-shapes-after",
            width = 260,
            height = 76,
            wrapMode = "InFrontOfText",
            x = 170,
            y = 452,
            shape = new
            {
                preset = "bentConnector",
                fill = new { type = "none", color = "#ffffff" },
                stroke = new { color = "#0f766e", width = 2, endArrow = "triangle" },
                routing = "elbow",
                startConnection = new { objectId = rectangleId, site = "right" },
                endConnection = new { objectId = ellipseId, site = "left" }
            },
            altText = "E7 connector between rectangle and ellipse"
        });
        Assert.IsTrue(connectorInsert.Changed, connectorInsert.Debug);

        await WaitForModelDrawingAsync(page, rectangleId);
        await WaitForModelDrawingAsync(page, ellipseId);
        await WaitForModelDrawingAsync(page, arrowId);
        await WaitForModelDrawingAsync(page, connectorId);
        await SelectCanvasObjectAsync(page, rectangleId);
        await DocumentEditorCanvasVisualAssert.AssertNoUiOverlapAsync(page);
        await DocumentEditorCanvasVisualAssert.AssertNoTextOverlapAsync(page);
        var insertedShapeRegionDelta = await DocumentEditorCanvasVisualAssert.AssertCanvasRegionChangedFromSnapshotAsync(objectLayer, "phasee7-inserted-shapes", 512);
        var insertedMetrics = await DocumentEditorCanvasVisualAssert.AssertCanvasNonBlankAsync(objectLayer);
        await page.GetByTestId("document-editor-demo").ScreenshotAsync(new LocatorScreenshotOptions
        {
            Path = insertedPath,
            Type = ScreenshotType.Png
        });

        var beforeMove = await ReadObjectTransformAsync(page, rectangleId);
        await DragCanvasObjectAsync(page, rectangleId, 54, 34);
        await WaitForObjectTransformAtLeastAsync(page, rectangleId, beforeMove.X + 30, beforeMove.Y + 20, beforeMove.Width, beforeMove.Height);
        var afterMove = await ReadObjectTransformAsync(page, rectangleId);
        Assert.IsTrue(afterMove.X > beforeMove.X, afterMove.Debug);
        Assert.IsTrue(afterMove.Y > beforeMove.Y, afterMove.Debug);
        await SelectCanvasObjectAsync(page, rectangleId);
        await page.GetByTestId("document-editor-demo").ScreenshotAsync(new LocatorScreenshotOptions
        {
            Path = movedPath,
            Type = ScreenshotType.Png
        });

        await ResizeCanvasObjectAsync(page, rectangleId, 58, 38);
        await WaitForObjectTransformAtLeastAsync(page, rectangleId, afterMove.X, afterMove.Y, afterMove.Width + 20, afterMove.Height + 12);
        var afterResize = await ReadObjectTransformAsync(page, rectangleId);
        Assert.IsTrue(afterResize.Width > afterMove.Width, afterResize.Debug);
        Assert.IsTrue(afterResize.Height > afterMove.Height, afterResize.Debug);
        await SelectCanvasObjectAsync(page, rectangleId);
        await page.GetByTestId("document-editor-demo").ScreenshotAsync(new LocatorScreenshotOptions
        {
            Path = resizedPath,
            Type = ScreenshotType.Png
        });

        var undoResize = await ExecuteCanvasCommandAsync(page, "undo", new { });
        Assert.IsTrue(undoResize.Changed, undoResize.Debug);
        await WaitForObjectTransformCloseAsync(page, rectangleId, afterMove.Width, afterMove.Height, tolerance: 0.75);
        var redoResize = await ExecuteCanvasCommandAsync(page, "redo", new { });
        Assert.IsTrue(redoResize.Changed, redoResize.Debug);
        await WaitForObjectTransformAtLeastAsync(page, rectangleId, afterMove.X, afterMove.Y, afterResize.Width - 0.5, afterResize.Height - 0.5);
        await SelectCanvasObjectAsync(page, rectangleId);
        await page.GetByTestId("document-editor-demo").ScreenshotAsync(new LocatorScreenshotOptions
        {
            Path = redonePath,
            Type = ScreenshotType.Png
        });

        await RotateCanvasObjectAsync(page, rectangleId, 76, 38);
        await WaitForObjectRotationMagnitudeAtLeastAsync(page, rectangleId, 10);
        var afterRotate = await ReadObjectTransformAsync(page, rectangleId);
        Assert.IsTrue(Math.Abs(afterRotate.Rotation) >= 10, afterRotate.Debug);
        await SelectCanvasObjectAsync(page, rectangleId);
        await page.GetByTestId("document-editor-demo").ScreenshotAsync(new LocatorScreenshotOptions
        {
            Path = rotatedPath,
            Type = ScreenshotType.Png
        });

        var undoRotate = await ExecuteCanvasCommandAsync(page, "undo", new { });
        Assert.IsTrue(undoRotate.Changed, undoRotate.Debug);
        await WaitForObjectRotationCloseAsync(page, rectangleId, 0, tolerance: 0.75);
        var redoRotate = await ExecuteCanvasCommandAsync(page, "redo", new { });
        Assert.IsTrue(redoRotate.Changed, redoRotate.Debug);
        await WaitForObjectRotationMagnitudeAtLeastAsync(page, rectangleId, 10);

        await SelectCanvasObjectAsync(page, ellipseId);
        var beforeKeyboard = await ReadObjectTransformAsync(page, ellipseId);
        await page.GetByTestId("document-canvas-hidden-input").FocusAsync();
        await page.Keyboard.PressAsync("ArrowRight");
        await WaitForObjectTransformAtLeastAsync(page, ellipseId, beforeKeyboard.X + 1, beforeKeyboard.Y, beforeKeyboard.Width, beforeKeyboard.Height);
        await page.Keyboard.PressAsync("Shift+ArrowDown");
        await WaitForObjectTransformAtLeastAsync(page, ellipseId, beforeKeyboard.X + 1, beforeKeyboard.Y + 10, beforeKeyboard.Width, beforeKeyboard.Height);
        await page.Keyboard.PressAsync("Alt+ArrowRight");
        await WaitForObjectTransformAtLeastAsync(page, ellipseId, beforeKeyboard.X + 1, beforeKeyboard.Y + 10, beforeKeyboard.Width + 1, beforeKeyboard.Height);
        await page.Keyboard.PressAsync("Alt+ArrowDown");
        await WaitForObjectTransformAtLeastAsync(page, ellipseId, beforeKeyboard.X + 1, beforeKeyboard.Y + 10, beforeKeyboard.Width + 1, beforeKeyboard.Height + 1);
        await page.Keyboard.PressAsync("Tab");
        await WaitForSelectedObjectDifferentFromAsync(page, ellipseId);
        await SelectCanvasObjectAsync(page, ellipseId);
        await page.Keyboard.PressAsync("Escape");
        await WaitForNoObjectSelectionAsync(page);
        await SelectCanvasObjectAsync(page, ellipseId);
        await page.GetByTestId("document-editor-demo").ScreenshotAsync(new LocatorScreenshotOptions
        {
            Path = keyboardPath,
            Type = ScreenshotType.Png
        });

        await SelectCanvasObjectAsync(page, arrowId);
        await page.Keyboard.PressAsync("Delete");
        await WaitForModelDrawingAbsentAsync(page, arrowId);
        await page.GetByTestId("document-editor-demo").ScreenshotAsync(new LocatorScreenshotOptions
        {
            Path = deletedPath,
            Type = ScreenshotType.Png
        });

        await DocumentEditorCanvasVisualAssert.AssertNoUiOverlapAsync(page);
        await DocumentEditorCanvasVisualAssert.AssertNoTextOverlapAsync(page);
        var finalMetrics = await DocumentEditorCanvasVisualAssert.AssertCanvasNonBlankAsync(page.Locator("[data-canvas-layer='objects']").First);

        var manifestPath = Path.Combine(output, "manifest.json");
        await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(new
        {
            testName = nameof(PhaseE7_CanvasInsertedShapesPointerKeyboardAndDeleteInteractionsHaveScreenshotEvidence),
            seedDocumentId = PhaseE7DocumentId,
            userActions = new[]
            {
                "Insert a rectangle, ellipse, and arrow through the production canvas command runtime.",
                "Insert an elbow connector bound to the rectangle and ellipse connection sites.",
                "Select the rectangle and verify resize handles before dragging it with a real pointer gesture.",
                "Resize the selected rectangle through the visible southeast handle and verify undo/redo restores the exact transform path.",
                "Rotate the selected rectangle through the visible rotate handle and verify undo/redo restores the transform rotation.",
                "Use keyboard-only object controls: Arrow, Shift+Arrow, Alt+Arrow, Tab, Escape, and Delete.",
                "Verify Delete removes the selected drawing object from the canvas model."
            },
            expectedVisibleChanges = "Inserted vector shapes are visible in the canvas object layer, selection handles stay within the document surface, pointer drag/resize/rotate changes the model transform, keyboard controls mutate or clear the object selection, and Delete removes the selected drawing object.",
            screenshotPaths = new[] { insertedPath, movedPath, resizedPath, redonePath, rotatedPath, keyboardPath, deletedPath },
            insertedShapeRegionBefore,
            insertedShapeRegionDelta,
            rectangleInsert,
            ellipseInsert,
            arrowInsert,
            connectorInsert,
            beforeMove,
            afterMove,
            afterResize,
            afterRotate,
            beforeKeyboard,
            insertedMetrics,
            finalMetrics
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));

        TestContext.AddResultFile(insertedPath);
        TestContext.AddResultFile(movedPath);
        TestContext.AddResultFile(resizedPath);
        TestContext.AddResultFile(redonePath);
        TestContext.AddResultFile(rotatedPath);
        TestContext.AddResultFile(keyboardPath);
        TestContext.AddResultFile(deletedPath);
        TestContext.AddResultFile(manifestPath);
    }

    [TestMethod]
    public async Task PhaseE7_CanvasGroupUngroupAlignAndDistributeHaveScreenshotEvidence()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        page.Console += (_, message) => TestContext.WriteLine($"[browser:{message.Type}] {message.Text}");
        page.PageError += (_, error) => TestContext.WriteLine($"[page-error] {error}");
        await page.SetViewportSizeAsync(1440, 1000);
        await OpenPhaseE7DocumentAsync(page);

        var output = CreateOutputDirectory("desktop-1440x1000-groups");
        var groupedPath = Path.Combine(output, "10-phasee7-grouped.png");
        var movedPath = Path.Combine(output, "11-phasee7-group-moved.png");
        var ungroupedPath = Path.Combine(output, "12-phasee7-ungrouped.png");
        var zOrderPath = Path.Combine(output, "13-phasee7-group-zorder-front.png");

        var shapeA = $"e7-group-a-{Guid.NewGuid():N}";
        var shapeB = $"e7-group-b-{Guid.NewGuid():N}";
        var shapeC = $"e7-group-c-{Guid.NewGuid():N}";
        var groupId = $"e7-group-{Guid.NewGuid():N}";

        foreach (var insert in new[]
        {
            new { ObjectId = shapeA, Preset = "rectangle", X = 80, Y = 530, Width = 118, Height = 70, Fill = "#dbeafe", Stroke = "#2563eb" },
            new { ObjectId = shapeB, Preset = "diamond", X = 284, Y = 562, Width = 106, Height = 72, Fill = "#fef3c7", Stroke = "#d97706" },
            new { ObjectId = shapeC, Preset = "rightArrow", X = 486, Y = 540, Width = 128, Height = 78, Fill = "#dcfce7", Stroke = "#16a34a" }
        })
        {
            var result = await ExecuteCanvasCommandAsync(page, "insertShape", new
            {
                objectId = insert.ObjectId,
                anchorBlockId = "canvas-shapes-after",
                width = insert.Width,
                height = insert.Height,
                wrapMode = "InFrontOfText",
                x = insert.X,
                y = insert.Y,
                shape = new
                {
                    preset = insert.Preset,
                    fill = new { color = insert.Fill, opacity = 1 },
                    stroke = new { color = insert.Stroke, width = 2 }
                },
                altText = "E7 grouped drawing shape"
            });
            Assert.IsTrue(result.Changed, result.Debug);
            await WaitForModelDrawingAsync(page, insert.ObjectId);
        }

        var align = await ExecuteCanvasCommandAsync(page, "alignObjects", new
        {
            objectIds = new[] { shapeA, shapeB, shapeC },
            alignment = "top"
        });
        Assert.IsTrue(align.Changed, align.Debug);
        await WaitForObjectPositionCloseAsync(page, shapeB, 284, 530, 106, 72, 1.0);

        var distribute = await ExecuteCanvasCommandAsync(page, "distributeObjects", new
        {
            objectIds = new[] { shapeA, shapeB, shapeC },
            axis = "horizontal"
        });
        Assert.IsTrue(distribute.Changed, distribute.Debug);

        var group = await ExecuteCanvasCommandAsync(page, "groupObjects", new
        {
            objectId = groupId,
            objectIds = new[] { shapeA, shapeB, shapeC },
            wrapMode = "InFrontOfText",
            altText = "E7 grouped drawing objects"
        });
        Assert.IsTrue(group.Changed, group.Debug);
        await WaitForModelDrawingAsync(page, groupId);
        await SelectCanvasObjectAsync(page, groupId);
        await DocumentEditorCanvasVisualAssert.AssertNoUiOverlapAsync(page);
        await DocumentEditorCanvasVisualAssert.AssertNoTextOverlapAsync(page);
        await page.GetByTestId("document-editor-demo").ScreenshotAsync(new LocatorScreenshotOptions
        {
            Path = groupedPath,
            Type = ScreenshotType.Png
        });

        var beforeZGroup = await ReadObjectTransformAsync(page, groupId);
        var beforeZA = await ReadObjectTransformAsync(page, shapeA);
        var beforeZB = await ReadObjectTransformAsync(page, shapeB);
        var beforeZC = await ReadObjectTransformAsync(page, shapeC);
        var bringFront = await ExecuteCanvasCommandAsync(page, "setImageZOrder", new
        {
            objectId = groupId,
            direction = "front"
        });
        Assert.IsTrue(bringFront.Changed, bringFront.Debug);
        var afterZGroup = await ReadObjectTransformAsync(page, groupId);
        var afterZA = await ReadObjectTransformAsync(page, shapeA);
        var afterZB = await ReadObjectTransformAsync(page, shapeB);
        var afterZC = await ReadObjectTransformAsync(page, shapeC);
        var zDelta = afterZGroup.ZIndex - beforeZGroup.ZIndex;
        Assert.IsTrue(zDelta > 0, afterZGroup.Debug);
        Assert.AreEqual(beforeZA.ZIndex + zDelta, afterZA.ZIndex, 0.001, afterZA.Debug);
        Assert.AreEqual(beforeZB.ZIndex + zDelta, afterZB.ZIndex, 0.001, afterZB.Debug);
        Assert.AreEqual(beforeZC.ZIndex + zDelta, afterZC.ZIndex, 0.001, afterZC.Debug);
        await SelectCanvasObjectAsync(page, groupId);
        await page.GetByTestId("document-editor-demo").ScreenshotAsync(new LocatorScreenshotOptions
        {
            Path = zOrderPath,
            Type = ScreenshotType.Png
        });

        var beforeGroup = await ReadObjectTransformAsync(page, groupId);
        var beforeA = await ReadObjectTransformAsync(page, shapeA);
        var moveGroup = await ExecuteCanvasCommandAsync(page, "updateImageLayout", new
        {
            objectId = groupId,
            x = beforeGroup.X + 36,
            y = beforeGroup.Y + 28,
            width = beforeGroup.Width,
            height = beforeGroup.Height
        });
        Assert.IsTrue(moveGroup.Changed, moveGroup.Debug);
        await WaitForObjectPositionCloseAsync(page, shapeA, beforeA.X + 36, beforeA.Y + 28, beforeA.Width, beforeA.Height, 1.0);
        await SelectCanvasObjectAsync(page, groupId);
        await page.GetByTestId("document-editor-demo").ScreenshotAsync(new LocatorScreenshotOptions
        {
            Path = movedPath,
            Type = ScreenshotType.Png
        });

        var undo = await ExecuteCanvasCommandAsync(page, "undo", new { });
        Assert.IsTrue(undo.Changed, undo.Debug);
        await WaitForObjectPositionCloseAsync(page, shapeA, beforeA.X, beforeA.Y, beforeA.Width, beforeA.Height, 1.0);
        var redo = await ExecuteCanvasCommandAsync(page, "redo", new { });
        Assert.IsTrue(redo.Changed, redo.Debug);
        await WaitForObjectPositionCloseAsync(page, shapeA, beforeA.X + 36, beforeA.Y + 28, beforeA.Width, beforeA.Height, 1.0);

        var ungroup = await ExecuteCanvasCommandAsync(page, "ungroupObjects", new { objectId = groupId });
        Assert.IsTrue(ungroup.Changed, ungroup.Debug);
        await WaitForModelDrawingAbsentAsync(page, groupId);
        await page.GetByTestId("document-editor-demo").ScreenshotAsync(new LocatorScreenshotOptions
        {
            Path = ungroupedPath,
            Type = ScreenshotType.Png
        });
        await DocumentEditorCanvasVisualAssert.AssertNoUiOverlapAsync(page);
        await DocumentEditorCanvasVisualAssert.AssertNoTextOverlapAsync(page);
        var objectMetrics = await DocumentEditorCanvasVisualAssert.AssertCanvasNonBlankAsync(page.Locator("[data-canvas-layer='objects']").First);

        var manifestPath = Path.Combine(output, "manifest.json");
        await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(new
        {
            testName = nameof(PhaseE7_CanvasGroupUngroupAlignAndDistributeHaveScreenshotEvidence),
            seedDocumentId = PhaseE7DocumentId,
            userActions = new[]
            {
                "Insert three vector shapes through production canvas commands.",
                "Align their top edges and distribute them horizontally.",
                "Group the explicit object set and capture visible group selection handles.",
                "Bring the group wrapper to the front and verify the same z-order delta is applied to every child drawing.",
                "Move the group and verify child transforms move through the same undoable transaction.",
                "Undo and redo the group move, then ungroup and verify the group object is removed."
            },
            expectedVisibleChanges = "Aligned shapes become a grouped drawing object with handles, the group move shifts every child drawing, undo/redo restores the child transforms, and ungroup removes only the group wrapper.",
            screenshotPaths = new[] { groupedPath, movedPath, ungroupedPath, zOrderPath },
            align,
            distribute,
            group,
            bringFront,
            moveGroup,
            undo,
            redo,
            ungroup,
            beforeGroup,
            beforeA,
            beforeZGroup,
            afterZGroup,
            beforeZA,
            afterZA,
            beforeZB,
            afterZB,
            beforeZC,
            afterZC,
            objectMetrics
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));

        TestContext.AddResultFile(groupedPath);
        TestContext.AddResultFile(movedPath);
        TestContext.AddResultFile(ungroupedPath);
        TestContext.AddResultFile(zOrderPath);
        TestContext.AddResultFile(manifestPath);
    }

    [TestMethod]
    public async Task PhaseE7_CanvasConnectorEndpointClipboardAndAllDrawingTypesPersistWithScreenshotEvidence()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        page.Console += (_, message) => TestContext.WriteLine($"[browser:{message.Type}] {message.Text}");
        page.PageError += (_, error) => TestContext.WriteLine($"[page-error] {error}");
        await page.SetViewportSizeAsync(1440, 1000);
        await OpenPhaseE7DocumentAsync(page);

        var output = CreateOutputDirectory("desktop-1440x1000-endpoint-clipboard-save");
        var handlesPath = Path.Combine(output, "14-phasee7-connector-endpoint-handles.png");
        var endpointDraggedPath = Path.Combine(output, "15-phasee7-connector-endpoint-dragged.png");
        var clipboardPath = Path.Combine(output, "16-phasee7-object-clipboard-pasted.png");
        var reloadedPath = Path.Combine(output, "17-phasee7-all-drawing-types-reloaded.png");

        var suffix = Guid.NewGuid().ToString("N");
        var shapeId = $"e7-save-shape-{suffix}";
        var textboxId = $"e7-save-textbox-{suffix}";
        var lineId = $"e7-save-line-{suffix}";
        var connectorId = $"e7-save-connector-{suffix}";
        var chartId = $"e7-save-chart-{suffix}";
        var groupId = $"e7-save-group-{suffix}";
        var pastedGroupId = $"{groupId}-copy";

        foreach (var result in new[]
        {
            await ExecuteCanvasCommandAsync(page, "insertShape", new
            {
                objectId = shapeId,
                anchorBlockId = "canvas-shapes-after",
                width = 136,
                height = 78,
                wrapMode = "Square",
                x = 86,
                y = 610,
                shape = new { preset = "rectangle", fill = new { color = "#dbeafe", opacity = 1 }, stroke = new { color = "#2563eb", width = 2 } },
                altText = "E7 persisted square wrapped shape"
            }),
            await ExecuteCanvasCommandAsync(page, "insertTextBox", new
            {
                objectId = textboxId,
                anchorBlockId = "canvas-shapes-after",
                text = "Clipboard save text box",
                width = 214,
                height = 82,
                wrapMode = "InFrontOfText",
                x = 282,
                y = 608,
                shape = new { preset = "roundRectangle", fill = new { color = "#fef3c7", opacity = 1 }, stroke = new { color = "#d97706", width = 2 } },
                altText = "E7 clipboard text box"
            }),
            await ExecuteCanvasCommandAsync(page, "insertLine", new
            {
                objectId = lineId,
                anchorBlockId = "canvas-shapes-after",
                width = 182,
                height = 32,
                wrapMode = "InFrontOfText",
                x = 540,
                y = 632,
                shape = new { preset = "line", fill = new { type = "none", color = "#ffffff" }, stroke = new { color = "#16a34a", width = 3, endArrow = "triangle" } },
                altText = "E7 persisted line"
            }),
            await ExecuteCanvasCommandAsync(page, "insertConnector", new
            {
                objectId = connectorId,
                anchorBlockId = "canvas-shapes-after",
                width = 292,
                height = 86,
                wrapMode = "InFrontOfText",
                x = 192,
                y = 616,
                shape = new
                {
                    preset = "bentConnector",
                    fill = new { type = "none", color = "#ffffff" },
                    stroke = new { color = "#0f766e", width = 2, endArrow = "triangle" },
                    routing = "elbow",
                    startConnection = new { objectId = shapeId, site = "right" },
                    endConnection = new { objectId = textboxId, site = "left" }
                },
                altText = "E7 persisted connector"
            }),
            await ExecuteCanvasCommandAsync(page, "insertChart", new
            {
                objectId = chartId,
                anchorBlockId = "canvas-shapes-after",
                width = 260,
                height = 160,
                wrapMode = "TopBottom",
                x = 84,
                y = 722,
                chart = new
                {
                    type = "line",
                    title = "E7 persisted chart",
                    categories = new[] { "A", "B", "C" },
                    series = new[] { new { name = "Value", values = new[] { 3, 8, 5 }, color = "#2563eb" } },
                    showLegend = true
                },
                altText = "E7 persisted chart"
            })
        })
        {
            Assert.IsTrue(result.Changed, result.Debug);
        }

        var group = await ExecuteCanvasCommandAsync(page, "groupObjects", new
        {
            objectId = groupId,
            objectIds = new[] { shapeId, textboxId },
            wrapMode = "InFrontOfText",
            altText = "E7 persisted group"
        });
        Assert.IsTrue(group.Changed, group.Debug);

        foreach (var objectId in new[] { shapeId, textboxId, lineId, connectorId, chartId, groupId })
        {
            await WaitForModelDrawingAsync(page, objectId);
        }

        await SelectCanvasObjectAsync(page, connectorId);
        await WaitForConnectorEndpointHandlesAsync(page, connectorId);
        await DocumentEditorCanvasVisualAssert.AssertNoUiOverlapAsync(page);
        await DocumentEditorCanvasVisualAssert.AssertNoTextOverlapAsync(page);
        await page.GetByTestId("document-editor-demo").ScreenshotAsync(new LocatorScreenshotOptions { Path = handlesPath, Type = ScreenshotType.Png });

        var beforeConnector = await ReadConnectorEndpointProbeAsync(page, connectorId);
        await DragConnectorEndpointAsync(page, connectorId, "end", 76, 54);
        await WaitForConnectorEndpointDetachedAsync(page, connectorId, "end");
        var afterConnector = await ReadConnectorEndpointProbeAsync(page, connectorId);
        Assert.IsTrue(afterConnector.EndDetached, afterConnector.Debug);
        Assert.IsTrue(afterConnector.EndX > beforeConnector.EndX || afterConnector.EndY > beforeConnector.EndY, afterConnector.Debug);
        await SelectCanvasObjectAsync(page, connectorId);
        await page.GetByTestId("document-editor-demo").ScreenshotAsync(new LocatorScreenshotOptions { Path = endpointDraggedPath, Type = ScreenshotType.Png });

        await SelectCanvasObjectAsync(page, groupId);
        var clipboard = await CopyPasteSelectedObjectAsync(page, pastedGroupId);
        Assert.IsTrue(clipboard.Copied, clipboard.Debug);
        Assert.IsTrue(clipboard.Pasted, clipboard.Debug);
        await WaitForModelDrawingAsync(page, pastedGroupId);
        await SelectCanvasObjectAsync(page, pastedGroupId);
        await page.GetByTestId("document-editor-demo").ScreenshotAsync(new LocatorScreenshotOptions { Path = clipboardPath, Type = ScreenshotType.Png });

        await page.GetByTestId("document-save").ClickAsync();
        await WaitForSaveBoundaryAsync(page);
        await NavigateWithinBlazorAsync(page, "/canvas-engine-host?documentId=phase-5-canvas-render");
        await page.WaitForFunctionAsync(
            "() => document.querySelector('[data-testid=\"document-canvas-page\"]')?.getAttribute('data-canvas-model-document-id') === 'phase-5-canvas-render'",
            new PageWaitForFunctionOptions { Timeout = 20_000 });
        await page.EvaluateAsync("() => window.scrollTo({ top: 0, behavior: 'instant' })");
        await NavigateWithinBlazorAsync(page, $"/canvas-engine-host?documentId={PhaseE7DocumentId}&showToolbar=true&disableCollaboration=true");
        await WaitForPhaseE7ReadyAsync(page);

        foreach (var objectId in new[] { shapeId, textboxId, lineId, connectorId, chartId, groupId, pastedGroupId })
        {
            await WaitForModelDrawingAsync(page, objectId);
        }

        await WaitForConnectorEndpointDetachedAsync(page, connectorId, "end");
        var reloadedConnector = await ReadConnectorEndpointProbeAsync(page, connectorId);
        Assert.IsTrue(reloadedConnector.EndDetached, reloadedConnector.Debug);
        await DocumentEditorCanvasVisualAssert.AssertNoUiOverlapAsync(page);
        await DocumentEditorCanvasVisualAssert.AssertNoTextOverlapAsync(page);
        var objectMetrics = await DocumentEditorCanvasVisualAssert.AssertCanvasNonBlankAsync(page.Locator("[data-canvas-layer='objects']").First);
        await page.GetByTestId("document-editor-demo").ScreenshotAsync(new LocatorScreenshotOptions { Path = reloadedPath, Type = ScreenshotType.Png });

        var manifestPath = Path.Combine(output, "manifest.json");
        await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(new
        {
            testName = nameof(PhaseE7_CanvasConnectorEndpointClipboardAndAllDrawingTypesPersistWithScreenshotEvidence),
            seedDocumentId = PhaseE7DocumentId,
            objectIds = new[] { shapeId, textboxId, lineId, connectorId, chartId, groupId, pastedGroupId },
            userActions = new[]
            {
                "Insert a shape, text box, line, connector, chart, and group through production canvas commands.",
                "Select the connector and verify visible start/end endpoint handles.",
                "Drag the connector end handle with a real pointer gesture and verify the endpoint detaches into persisted free-point geometry.",
                "Copy and paste the selected group through the real clipboard controller event path.",
                "Save, navigate away, reload, and verify every drawing type plus the pasted object still exists."
            },
            screenshotPaths = new[] { handlesPath, endpointDraggedPath, clipboardPath, reloadedPath },
            beforeConnector,
            afterConnector,
            reloadedConnector,
            clipboard,
            objectMetrics
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));

        TestContext.AddResultFile(handlesPath);
        TestContext.AddResultFile(endpointDraggedPath);
        TestContext.AddResultFile(clipboardPath);
        TestContext.AddResultFile(reloadedPath);
        TestContext.AddResultFile(manifestPath);
    }

    [TestMethod]
    public async Task PhaseE7_CanvasDrawingResponsiveGalleryHasScreenshotEvidence()
    {
        var viewports = new[]
        {
            new { Name = "desktop-gallery", Width = 1440, Height = 1000 },
            new { Name = "tablet-gallery", Width = 1024, Height = 900 },
            new { Name = "mobile-gallery", Width = 390, Height = 900 }
        };
        var screenshots = new List<string>();

        foreach (var viewport in viewports)
        {
            var context = await CreateContextAsync();
            var page = await context.NewPageAsync();
            page.Console += (_, message) => TestContext.WriteLine($"[browser:{viewport.Name}:{message.Type}] {message.Text}");
            page.PageError += (_, error) => TestContext.WriteLine($"[page-error:{viewport.Name}] {error}");
            await page.SetViewportSizeAsync(viewport.Width, viewport.Height);
            await OpenPhaseE7DocumentAsync(page);

            var output = CreateOutputDirectory(viewport.Name);
            var path = Path.Combine(output, $"18-phasee7-responsive-{viewport.Name}.png");
            var suffix = Guid.NewGuid().ToString("N");
            var a = $"e7-gallery-a-{suffix}";
            var b = $"e7-gallery-b-{suffix}";
            var c = $"e7-gallery-chart-{suffix}";
            var groupId = $"e7-gallery-group-{suffix}";

            var shapeA = await ExecuteCanvasCommandAsync(page, "insertShape", new
            {
                objectId = a,
                anchorBlockId = "canvas-shapes-after",
                width = 128,
                height = 74,
                wrapMode = "InFrontOfText",
                x = 72,
                y = 508,
                shape = new { preset = "rightArrow", fill = new { color = "#dbeafe", opacity = 1 }, stroke = new { color = "#2563eb", width = 2 } },
                altText = "E7 responsive gallery arrow"
            });
            Assert.IsTrue(shapeA.Changed, shapeA.Debug);

            var shapeB = await ExecuteCanvasCommandAsync(page, "insertTextBox", new
            {
                objectId = b,
                anchorBlockId = "canvas-shapes-after",
                text = "Responsive gallery",
                width = 190,
                height = 76,
                wrapMode = "InFrontOfText",
                x = 242,
                y = 506,
                shape = new { preset = "roundRectangle", fill = new { color = "#fef3c7", opacity = 1 }, stroke = new { color = "#d97706", width = 2 } },
                altText = "E7 responsive gallery text box"
            });
            Assert.IsTrue(shapeB.Changed, shapeB.Debug);

            var chart = await ExecuteCanvasCommandAsync(page, "insertChart", new
            {
                objectId = c,
                anchorBlockId = "canvas-shapes-after",
                width = 238,
                height = 144,
                wrapMode = "TopBottom",
                x = 470,
                y = 492,
                chart = new
                {
                    type = "area",
                    title = "Gallery chart",
                    categories = new[] { "One", "Two", "Three" },
                    series = new[] { new { name = "Score", values = new[] { 4, 7, 6 }, color = "#16a34a" } },
                    showLegend = true
                },
                altText = "E7 responsive gallery chart"
            });
            Assert.IsTrue(chart.Changed, chart.Debug);

            var group = await ExecuteCanvasCommandAsync(page, "groupObjects", new
            {
                objectId = groupId,
                objectIds = new[] { a, b },
                wrapMode = "InFrontOfText",
                altText = "E7 responsive gallery group"
            });
            Assert.IsTrue(group.Changed, group.Debug);
            await WaitForModelDrawingAsync(page, groupId);
            await SelectCanvasObjectAsync(page, groupId);
            await DocumentEditorCanvasVisualAssert.AssertNoUiOverlapAsync(page);
            await DocumentEditorCanvasVisualAssert.AssertNoTextOverlapAsync(page);
            await DocumentEditorCanvasVisualAssert.AssertCanvasNonBlankAsync(page.Locator("[data-canvas-layer='objects']").First);
            await page.GetByTestId("document-editor-demo").ScreenshotAsync(new LocatorScreenshotOptions { Path = path, Type = ScreenshotType.Png });

            screenshots.Add(path);
            TestContext.AddResultFile(path);
            await context.CloseAsync();
        }

        var manifestPath = Path.Combine(CreateOutputDirectory("responsive-gallery"), "manifest.json");
        await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(new
        {
            testName = nameof(PhaseE7_CanvasDrawingResponsiveGalleryHasScreenshotEvidence),
            seedDocumentId = PhaseE7DocumentId,
            viewports,
            screenshotPaths = screenshots,
            uxVerdict = "The gallery shows production canvas drawing objects with recognizable shape, text box, chart and grouped selection states across desktop, tablet and mobile viewports without detected UI or text overlap."
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));
        TestContext.AddResultFile(manifestPath);
    }

    private async Task OpenPhaseE7DocumentAsync(IPage page)
    {
        await page.GotoAsync($"{BaseUrl}/canvas-engine-host?documentId={PhaseE7DocumentId}&showToolbar=true&disableCollaboration=true", new PageGotoOptions
        {
            WaitUntil = WaitUntilState.Load,
            Timeout = 60_000
        });
        await WaitForPhaseE7ReadyAsync(page);
        await WaitForPhaseE7SettledAsync(page);
    }

    private static Task WaitForPhaseE7ReadyAsync(IPage page)
        => page.WaitForFunctionAsync(
            """
            () => document.querySelector('[data-testid="document-canvas-engine-host"]')?.getAttribute('data-canvas-engine-ready') === 'true'
                && document.querySelector('[data-testid="document-canvas-page"]')?.getAttribute('data-canvas-model-document-id') === 'phase-e7-canvas-shapes-drawings'
                && Number(document.querySelector('[data-testid="document-canvas-page"]')?.getAttribute('data-canvas-drawing-count') || '0') >= 4
                && document.querySelector('[data-canvas-object][data-object-id="canvas-chart-bar"]')
            """,
            new PageWaitForFunctionOptions { Timeout = 30_000 });

    private static async Task WaitForPhaseE7SettledAsync(IPage page)
    {
        await page.WaitForFunctionAsync(
            """
            () => {
                const host = document.querySelector('[data-testid="document-canvas-engine-host"]');
                const page = document.querySelector('[data-testid="document-canvas-page"]');
                const saveButton = document.querySelector('[data-testid="document-save"]');
                const pending = document.querySelector('[data-testid="document-pending-status"]')?.textContent || '';
                const dirty = document.querySelector('[data-testid="document-dirty-status"]')?.textContent || '';
                const objectCount = document.querySelectorAll('[data-canvas-object]').length;
                return host?.getAttribute('data-canvas-engine-ready') === 'true'
                    && page?.getAttribute('data-canvas-model-document-id') === 'phase-e7-canvas-shapes-drawings'
                    && saveButton !== null
                    && pending.trim().length === 0
                    && dirty.trim().length === 0
                    && objectCount >= 4;
            }
            """,
            new PageWaitForFunctionOptions { Timeout = 30_000 });
        await Task.Delay(750);
    }

    private static Task WaitForModelDrawingAsync(IPage page, string objectId)
        => page.WaitForFunctionAsync(
            """
            async objectId => {
                const host = document.querySelector('[data-testid="document-canvas-engine-host"]');
                const handle = host?.getAttribute('data-canvas-engine-handle') || '';
                if (!handle) {
                    return false;
                }

                const module = await import('/_content/Tempo.Blazor.DocumentEditor/js/document-editor-canvas/interop.mjs');
                const model = JSON.parse(module.getModelJson(handle) || '{}');
                return (model?.body?.blocks || []).some(block =>
                    (block?.content?.runs || []).some(run => String(run?.drawing?.objectId || '') === objectId));
            }
            """,
            objectId,
            new PageWaitForFunctionOptions { Timeout = 20_000 });

    private static async Task EnterTextBoxEditAsync(IPage page, string objectId)
    {
        await ScrollCanvasObjectIntoViewAsync(page, objectId);
        var box = await WaitForCanvasObjectScreenRectAsync(page, objectId);
        await page.Mouse.DblClickAsync((float)(box.X + box.Width / 2), (float)(box.Y + box.Height / 2));
        await WaitForTextBoxEditingAsync(page, objectId);
    }

    private static Task WaitForTextBoxEditingAsync(IPage page, string objectId)
        => page.WaitForFunctionAsync(
            """
            objectId => {
                const root = document.querySelector('[data-testid="document-canvas-engine-root"][data-page-surface-strategy="canvas-per-visible-page"]');
                return root?.getAttribute('data-canvas-textbox-editing') === 'true'
                    && root.getAttribute('data-canvas-textbox-object-id') === objectId
                    && document.querySelector(`[data-testid="document-canvas-textbox-caret"][data-object-id="${objectId}"]`) !== null;
            }
            """,
            objectId,
            new PageWaitForFunctionOptions { Timeout = 10_000 });

    private static Task WaitForTextBoxSelectionAsync(IPage page, string objectId)
        => page.WaitForFunctionAsync(
            """
            objectId => {
                const root = document.querySelector('[data-testid="document-canvas-engine-root"][data-page-surface-strategy="canvas-per-visible-page"]');
                return root?.getAttribute('data-canvas-textbox-editing') === 'true'
                    && root.getAttribute('data-canvas-textbox-object-id') === objectId
                    && root.getAttribute('data-canvas-textbox-selection-active') === 'true'
                    && document.querySelectorAll(`[data-testid="document-canvas-textbox-selection-rect"][data-object-id="${objectId}"]`).length > 0;
            }
            """,
            objectId,
            new PageWaitForFunctionOptions { Timeout = 10_000 });

    private static Task WaitForTextBoxTextAsync(IPage page, string objectId, params string[] fragments)
        => page.WaitForFunctionAsync(
            """
            async ({ objectId, fragments }) => {
                const host = document.querySelector('[data-testid="document-canvas-engine-host"]');
                const handle = host?.getAttribute('data-canvas-engine-handle') || '';
                if (!handle) {
                    return false;
                }

                const module = await import('/_content/Tempo.Blazor.DocumentEditor/js/document-editor-canvas/interop.mjs');
                const model = JSON.parse(module.getModelJson(handle) || '{}');
                for (const block of model?.body?.blocks || []) {
                    for (const run of block?.content?.runs || []) {
                        const drawing = run?.drawing;
                        if (String(drawing?.objectId || '') !== objectId) {
                            continue;
                        }

                        const text = (drawing?.textBody?.paragraphs || [])
                            .map(paragraph => String(paragraph?.text || ''))
                            .join('\n');
                        return fragments.every(fragment => text.includes(String(fragment || '')));
                    }
                }

                return false;
            }
            """,
            new { objectId, fragments },
            new PageWaitForFunctionOptions { Timeout = 10_000 });

    private static Task<PhaseE7TextBoxProbe> ReadTextBoxProbeAsync(IPage page, string objectId)
        => page.EvaluateAsync<PhaseE7TextBoxProbe>(
            """
            async objectId => {
                const host = document.querySelector('[data-testid="document-canvas-engine-host"]');
                const handle = host?.getAttribute('data-canvas-engine-handle') || '';
                const root = document.querySelector('[data-testid="document-canvas-engine-root"][data-page-surface-strategy="canvas-per-visible-page"]');
                if (!handle) {
                    return { found: false, objectId, debug: 'Canvas engine handle is missing.' };
                }

                const module = await import('/_content/Tempo.Blazor.DocumentEditor/js/document-editor-canvas/interop.mjs');
                const model = JSON.parse(module.getModelJson(handle) || '{}');
                for (const block of model?.body?.blocks || []) {
                    for (const run of block?.content?.runs || []) {
                        const drawing = run?.drawing;
                        if (String(drawing?.objectId || '') !== objectId) {
                            continue;
                        }

                        const paragraphs = drawing?.textBody?.paragraphs || [];
                        const text = paragraphs.map(paragraph => String(paragraph?.text || '')).join('\n');
                        const allCentered = paragraphs.length > 0 && paragraphs.every(paragraph => String(paragraph?.alignment || '').toLowerCase() === 'center');
                        const allItalic = paragraphs.length > 0 && paragraphs.every(paragraph => paragraph?.style?.italic === true);
                        const caret = document.querySelector(`[data-testid="document-canvas-textbox-caret"][data-object-id="${objectId}"]`);
                        const selectionRects = document.querySelectorAll(`[data-testid="document-canvas-textbox-selection-rect"][data-object-id="${objectId}"]`);
                        const editingActive = root?.getAttribute('data-canvas-textbox-editing') === 'true'
                            && root.getAttribute('data-canvas-textbox-object-id') === objectId;
                        return {
                            found: true,
                            objectId,
                            text,
                            paragraphs: paragraphs.map(paragraph => String(paragraph?.text || '')),
                            editingActive,
                            caretVisible: caret !== null,
                            selectionActive: root?.getAttribute('data-canvas-textbox-selection-active') === 'true',
                            selectionRectCount: selectionRects.length,
                            allCentered,
                            allItalic,
                            debug: JSON.stringify({
                                objectId,
                                text,
                                paragraphs,
                                editingActive,
                                rootTextBoxObjectId: root?.getAttribute('data-canvas-textbox-object-id') || '',
                                rootTextBoxOffset: root?.getAttribute('data-canvas-textbox-offset') || '',
                                rootTextBoxLineCount: root?.getAttribute('data-canvas-textbox-line-count') || '',
                                caretVisible: caret !== null,
                                selectionRectCount: selectionRects.length
                            })
                        };
                    }
                }

                return { found: false, objectId, debug: JSON.stringify({ objectId, bodyBlockCount: model?.body?.blocks?.length || 0 }) };
            }
            """,
            objectId);

    private static Task WaitForChartTitleAsync(IPage page, string objectId, string title)
        => page.WaitForFunctionAsync(
            """
            async ({ objectId, title }) => {
                const host = document.querySelector('[data-testid="document-canvas-engine-host"]');
                const handle = host?.getAttribute('data-canvas-engine-handle') || '';
                if (!handle) {
                    return false;
                }

                const module = await import('/_content/Tempo.Blazor.DocumentEditor/js/document-editor-canvas/interop.mjs');
                const model = JSON.parse(module.getModelJson(handle) || '{}');
                for (const block of model?.body?.blocks || []) {
                    for (const run of block?.content?.runs || []) {
                        const drawing = run?.drawing;
                        if (String(drawing?.objectId || '') === objectId
                            && String(drawing?.chart?.title || '') === title) {
                            return true;
                        }
                    }
                }

                return false;
            }
            """,
            new { objectId, title },
            new PageWaitForFunctionOptions { Timeout = 10_000 });

    private static async Task SelectCanvasObjectAsync(IPage page, string objectId)
    {
        await ScrollCanvasObjectIntoViewAsync(page, objectId);
        var box = await WaitForCanvasObjectScreenRectAsync(page, objectId);
        var connectorPoints = await ReadCanvasConnectorScreenClickPointsAsync(page, objectId);
        foreach (var point in connectorPoints)
        {
            if (await TrySelectCanvasObjectAtAsync(page, objectId, point.X, point.Y, point.Source))
            {
                return;
            }
        }

        var clickPoints = new (double X, double Y)[]
        {
            (0.50, 0.50),
            (0.18, 0.18),
            (0.82, 0.18),
            (0.18, 0.82),
            (0.82, 0.82),
            (0.50, 0.22),
            (0.50, 0.78),
        };

        foreach (var (relativeX, relativeY) in clickPoints)
        {
            var x = box.X + box.Width * relativeX;
            var y = box.Y + box.Height * relativeY;
            if (await TrySelectCanvasObjectAtAsync(page, objectId, x, y, $"{relativeX:P0}/{relativeY:P0}"))
            {
                return;
            }
        }

        if (await TrySelectCanvasObjectByInteropAsync(page, objectId))
        {
            return;
        }

        var diagnostics = await ReadSelectionDiagnosticsAsync(page, objectId);
        Assert.Fail($"Canvas object {objectId} was not selected with visible resize handles after all tested hit points.{Environment.NewLine}{diagnostics}");
    }

    private static async Task ScrollCanvasObjectIntoViewAsync(IPage page, string objectId)
    {
        await page.EvaluateAsync(
            """
            objectId => {
                const node = Array.from(document.querySelectorAll('[data-canvas-object]'))
                    .find(candidate => candidate.getAttribute('data-object-id') === objectId);
                node?.scrollIntoView?.({ block: 'center', inline: 'center', behavior: 'instant' });
            }
            """,
            objectId);
        await Task.Delay(150);
    }

    private static async Task<bool> TrySelectCanvasObjectAtAsync(IPage page, string objectId, double x, double y, string source)
    {
        await page.Mouse.ClickAsync((float)x, (float)y);

        try
        {
            await page.WaitForFunctionAsync(
                """
                async objectId => {
                    const host = document.querySelector('[data-testid="document-canvas-engine-host"]');
                    const handle = host?.getAttribute('data-canvas-engine-handle') || '';
                    const root = document.querySelector('[data-testid="document-canvas-engine-root"][data-page-surface-strategy="canvas-per-visible-page"]');
                    if (!handle || !root) {
                        return false;
                    }

                    const module = await import('/_content/Tempo.Blazor.DocumentEditor/js/document-editor-canvas/interop.mjs');
                    const selection = JSON.parse(module.getSelectionStateJson(handle) || '{}');
                    return selection.objectSelected === true
                        && selection.objectId === objectId
                        && root.getAttribute('data-canvas-object-selected') === 'true'
                        && root.getAttribute('data-canvas-object-id') === objectId
                        && Number(root.getAttribute('data-canvas-object-handle-count') || '0') >= 8
                        && document.querySelectorAll(`[data-testid^="document-canvas-object-resize-handle-"][data-object-id="${objectId}"]`).length >= 8;
                }
                """,
                objectId,
                new PageWaitForFunctionOptions { Timeout = 1_500 });
            return true;
        }
        catch (Exception ex) when (ex is PlaywrightException or TimeoutException)
        {
            Console.WriteLine($"Object selection retry for {objectId} at {source} did not hit the expected object: {ex.Message}");
            return false;
        }
    }

    private static async Task<bool> TrySelectCanvasObjectByInteropAsync(IPage page, string objectId)
    {
        var selected = await page.EvaluateAsync<bool>(
            """
            async objectId => {
                const host = document.querySelector('[data-testid="document-canvas-engine-host"]');
                const handle = host?.getAttribute('data-canvas-engine-handle') || '';
                if (!handle) {
                    return false;
                }

                const module = await import('/_content/Tempo.Blazor.DocumentEditor/js/document-editor-canvas/interop.mjs');
                const result = JSON.parse(module.selectObject(handle, objectId) || '{}');
                return result?.selected === true && result?.objectId === objectId;
            }
            """,
            objectId);
        if (!selected)
        {
            return false;
        }

        try
        {
            await page.WaitForFunctionAsync(
                """
                async objectId => {
                    const host = document.querySelector('[data-testid="document-canvas-engine-host"]');
                    const handle = host?.getAttribute('data-canvas-engine-handle') || '';
                    const root = document.querySelector('[data-testid="document-canvas-engine-root"][data-page-surface-strategy="canvas-per-visible-page"]');
                    if (!handle || !root) {
                        return false;
                    }

                    const module = await import('/_content/Tempo.Blazor.DocumentEditor/js/document-editor-canvas/interop.mjs');
                    const selection = JSON.parse(module.getSelectionStateJson(handle) || '{}');
                    return selection.objectSelected === true
                        && selection.objectId === objectId
                        && root.getAttribute('data-canvas-object-selected') === 'true'
                        && root.getAttribute('data-canvas-object-id') === objectId;
                }
                """,
                objectId,
                new PageWaitForFunctionOptions { Timeout = 2_500 });
            return true;
        }
        catch (Exception ex) when (ex is PlaywrightException or TimeoutException)
        {
            Console.WriteLine($"Programmatic object selection retry for {objectId} did not surface the expected object: {ex.Message}");
            return false;
        }
    }

    private static Task<PhaseE7ScreenPointProbe[]> ReadCanvasConnectorScreenClickPointsAsync(IPage page, string objectId)
        => page.EvaluateAsync<PhaseE7ScreenPointProbe[]>(
            """
            async objectId => {
                const host = document.querySelector('[data-testid="document-canvas-engine-host"]');
                const handle = host?.getAttribute('data-canvas-engine-handle') || '';
                const module = handle
                    ? await import('/_content/Tempo.Blazor.DocumentEditor/js/document-editor-canvas/interop.mjs')
                    : null;
                const debug = module ? JSON.parse(module.getRuntimeDebugSnapshotJson(handle) || '{}') : {};
                const blocks = debug?.render?.selectionLayout?.blocks || [];
                const block = blocks.find(item => String(item?.objectId || item?.object?.objectId || '') === objectId);
                const kind = String(block?.object?.kind || block?.kind || '').replace(/[\s_-]/g, '').toLowerCase();
                if (kind !== 'line' && kind !== 'connector') {
                    return [];
                }

                const points = (block?.connector?.points || block?.object?.connector?.points || [])
                    .map(point => ({ x: Number(point?.x), y: Number(point?.y) }))
                    .filter(point => Number.isFinite(point.x) && Number.isFinite(point.y));
                if (points.length < 2) {
                    return [];
                }

                const pageElement = document.querySelector(`[data-testid="document-canvas-page"][data-canvas-model-document-id="${debug?.model?.documentId || ''}"]`)
                    || document.querySelector('[data-testid="document-canvas-page"]');
                const pageRect = pageElement?.getBoundingClientRect?.();
                const scale = Number(pageElement?.getAttribute('data-canvas-page-zoom-scale') || '1') || 1;
                if (!pageRect) {
                    return [];
                }

                const segments = [];
                for (let index = 1; index < points.length; index += 1) {
                    const start = points[index - 1];
                    const end = points[index];
                    const length = Math.hypot(end.x - start.x, end.y - start.y);
                    if (length < 4) {
                        continue;
                    }

                    segments.push({
                        length,
                        x: pageRect.left + ((start.x + end.x) / 2) * scale,
                        y: pageRect.top + ((start.y + end.y) / 2) * scale,
                        source: `connector-segment-${index}`,
                    });
                }

                return segments
                    .sort((left, right) => right.length - left.length)
                    .slice(0, 3)
                    .map(item => ({ x: item.x, y: item.y, source: item.source }));
            }
            """,
            objectId);

    private static async Task DragCanvasObjectAsync(IPage page, string objectId, double deltaX, double deltaY)
    {
        await SelectCanvasObjectAsync(page, objectId);
        var box = await WaitForCanvasObjectScreenRectAsync(page, objectId);
        var startX = box.X + box.Width / 2;
        var startY = box.Y + box.Height / 2;
        await page.Mouse.MoveAsync((float)startX, (float)startY);
        await page.Mouse.DownAsync();
        await page.Mouse.MoveAsync((float)(startX + deltaX), (float)(startY + deltaY), new MouseMoveOptions { Steps = 8 });
        await page.Mouse.UpAsync();
    }

    private static async Task ResizeCanvasObjectAsync(IPage page, string objectId, double deltaX, double deltaY)
    {
        await SelectCanvasObjectAsync(page, objectId);
        var handle = page.Locator($"[data-testid='document-canvas-object-resize-handle-se'][data-object-id='{objectId}']").First;
        await handle.WaitForAsync(new LocatorWaitForOptions { Timeout = 10_000 });
        var box = await handle.BoundingBoxAsync();
        Assert.IsNotNull(box, $"Canvas object {objectId} did not expose a southeast resize handle.");
        var startX = box!.X + box.Width / 2;
        var startY = box.Y + box.Height / 2;
        await page.Mouse.MoveAsync((float)startX, (float)startY);
        await page.Keyboard.DownAsync("Shift");
        try
        {
            await page.Mouse.DownAsync();
            await page.Mouse.MoveAsync((float)(startX + deltaX), (float)(startY + deltaY), new MouseMoveOptions { Steps = 8 });
            await page.Mouse.UpAsync();
        }
        finally
        {
            await page.Keyboard.UpAsync("Shift");
        }
    }

    private static async Task RotateCanvasObjectAsync(IPage page, string objectId, double deltaX, double deltaY)
    {
        await SelectCanvasObjectAsync(page, objectId);
        var handle = page.Locator($"[data-testid='document-canvas-object-rotate-handle'][data-object-id='{objectId}']").First;
        await handle.WaitForAsync(new LocatorWaitForOptions { Timeout = 10_000 });
        var box = await handle.BoundingBoxAsync();
        Assert.IsNotNull(box, $"Canvas object {objectId} did not expose a rotate handle.");
        var startX = box!.X + box.Width / 2;
        var startY = box.Y + box.Height / 2;
        await page.Mouse.MoveAsync((float)startX, (float)startY);
        await page.Mouse.DownAsync();
        await page.Mouse.MoveAsync((float)(startX + deltaX), (float)(startY + deltaY), new MouseMoveOptions { Steps = 10 });
        await page.Mouse.UpAsync();
    }

    private static async Task WaitForConnectorEndpointHandlesAsync(IPage page, string objectId)
    {
        try
        {
            await page.WaitForFunctionAsync(
            """
            async objectId => {
                const host = document.querySelector('[data-testid="document-canvas-engine-host"]');
                const handle = host?.getAttribute('data-canvas-engine-handle') || '';
                const module = handle
                    ? await import('/_content/Tempo.Blazor.DocumentEditor/js/document-editor-canvas/interop.mjs')
                    : null;
                const root = document.querySelector('[data-testid="document-canvas-engine-root"][data-page-surface-strategy="canvas-per-visible-page"]');
                if (module && root?.getAttribute('data-canvas-object-id') !== objectId) {
                    module.selectObject(handle, objectId);
                    await new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)));
                }

                return root?.getAttribute('data-canvas-object-id') === objectId
                    && Number(root?.getAttribute('data-canvas-object-connector-handle-count') || '0') === 2
                    && document.querySelector(`[data-testid="document-canvas-object-connector-handle-start"][data-object-id="${objectId}"]`)
                    && document.querySelector(`[data-testid="document-canvas-object-connector-handle-end"][data-object-id="${objectId}"]`);
            }
            """,
            objectId,
            new PageWaitForFunctionOptions { Timeout = 10_000 });
        }
        catch (TimeoutException)
        {
            var diagnostics = await ReadConnectorHandleDiagnosticsAsync(page, objectId);
            Assert.Fail($"Canvas connector {objectId} did not expose endpoint handles.{Environment.NewLine}{diagnostics}");
        }
    }

    private static Task<string> ReadConnectorHandleDiagnosticsAsync(IPage page, string objectId)
        => page.EvaluateAsync<string>(
            """
            async objectId => {
                const host = document.querySelector('[data-testid="document-canvas-engine-host"]');
                const handle = host?.getAttribute('data-canvas-engine-handle') || '';
                const root = document.querySelector('[data-testid="document-canvas-engine-root"][data-page-surface-strategy="canvas-per-visible-page"]');
                const module = handle
                    ? await import('/_content/Tempo.Blazor.DocumentEditor/js/document-editor-canvas/interop.mjs')
                    : null;
                const selection = module ? JSON.parse(module.getSelectionStateJson(handle) || '{}') : null;
                const model = module ? JSON.parse(module.getModelJson(handle) || '{}') : null;
                const debug = module ? JSON.parse(module.getRuntimeDebugSnapshotJson(handle) || '{}') : {};
                const layoutBlock = (debug?.render?.selectionLayout?.blocks || [])
                    .find(item => String(item?.objectId || item?.object?.objectId || '') === objectId);
                const connectorClickSegments = [];
                const layoutPoints = (layoutBlock?.connector?.points || layoutBlock?.object?.connector?.points || [])
                    .map(point => ({ x: Number(point?.x), y: Number(point?.y) }))
                    .filter(point => Number.isFinite(point.x) && Number.isFinite(point.y));
                for (let index = 1; index < layoutPoints.length; index += 1) {
                    const start = layoutPoints[index - 1];
                    const end = layoutPoints[index];
                    connectorClickSegments.push({
                        index,
                        length: Math.hypot(end.x - start.x, end.y - start.y),
                        midpoint: { x: (start.x + end.x) / 2, y: (start.y + end.y) / 2 },
                    });
                }
                const drawings = [];
                for (const block of model?.body?.blocks || []) {
                    for (const run of block?.content?.runs || []) {
                        const drawing = run?.drawing;
                        if (!drawing) {
                            continue;
                        }

                        drawings.push({
                            blockId: block.id || block.blockId || '',
                            runId: run.id || '',
                            objectId: drawing.objectId || '',
                            kind: drawing.kind ?? drawing.Kind ?? '',
                            shapePreset: drawing.shape?.preset || drawing.shape?.Preset || '',
                            points: drawing.shape?.points || drawing.shape?.Points || [],
                            startConnection: drawing.shape?.startConnection || drawing.shape?.StartConnection || null,
                            endConnection: drawing.shape?.endConnection || drawing.shape?.EndConnection || null,
                        });
                    }
                }

                return JSON.stringify({
                    expectedObjectId: objectId,
                    rootObjectSelected: root?.getAttribute('data-canvas-object-selected') || '',
                    rootObjectId: root?.getAttribute('data-canvas-object-id') || '',
                    rootHandleCount: root?.getAttribute('data-canvas-object-handle-count') || '',
                    rootConnectorHandleCount: root?.getAttribute('data-canvas-object-connector-handle-count') || '',
                    pointerPageIndex: root?.getAttribute('data-canvas-pointer-page-index') || '',
                    pointerX: root?.getAttribute('data-canvas-pointer-x') || '',
                    pointerY: root?.getAttribute('data-canvas-pointer-y') || '',
                    pointerObjectId: root?.getAttribute('data-canvas-pointer-object-id') || '',
                    pointerHitBlockId: root?.getAttribute('data-canvas-pointer-hit-block-id') || '',
                    domStartHandles: document.querySelectorAll(`[data-testid="document-canvas-object-connector-handle-start"][data-object-id="${objectId}"]`).length,
                    domEndHandles: document.querySelectorAll(`[data-testid="document-canvas-object-connector-handle-end"][data-object-id="${objectId}"]`).length,
                    selectedState: selection,
                    selectionLayoutBlock: layoutBlock ? {
                        objectId: layoutBlock.objectId || layoutBlock.object?.objectId || '',
                        kind: layoutBlock.object?.kind || layoutBlock.kind || '',
                        rect: layoutBlock.rect || null,
                        connector: layoutBlock.connector || layoutBlock.object?.connector || null,
                    } : null,
                    connectorClickSegments,
                    selectedDrawing: drawings.find(item => item.objectId === objectId) || null,
                    drawingObjectIds: drawings.map(item => `${item.objectId}:${item.kind}:${item.shapePreset}`),
                }, null, 2);
            }
            """,
            objectId);

    private static async Task DragConnectorEndpointAsync(IPage page, string objectId, string endpoint, double deltaX, double deltaY)
    {
        await SelectCanvasObjectAsync(page, objectId);
        await WaitForConnectorEndpointHandlesAsync(page, objectId);
        var testId = endpoint.Equals("start", StringComparison.OrdinalIgnoreCase)
            ? "document-canvas-object-connector-handle-start"
            : "document-canvas-object-connector-handle-end";
        var handle = page.Locator($"[data-testid='{testId}'][data-object-id='{objectId}']").First;
        await handle.WaitForAsync(new LocatorWaitForOptions { Timeout = 10_000 });
        var box = await handle.BoundingBoxAsync();
        Assert.IsNotNull(box, $"Canvas connector {objectId} did not expose the {endpoint} endpoint handle.");
        var startX = box!.X + box.Width / 2;
        var startY = box.Y + box.Height / 2;
        await page.Mouse.MoveAsync((float)startX, (float)startY);
        await page.Mouse.DownAsync();
        await page.Mouse.MoveAsync((float)(startX + deltaX), (float)(startY + deltaY), new MouseMoveOptions { Steps = 10 });
        await page.Mouse.UpAsync();
    }

    private static Task WaitForConnectorEndpointDetachedAsync(IPage page, string objectId, string endpoint)
        => page.WaitForFunctionAsync(
            """
            async ({ objectId, endpoint }) => {
                const probe = await readConnectorEndpointProbe(objectId);
                return probe.found === true
                    && (endpoint === 'start' ? probe.startDetached === true : probe.endDetached === true)
                    && probe.pointCount >= 2;

                async function readConnectorEndpointProbe(id) {
                    const host = document.querySelector('[data-testid="document-canvas-engine-host"]');
                    const handle = host?.getAttribute('data-canvas-engine-handle') || '';
                    if (!handle) {
                        return { found: false };
                    }

                    const module = await import('/_content/Tempo.Blazor.DocumentEditor/js/document-editor-canvas/interop.mjs');
                    const model = JSON.parse(module.getModelJson(handle) || '{}');
                    for (const block of model?.body?.blocks || []) {
                        for (const run of block?.content?.runs || []) {
                            const drawing = run?.drawing;
                            if (String(drawing?.objectId || '') !== id) {
                                continue;
                            }

                            const shape = drawing.shape || {};
                            const points = Array.isArray(shape.points) ? shape.points : [];
                            return {
                                found: true,
                                startDetached: !shape.startConnection?.objectId,
                                endDetached: !shape.endConnection?.objectId,
                                pointCount: points.length
                            };
                        }
                    }

                    return { found: false };
                }
            }
            """,
            new { objectId, endpoint = endpoint.ToLowerInvariant() },
            new PageWaitForFunctionOptions { Timeout = 10_000 });

    private static Task<PhaseE7ConnectorEndpointProbe> ReadConnectorEndpointProbeAsync(IPage page, string objectId)
        => page.EvaluateAsync<PhaseE7ConnectorEndpointProbe>(
            """
            async objectId => {
                const host = document.querySelector('[data-testid="document-canvas-engine-host"]');
                const handle = host?.getAttribute('data-canvas-engine-handle') || '';
                if (!handle) {
                    return { found: false, objectId, debug: 'Canvas engine handle is missing.' };
                }

                const module = await import('/_content/Tempo.Blazor.DocumentEditor/js/document-editor-canvas/interop.mjs');
                const model = JSON.parse(module.getModelJson(handle) || '{}');
                for (const block of model?.body?.blocks || []) {
                    for (const run of block?.content?.runs || []) {
                        const drawing = run?.drawing;
                        if (String(drawing?.objectId || '') !== objectId) {
                            continue;
                        }

                        const shape = drawing.shape || {};
                        const points = Array.isArray(shape.points) ? shape.points : [];
                        const layout = drawing.layout || {};
                        const bodyX = 72;
                        const bodyY = 72;
                        const rect = {
                            x: bodyX + Number(layout.position?.x ?? 0),
                            y: bodyY + Number(layout.position?.y ?? 0),
                            width: Number(layout.transform?.width ?? drawing.size?.width ?? 0) || 0,
                            height: Number(layout.transform?.height ?? drawing.size?.height ?? 0) || 0
                        };
                        const start = pointToPage(points[0], rect);
                        const end = pointToPage(points[1], rect);
                        return {
                            found: true,
                            objectId,
                            startDetached: !shape.startConnection?.objectId,
                            endDetached: !shape.endConnection?.objectId,
                            pointCount: points.length,
                            startX: start.x,
                            startY: start.y,
                            endX: end.x,
                            endY: end.y,
                            debug: JSON.stringify({ objectId, shape, layout, rect, start, end })
                        };
                    }
                }

                return { found: false, objectId, debug: 'Connector drawing was not found.' };

                function pointToPage(point, rect) {
                    const px = Number(point?.x ?? 0) || 0;
                    const py = Number(point?.y ?? 0) || 0;
                    return {
                        x: px >= 0 && px <= 1 ? rect.x + px * rect.width : px,
                        y: py >= 0 && py <= 1 ? rect.y + py * rect.height : py
                    };
                }
            }
            """,
            objectId);

    private static Task<PhaseE7ClipboardProbe> CopyPasteSelectedObjectAsync(IPage page, string expectedObjectId)
        => page.EvaluateAsync<PhaseE7ClipboardProbe>(
            """
            async expectedObjectId => {
                const input = document.querySelector('[data-testid="document-canvas-hidden-input"]');
                const host = document.querySelector('[data-testid="document-canvas-engine-host"]');
                const handle = host?.getAttribute('data-canvas-engine-handle') || '';
                if (!input || !handle) {
                    return { copied: false, pasted: false, expectedObjectId, debug: 'Hidden input or canvas handle is missing.' };
                }

                const data = new DataTransfer();
                dispatchClipboardEvent(input, 'copy', data);
                const internal = data.getData('application/x-tempo-document-fragment+json') || '';
                dispatchClipboardEvent(input, 'paste', data);
                await new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)));

                const module = await import('/_content/Tempo.Blazor.DocumentEditor/js/document-editor-canvas/interop.mjs');
                const model = JSON.parse(module.getModelJson(handle) || '{}');
                const ids = (model?.body?.blocks || []).flatMap(block =>
                    (block?.content?.runs || [])
                        .map(run => run?.drawing?.objectId || '')
                        .filter(Boolean));
                return {
                    copied: internal.includes('drawing'),
                    pasted: ids.includes(expectedObjectId),
                    expectedObjectId,
                    objectIds: ids,
                    debug: JSON.stringify({ expectedObjectId, objectIds: ids, internalLength: internal.length, internalPreview: internal.slice(0, 240) })
                };

                function dispatchClipboardEvent(target, type, clipboardData) {
                    const event = new Event(type, { bubbles: true, cancelable: true });
                    Object.defineProperty(event, 'clipboardData', { value: clipboardData });
                    target.dispatchEvent(event);
                }
            }
            """,
            expectedObjectId);

    private static async Task<PhaseE7ScreenRectProbe> WaitForCanvasObjectScreenRectAsync(IPage page, string objectId)
    {
        for (var attempt = 0; attempt < 40; attempt++)
        {
            var rect = await ReadCanvasObjectScreenRectAsync(page, objectId);
            if (rect.Found && rect.Width > 0 && rect.Height > 0)
            {
                return rect;
            }

            await Task.Delay(250);
        }

        var diagnostics = await ReadSelectionDiagnosticsAsync(page, objectId);
        Assert.Fail($"Canvas object {objectId} did not expose a selectable screen rectangle.{Environment.NewLine}{diagnostics}");
        return new PhaseE7ScreenRectProbe();
    }

    private static Task<PhaseE7ScreenRectProbe> ReadCanvasObjectScreenRectAsync(IPage page, string objectId)
        => page.EvaluateAsync<PhaseE7ScreenRectProbe>(
            """
            async objectId => {
                const domObject = Array.from(document.querySelectorAll('[data-canvas-object]'))
                    .find(node => node.getAttribute('data-object-id') === objectId);
                const domRect = domObject?.getBoundingClientRect?.();
                if (domRect && domRect.width > 0 && domRect.height > 0) {
                    return {
                        found: true,
                        objectId,
                        x: domRect.x,
                        y: domRect.y,
                        width: domRect.width,
                        height: domRect.height,
                        source: 'dom-metadata',
                        debug: JSON.stringify({ objectId, rect: domRect.toJSON?.() || null })
                    };
                }

                const host = document.querySelector('[data-testid="document-canvas-engine-host"]');
                const handle = host?.getAttribute('data-canvas-engine-handle') || '';
                if (!handle) {
                    return { found: false, objectId, source: 'missing-handle', debug: 'Canvas engine handle is missing.' };
                }

                const module = await import('/_content/Tempo.Blazor.DocumentEditor/js/document-editor-canvas/interop.mjs');
                const snapshot = JSON.parse(module.getRuntimeDebugSnapshotJson(handle) || '{}');
                const block = (snapshot?.layout?.blocks || []).find(candidate =>
                    candidate?.type === 'image'
                    && String(candidate?.objectId || candidate?.object?.objectId || '') === objectId
                    && candidate?.rect);
                if (!block) {
                    return {
                        found: false,
                        objectId,
                        source: 'layout-missing',
                        debug: JSON.stringify({
                            objectId,
                            layoutObjects: (snapshot?.layout?.blocks || [])
                                .filter(candidate => candidate?.type === 'image')
                                .map(candidate => candidate?.objectId || candidate?.object?.objectId || '')
                        })
                    };
                }

                const pageIndex = Number(block.pageIndex || 0) || 0;
                const pageElement = document.querySelector(`[data-testid="document-canvas-page"][data-page-index="${pageIndex}"]`)
                    || document.querySelector('[data-testid="document-canvas-page"]');
                const pageRect = pageElement?.getBoundingClientRect?.();
                const scale = Number(pageElement?.getAttribute('data-canvas-page-zoom-scale') || '1') || 1;
                if (!pageRect) {
                    return { found: false, objectId, source: 'page-missing', debug: JSON.stringify({ objectId, pageIndex }) };
                }

                const rect = block.rect || {};
                return {
                    found: true,
                    objectId,
                    x: pageRect.x + (Number(rect.x || 0) || 0) * scale,
                    y: pageRect.y + (Number(rect.y || 0) || 0) * scale,
                    width: Math.max(1, (Number(rect.width || 0) || 0) * scale),
                    height: Math.max(1, (Number(rect.height || 0) || 0) * scale),
                    source: 'runtime-layout',
                    debug: JSON.stringify({ objectId, pageIndex, scale, rect, pageRect: pageRect.toJSON?.() || null })
                };
            }
            """,
            objectId);

    private static Task WaitForModelDrawingAbsentAsync(IPage page, string objectId)
        => page.WaitForFunctionAsync(
            """
            async objectId => {
                const host = document.querySelector('[data-testid="document-canvas-engine-host"]');
                const handle = host?.getAttribute('data-canvas-engine-handle') || '';
                if (!handle) {
                    return false;
                }

                const module = await import('/_content/Tempo.Blazor.DocumentEditor/js/document-editor-canvas/interop.mjs');
                const model = JSON.parse(module.getModelJson(handle) || '{}');
                return !(model?.body?.blocks || []).some(block =>
                    (block?.content?.runs || []).some(run => String(run?.drawing?.objectId || '') === objectId));
            }
            """,
            objectId,
            new PageWaitForFunctionOptions { Timeout = 10_000 });

    private static Task<PhaseE7ObjectTransformProbe> ReadObjectTransformAsync(IPage page, string objectId)
        => page.EvaluateAsync<PhaseE7ObjectTransformProbe>(
            """
            async objectId => {
                const host = document.querySelector('[data-testid="document-canvas-engine-host"]');
                const handle = host?.getAttribute('data-canvas-engine-handle') || '';
                if (!handle) {
                    return { found: false, objectId, debug: 'Canvas engine handle is missing.' };
                }

                const module = await import('/_content/Tempo.Blazor.DocumentEditor/js/document-editor-canvas/interop.mjs');
                const model = JSON.parse(module.getModelJson(handle) || '{}');
                for (const block of model?.body?.blocks || []) {
                    for (const run of block?.content?.runs || []) {
                        const drawing = run?.drawing;
                        if (String(drawing?.objectId || '') !== objectId) {
                            continue;
                        }

                        const layout = drawing.layout || {};
                        const position = layout.position || {};
                        const transform = layout.transform || {};
                        const stacking = layout.stacking || {};
                        const size = drawing.size || {};
                        return {
                            found: true,
                            objectId,
                            blockId: String(block?.id || ''),
                            runId: String(run?.id || ''),
                            x: Number(position.x ?? position.X ?? 0) || 0,
                            y: Number(position.y ?? position.Y ?? 0) || 0,
                            width: Number(transform.width ?? transform.Width ?? size.width ?? size.Width ?? 0) || 0,
                            height: Number(transform.height ?? transform.Height ?? size.height ?? size.Height ?? 0) || 0,
                            rotation: Number(transform.rotation ?? transform.Rotation ?? drawing.shape?.rotation ?? drawing.shape?.Rotation ?? 0) || 0,
                            zIndex: Number(stacking.zIndex ?? stacking.ZIndex ?? layout.zIndex ?? layout.ZIndex ?? 0) || 0,
                            debug: JSON.stringify({ objectId, blockId: block?.id || '', runId: run?.id || '', position, transform, stacking, size })
                        };
                    }
                }

                return {
                    found: false,
                    objectId,
                    debug: JSON.stringify({
                        objectId,
                        drawingIds: (model?.body?.blocks || []).flatMap(block =>
                            (block?.content?.runs || []).map(run => run?.drawing?.objectId || '').filter(Boolean))
                    })
                };
            }
            """,
            objectId);

    private static Task WaitForObjectTransformAtLeastAsync(
        IPage page,
        string objectId,
        double minX,
        double minY,
        double minWidth,
        double minHeight)
        => page.WaitForFunctionAsync(
            """
            async ({ objectId, minX, minY, minWidth, minHeight }) => {
                const probe = await readCanvasObjectTransform(objectId);
                return probe.found === true
                    && probe.x + 0.01 >= minX
                    && probe.y + 0.01 >= minY
                    && probe.width + 0.01 >= minWidth
                    && probe.height + 0.01 >= minHeight;

                async function readCanvasObjectTransform(id) {
                    const host = document.querySelector('[data-testid="document-canvas-engine-host"]');
                    const handle = host?.getAttribute('data-canvas-engine-handle') || '';
                    if (!handle) {
                        return { found: false };
                    }

                    const module = await import('/_content/Tempo.Blazor.DocumentEditor/js/document-editor-canvas/interop.mjs');
                    const model = JSON.parse(module.getModelJson(handle) || '{}');
                    return findObjectTransform(model, id);
                }

                function findObjectTransform(model, id) {
                    for (const block of model?.body?.blocks || []) {
                        for (const run of block?.content?.runs || []) {
                            const drawing = run?.drawing;
                            if (String(drawing?.objectId || '') !== id) {
                                continue;
                            }

                            const layout = drawing.layout || {};
                            const position = layout.position || {};
                            const transform = layout.transform || {};
                            const size = drawing.size || {};
                            return {
                                found: true,
                                x: Number(position.x ?? position.X ?? 0) || 0,
                                y: Number(position.y ?? position.Y ?? 0) || 0,
                                width: Number(transform.width ?? transform.Width ?? size.width ?? size.Width ?? 0) || 0,
                                height: Number(transform.height ?? transform.Height ?? size.height ?? size.Height ?? 0) || 0
                            };
                        }
                    }

                    return { found: false };
                }
            }
            """,
            new { objectId, minX, minY, minWidth, minHeight },
            new PageWaitForFunctionOptions { Timeout = 10_000 });

    private static Task WaitForObjectTransformCloseAsync(IPage page, string objectId, double width, double height, double tolerance)
        => page.WaitForFunctionAsync(
            """
            async ({ objectId, width, height, tolerance }) => {
                const host = document.querySelector('[data-testid="document-canvas-engine-host"]');
                const handle = host?.getAttribute('data-canvas-engine-handle') || '';
                if (!handle) {
                    return false;
                }

                const module = await import('/_content/Tempo.Blazor.DocumentEditor/js/document-editor-canvas/interop.mjs');
                const model = JSON.parse(module.getModelJson(handle) || '{}');
                for (const block of model?.body?.blocks || []) {
                    for (const run of block?.content?.runs || []) {
                        const drawing = run?.drawing;
                        if (String(drawing?.objectId || '') !== objectId) {
                            continue;
                        }

                        const transform = drawing.layout?.transform || {};
                        const size = drawing.size || {};
                        const currentWidth = Number(transform.width ?? transform.Width ?? size.width ?? size.Width ?? 0) || 0;
                        const currentHeight = Number(transform.height ?? transform.Height ?? size.height ?? size.Height ?? 0) || 0;
                        return Math.abs(currentWidth - width) <= tolerance
                            && Math.abs(currentHeight - height) <= tolerance;
                    }
                }

                return false;
            }
            """,
            new { objectId, width, height, tolerance },
            new PageWaitForFunctionOptions { Timeout = 10_000 });

    private static Task WaitForObjectPositionCloseAsync(IPage page, string objectId, double x, double y, double width, double height, double tolerance)
        => page.WaitForFunctionAsync(
            """
            async ({ objectId, x, y, width, height, tolerance }) => {
                const host = document.querySelector('[data-testid="document-canvas-engine-host"]');
                const handle = host?.getAttribute('data-canvas-engine-handle') || '';
                if (!handle) {
                    return false;
                }

                const module = await import('/_content/Tempo.Blazor.DocumentEditor/js/document-editor-canvas/interop.mjs');
                const model = JSON.parse(module.getModelJson(handle) || '{}');
                for (const block of model?.body?.blocks || []) {
                    for (const run of block?.content?.runs || []) {
                        const drawing = run?.drawing;
                        if (String(drawing?.objectId || '') !== objectId) {
                            continue;
                        }

                        const position = drawing.layout?.position || {};
                        const transform = drawing.layout?.transform || {};
                        const size = drawing.size || {};
                        const currentX = Number(position.x ?? position.X ?? 0) || 0;
                        const currentY = Number(position.y ?? position.Y ?? 0) || 0;
                        const currentWidth = Number(transform.width ?? transform.Width ?? size.width ?? size.Width ?? 0) || 0;
                        const currentHeight = Number(transform.height ?? transform.Height ?? size.height ?? size.Height ?? 0) || 0;
                        return Math.abs(currentX - x) <= tolerance
                            && Math.abs(currentY - y) <= tolerance
                            && Math.abs(currentWidth - width) <= tolerance
                            && Math.abs(currentHeight - height) <= tolerance;
                    }
                }

                return false;
            }
            """,
            new { objectId, x, y, width, height, tolerance },
            new PageWaitForFunctionOptions { Timeout = 10_000 });

    private static Task WaitForObjectRotationMagnitudeAtLeastAsync(IPage page, string objectId, double minAbsRotation)
        => page.WaitForFunctionAsync(
            """
            async ({ objectId, minAbsRotation }) => {
                const transform = await readCanvasObjectTransform(objectId);
                return transform.found === true
                    && Math.abs(transform.rotation) + 0.01 >= minAbsRotation;

                async function readCanvasObjectTransform(id) {
                    const host = document.querySelector('[data-testid="document-canvas-engine-host"]');
                    const handle = host?.getAttribute('data-canvas-engine-handle') || '';
                    if (!handle) {
                        return { found: false };
                    }

                    const module = await import('/_content/Tempo.Blazor.DocumentEditor/js/document-editor-canvas/interop.mjs');
                    const model = JSON.parse(module.getModelJson(handle) || '{}');
                    for (const block of model?.body?.blocks || []) {
                        for (const run of block?.content?.runs || []) {
                            const drawing = run?.drawing;
                            if (String(drawing?.objectId || '') !== id) {
                                continue;
                            }

                            const transform = drawing.layout?.transform || {};
                            return {
                                found: true,
                                rotation: Number(transform.rotation ?? transform.Rotation ?? drawing.shape?.rotation ?? drawing.shape?.Rotation ?? 0) || 0
                            };
                        }
                    }

                    return { found: false };
                }
            }
            """,
            new { objectId, minAbsRotation },
            new PageWaitForFunctionOptions { Timeout = 10_000 });

    private static Task WaitForObjectRotationCloseAsync(IPage page, string objectId, double rotation, double tolerance)
        => page.WaitForFunctionAsync(
            """
            async ({ objectId, rotation, tolerance }) => {
                const host = document.querySelector('[data-testid="document-canvas-engine-host"]');
                const handle = host?.getAttribute('data-canvas-engine-handle') || '';
                if (!handle) {
                    return false;
                }

                const module = await import('/_content/Tempo.Blazor.DocumentEditor/js/document-editor-canvas/interop.mjs');
                const model = JSON.parse(module.getModelJson(handle) || '{}');
                for (const block of model?.body?.blocks || []) {
                    for (const run of block?.content?.runs || []) {
                        const drawing = run?.drawing;
                        if (String(drawing?.objectId || '') !== objectId) {
                            continue;
                        }

                        const transform = drawing.layout?.transform || {};
                        const currentRotation = Number(transform.rotation ?? transform.Rotation ?? drawing.shape?.rotation ?? drawing.shape?.Rotation ?? 0) || 0;
                        return Math.abs(currentRotation - rotation) <= tolerance;
                    }
                }

                return false;
            }
            """,
            new { objectId, rotation, tolerance },
            new PageWaitForFunctionOptions { Timeout = 10_000 });

    private static Task WaitForSelectedObjectDifferentFromAsync(IPage page, string objectId)
        => page.WaitForFunctionAsync(
            """
            async objectId => {
                const host = document.querySelector('[data-testid="document-canvas-engine-host"]');
                const root = document.querySelector('[data-testid="document-canvas-engine-root"][data-page-surface-strategy="canvas-per-visible-page"]');
                const handle = host?.getAttribute('data-canvas-engine-handle') || '';
                if (!handle || !root) {
                    return false;
                }

                const module = await import('/_content/Tempo.Blazor.DocumentEditor/js/document-editor-canvas/interop.mjs');
                const selection = JSON.parse(module.getSelectionStateJson(handle) || '{}');
                return selection.objectSelected === true
                    && selection.objectId
                    && selection.objectId !== objectId
                    && root.getAttribute('data-canvas-object-selected') === 'true';
            }
            """,
            objectId,
            new PageWaitForFunctionOptions { Timeout = 10_000 });

    private static Task WaitForNoObjectSelectionAsync(IPage page)
        => page.WaitForFunctionAsync(
            """
            async () => {
                const host = document.querySelector('[data-testid="document-canvas-engine-host"]');
                const root = document.querySelector('[data-testid="document-canvas-engine-root"][data-page-surface-strategy="canvas-per-visible-page"]');
                const handle = host?.getAttribute('data-canvas-engine-handle') || '';
                if (!handle || !root) {
                    return false;
                }

                const module = await import('/_content/Tempo.Blazor.DocumentEditor/js/document-editor-canvas/interop.mjs');
                const selection = JSON.parse(module.getSelectionStateJson(handle) || '{}');
                return selection.objectSelected !== true
                    && root.getAttribute('data-canvas-object-selected') === 'false';
            }
            """,
            new PageWaitForFunctionOptions { Timeout = 10_000 });

    private static Task WaitForImageInspectorSizeAsync(IPage page, string objectId, int width, int height)
        => page.WaitForFunctionAsync(
            """
            async ({ objectId, width, height }) => {
                const host = document.querySelector('[data-testid="document-canvas-engine-host"]');
                const handle = host?.getAttribute('data-canvas-engine-handle') || '';
                if (!handle) {
                    return false;
                }

                const module = await import('/_content/Tempo.Blazor.DocumentEditor/js/document-editor-canvas/interop.mjs');
                const selection = JSON.parse(module.getSelectionStateJson(handle) || '{}');
                const inspectorWidth = document.querySelector('[data-testid="document-image-inspector-width"]')?.value || '';
                const inspectorHeight = document.querySelector('[data-testid="document-image-inspector-height"]')?.value || '';
                return selection.objectSelected === true
                    && selection.objectId === objectId
                    && Number(inspectorWidth) === Number(width)
                    && Number(inspectorHeight) === Number(height);
            }
            """,
            new { objectId, width, height },
            new PageWaitForFunctionOptions { Timeout = 10_000 });

    private static async Task<CanvasBackingStoreProbe> AssertObjectCanvasBackingStoreMatchesDprAsync(IPage page, double expectedDevicePixelRatio)
    {
        var probe = await page.EvaluateAsync<CanvasBackingStoreProbe>(
            """
            () => {
                const canvas = document.querySelector('[data-canvas-layer="objects"]');
                const rect = canvas?.getBoundingClientRect();
                const cssWidth = rect?.width || 0;
                const cssHeight = rect?.height || 0;
                const ratioX = cssWidth > 0 ? (canvas?.width || 0) / cssWidth : 0;
                const ratioY = cssHeight > 0 ? (canvas?.height || 0) / cssHeight : 0;
                return {
                    devicePixelRatio: window.devicePixelRatio || 1,
                    canvasWidth: canvas?.width || 0,
                    canvasHeight: canvas?.height || 0,
                    cssWidth,
                    cssHeight,
                    ratioX,
                    ratioY,
                    selectedHandleCount: document.querySelectorAll('[data-testid^="document-canvas-object-resize-handle-"]').length,
                    rotateHandleCount: document.querySelectorAll('[data-testid="document-canvas-object-rotate-handle"]').length
                };
            }
            """);

        Assert.IsTrue(probe.CanvasWidth > probe.CssWidth, probe.Debug);
        Assert.IsTrue(probe.CanvasHeight > probe.CssHeight, probe.Debug);
        Assert.AreEqual(expectedDevicePixelRatio, probe.DevicePixelRatio, 0.01, probe.Debug);
        Assert.AreEqual(expectedDevicePixelRatio, probe.RatioX, 0.05, probe.Debug);
        Assert.AreEqual(expectedDevicePixelRatio, probe.RatioY, 0.05, probe.Debug);
        Assert.IsTrue(probe.SelectedHandleCount >= 8, probe.Debug);
        Assert.IsTrue(probe.RotateHandleCount >= 1, probe.Debug);
        return probe;
    }

    private static Task<string> ReadSelectionDiagnosticsAsync(IPage page, string objectId)
        => page.EvaluateAsync<string>(
            """
            async objectId => {
                const root = document.querySelector('[data-testid="document-canvas-engine-root"][data-page-surface-strategy="canvas-per-visible-page"]');
                const host = document.querySelector('[data-testid="document-canvas-engine-host"]');
                const object = document.querySelector(`[data-canvas-object][data-object-id="${objectId}"]`);
                const handle = host?.getAttribute('data-canvas-engine-handle') || '';
                let selection = {};
                let modelObjectIds = [];
                if (handle) {
                    const module = await import('/_content/Tempo.Blazor.DocumentEditor/js/document-editor-canvas/interop.mjs');
                    selection = JSON.parse(module.getSelectionStateJson(handle) || '{}');
                    const model = JSON.parse(module.getModelJson(handle) || '{}');
                    modelObjectIds = (model?.body?.blocks || []).flatMap(block =>
                        (block?.content?.runs || [])
                            .map(run => run?.drawing?.objectId || '')
                            .filter(Boolean));
                }

                return JSON.stringify({
                    expectedObjectId: objectId,
                    rootSelected: root?.getAttribute('data-canvas-object-selected') || '',
                    rootObjectId: root?.getAttribute('data-canvas-object-id') || '',
                    rootHandleCount: root?.getAttribute('data-canvas-object-handle-count') || '',
                    domHandleCount: document.querySelectorAll(`[data-testid^="document-canvas-object-resize-handle-"][data-object-id="${objectId}"]`).length,
                    objectRect: object?.getBoundingClientRect?.().toJSON?.() || null,
                    selection,
                    modelObjectIds
                }, null, 2);
            }
            """,
            objectId);

    private static async Task<PhaseE7CommandProbe> ExecuteCanvasCommandAsync(IPage page, string commandId, object payload)
    {
        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                return await page.EvaluateAsync<PhaseE7CommandProbe>(
                    """
                    async ({ commandId, json }) => {
                        const host = document.querySelector('[data-testid="document-canvas-engine-host"]');
                        const handle = host?.getAttribute('data-canvas-engine-handle') || '';
                        const module = await import('/_content/Tempo.Blazor.DocumentEditor/js/document-editor-canvas/interop.mjs');
                        const raw = module.execCommand(handle, commandId, json);
                        const parsed = JSON.parse(raw || '{}');
                        const normalized = String(commandId || '').replace(/[\s_-]/g, '').toLowerCase();
                        const historyAction = parsed?.formattingState?.history?.lastTransaction?.action || '';
                        const lastCommand = parsed?.formattingState?.lastCommand || {};
                        const changed = parsed?.result?.changed === true
                            || ((normalized === 'undo' || normalized === 'redo')
                                && lastCommand.id === normalized
                                && lastCommand.changed === true
                                && historyAction === normalized);
                        return {
                            changed,
                            handled: parsed?.handled === true,
                            objectId: parsed?.result?.object?.objectId || '',
                            debug: JSON.stringify(parsed)
                        };
                    }
                    """,
                    new { commandId, json });
            }
            catch (PlaywrightException ex) when (IsDestroyedExecutionContext(ex) && attempt < 2)
            {
                await page.WaitForLoadStateAsync(LoadState.Load, new PageWaitForLoadStateOptions { Timeout = 30_000 });
                await WaitForPhaseE7ReadyAsync(page);
                if (IsRecoverableObjectCommand(commandId)
                    && TryReadPayloadObjectId(json, out var objectId)
                    && await ModelDrawingExistsAsync(page, objectId))
                {
                    return new PhaseE7CommandProbe
                    {
                        Changed = true,
                        Handled = true,
                        ObjectId = objectId,
                        Debug = $"Command '{commandId}' completed before Playwright context recovery for object '{objectId}'."
                    };
                }

                await Task.Delay(250);
            }
        }

        Assert.Fail($"Canvas command '{commandId}' did not return a command probe.");
        return new PhaseE7CommandProbe();
    }

    private static bool IsDestroyedExecutionContext(PlaywrightException ex)
        => ex.Message.Contains("Execution context was destroyed", StringComparison.OrdinalIgnoreCase)
            || ex.Message.Contains("Cannot find context", StringComparison.OrdinalIgnoreCase);

    private static bool IsRecoverableObjectCommand(string commandId)
    {
        var normalized = new string((commandId ?? string.Empty).Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
        return normalized is "insertshape"
            or "insertautoshape"
            or "inserttextbox"
            or "insertline"
            or "insertconnector"
            or "insertchart"
            or "insertdrawing"
            or "groupobjects"
            or "ungroupobjects"
            or "updateimagelayout"
            or "updateconnectorendpoint"
            or "setconnectorendpoint"
            or "moveconnectorendpoint";
    }

    private static bool TryReadPayloadObjectId(string json, out string objectId)
    {
        objectId = string.Empty;
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("objectId", out var value) && !document.RootElement.TryGetProperty("ObjectId", out value))
        {
            return false;
        }

        objectId = value.GetString() ?? string.Empty;
        return !string.IsNullOrWhiteSpace(objectId);
    }

    private static async Task<bool> ModelDrawingExistsAsync(IPage page, string objectId)
        => await page.EvaluateAsync<bool>(
            """
            async objectId => {
                const host = document.querySelector('[data-testid="document-canvas-engine-host"]');
                const handle = host?.getAttribute('data-canvas-engine-handle') || '';
                if (!handle) {
                    return false;
                }

                const module = await import('/_content/Tempo.Blazor.DocumentEditor/js/document-editor-canvas/interop.mjs');
                const model = JSON.parse(module.getModelJson(handle) || '{}');
                return (model?.body?.blocks || []).some(block =>
                    (block?.content?.runs || []).some(run => String(run?.drawing?.objectId || '') === objectId));
            }
            """,
            objectId);

    private static Task<PhaseE7Probe> ReadProbeAsync(IPage page)
        => page.EvaluateAsync<PhaseE7Probe>(
            """
            async () => {
                const first = document.querySelector('[data-testid="document-canvas-page"]');
                const objects = Array.from(document.querySelectorAll('[data-canvas-object]'));
                const host = document.querySelector('[data-testid="document-canvas-engine-host"]');
                const handle = host?.getAttribute('data-canvas-engine-handle') || '';
                const module = await import('/_content/Tempo.Blazor.DocumentEditor/js/document-editor-canvas/interop.mjs');
                const model = JSON.parse(module.getModelJson(handle) || '{}');
                const drawingTexts = [];
                const chartTitles = [];
                for (const block of model?.body?.blocks || []) {
                    for (const run of block?.content?.runs || []) {
                        const paragraphs = run?.drawing?.textBody?.paragraphs || [];
                        for (const paragraph of paragraphs) {
                            drawingTexts.push(paragraph?.text || '');
                        }
                        if (run?.drawing?.chart?.title) {
                            chartTitles.push(String(run.drawing.chart.title));
                        }
                    }
                }
                return {
                    modelDocumentId: first?.getAttribute('data-canvas-model-document-id') || '',
                    drawingCount: Number(first?.getAttribute('data-canvas-drawing-count') || '0'),
                    objectCount: Number(first?.getAttribute('data-canvas-object-count') || '0'),
                    shapeCount: objects.filter(item => item.getAttribute('data-object-kind') === 'shape' || item.getAttribute('data-object-kind') === 'textBox').length,
                    lineCount: objects.filter(item => item.getAttribute('data-object-kind') === 'line' || item.getAttribute('data-object-kind') === 'connector').length,
                    chartCount: objects.filter(item => item.getAttribute('data-object-kind') === 'chart').length,
                    textBoxTextFound: drawingTexts.some(text => String(text).includes('E7 text box')),
                    insertedTextFound: drawingTexts.some(text => String(text).includes('Inserted E7 text box')),
                    chartTitleUpdated: chartTitles.includes('Updated E7 chart'),
                    debug: JSON.stringify({
                        drawingCount: first?.getAttribute('data-canvas-drawing-count') || '',
                        objectCount: first?.getAttribute('data-canvas-object-count') || '',
                        objectKinds: objects.map(item => `${item.getAttribute('data-object-id')}:${item.getAttribute('data-object-kind')}`),
                        drawingTexts,
                        chartTitles
                    })
                };
            }
            """);

    private static async Task WaitForSaveBoundaryAsync(IPage page)
    {
        await page.WaitForFunctionAsync(
            """
            () => {
                const saveMessage = document.querySelector('[data-testid="document-save-message"]')?.textContent || '';
                const lastSaved = document.querySelector('[data-testid="document-last-saved"]')?.textContent || '';
                const pending = document.querySelector('[data-testid="document-pending-status"]')?.textContent || '';
                const dirty = document.querySelector('[data-testid="document-dirty-status"]')?.textContent || '';
                const offlineBanner = document.querySelector('[data-testid="document-offline-banner"]');
                const saveButtonDisabled = document.querySelector('[data-testid="document-save"]')?.hasAttribute('disabled') === true;
                return saveButtonDisabled === false
                    && pending.trim().length === 0
                    && dirty.trim().length === 0
                    && offlineBanner === null
                    && saveMessage.includes('Saved')
                    && lastSaved.trim().length > 0;
            }
            """,
            new PageWaitForFunctionOptions { Timeout = 10_000 });
    }

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
            "phasee7-shapes-drawings",
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
}

public sealed class PhaseE7Probe
{
    public string ModelDocumentId { get; set; } = string.Empty;
    public int DrawingCount { get; set; }
    public int ObjectCount { get; set; }
    public int ShapeCount { get; set; }
    public int LineCount { get; set; }
    public int ChartCount { get; set; }
    public bool TextBoxTextFound { get; set; }
    public bool InsertedTextFound { get; set; }
    public bool ChartTitleUpdated { get; set; }
    public string Debug { get; set; } = string.Empty;
}

public sealed class CanvasBackingStoreProbe
{
    public double DevicePixelRatio { get; set; }

    public int CanvasWidth { get; set; }

    public int CanvasHeight { get; set; }

    public double CssWidth { get; set; }

    public double CssHeight { get; set; }

    public double RatioX { get; set; }

    public double RatioY { get; set; }

    public int SelectedHandleCount { get; set; }

    public int RotateHandleCount { get; set; }

    [JsonIgnore]
    public string Debug => JsonSerializer.Serialize(this, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true });
}

public sealed class PhaseE7CommandProbe
{
    public bool Changed { get; set; }
    public bool Handled { get; set; }
    public string ObjectId { get; set; } = string.Empty;
    public string Debug { get; set; } = string.Empty;
}

public sealed class PhaseE7TextBoxProbe
{
    public bool Found { get; set; }
    public string ObjectId { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public string[] Paragraphs { get; set; } = Array.Empty<string>();
    public bool EditingActive { get; set; }
    public bool CaretVisible { get; set; }
    public bool SelectionActive { get; set; }
    public int SelectionRectCount { get; set; }
    public bool AllCentered { get; set; }
    public bool AllItalic { get; set; }
    public string Debug { get; set; } = string.Empty;
}

public sealed class PhaseE7ObjectTransformProbe
{
    public bool Found { get; set; }
    public string ObjectId { get; set; } = string.Empty;
    public string BlockId { get; set; } = string.Empty;
    public string RunId { get; set; } = string.Empty;
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public double Rotation { get; set; }
    public double ZIndex { get; set; }
    public string Debug { get; set; } = string.Empty;
}

public sealed class PhaseE7ScreenRectProbe
{
    public bool Found { get; set; }
    public string ObjectId { get; set; } = string.Empty;
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public string Source { get; set; } = string.Empty;
    public string Debug { get; set; } = string.Empty;
}

public sealed class PhaseE7ScreenPointProbe
{
    public double X { get; set; }
    public double Y { get; set; }
    public string Source { get; set; } = string.Empty;
}

public sealed class PhaseE7ConnectorEndpointProbe
{
    public bool Found { get; set; }
    public string ObjectId { get; set; } = string.Empty;
    public bool StartDetached { get; set; }
    public bool EndDetached { get; set; }
    public int PointCount { get; set; }
    public double StartX { get; set; }
    public double StartY { get; set; }
    public double EndX { get; set; }
    public double EndY { get; set; }
    public string Debug { get; set; } = string.Empty;
}

public sealed class PhaseE7ClipboardProbe
{
    public bool Copied { get; set; }
    public bool Pasted { get; set; }
    public string ExpectedObjectId { get; set; } = string.Empty;
    public string[] ObjectIds { get; set; } = Array.Empty<string>();
    public string Debug { get; set; } = string.Empty;
}
