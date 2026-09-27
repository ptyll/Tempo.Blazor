using System.Text.Json;
using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Tempo.Blazor.E2E.CanvasEngine;

namespace Tempo.Blazor.E2E;

/// <summary>Phase 15 E2E coverage for canvas image and drawing objects.</summary>
[TestClass]
[TestCategory("DocumentEditor")]
[TestCategory("DocumentEditor:CanvasEngine")]
[DoNotParallelize]
public sealed class DocumentEditorCanvasImageE2ETests : WasmTestBase
{
    private const string Phase15DocumentId = "phase-15-canvas-images";
    private const string TinyPngDataUrl = "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAFgwJ/l7W5jwAAAABJRU5ErkJggg==";
    private const double ImageSnapGrid = 8d;

    /// <summary>Restores the canonical phase 15 seed before each persistent canvas image run.</summary>
    [TestInitialize]
    public Task ResetDocumentEditorDemoAsync()
        => DocumentEditorE2EReset.ResetAsync();

    [TestMethod]
    public async Task Phase15_CanvasImages_RenderSelectResizeMoveAndPersist()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(1440, 1000);
        await OpenPhase15DocumentAsync(page);

        var output = CreateOutputDirectory("desktop-1440x1000");
        var beforePath = Path.Combine(output, "00-phase15-images-before.png");
        var selectedPath = Path.Combine(output, "01-phase15-images-selected.png");
        var pointerPath = Path.Combine(output, "02-phase15-images-pointer-move-resize.png");
        var afterPath = Path.Combine(output, "03-phase15-images-after-reload.png");

        await page.GetByTestId("document-editor-demo").ScreenshotAsync(new LocatorScreenshotOptions
        {
            Path = beforePath,
            Type = ScreenshotType.Png
        });

        var initialProbe = await ReadPhase15ProbeAsync(page);
        Assert.AreEqual(Phase15DocumentId, initialProbe.ModelDocumentId);
        Assert.IsTrue(initialProbe.ObjectCount >= 2, $"Expected at least two canvas image objects. Actual: {initialProbe.ObjectCount}.");
        Assert.IsTrue(initialProbe.HasDrawingRun);
        Assert.IsTrue(initialProbe.HasAltWarning);
        Assert.AreEqual("Square", initialProbe.MainWrapMode);

        var mainObject = await ReadObjectRectAsync(page, "canvas-image-phase15-main");
        var mainHitPoint = await ReadObjectHitPointAsync(page, "canvas-image-phase15-main");
        await page.Mouse.ClickAsync((float)mainHitPoint.X, (float)mainHitPoint.Y);
        await WaitForObjectSelectionAsync(page, "canvas-image-phase15-main");
        await Assertions.Expect(page.GetByTestId("document-image-inspector")).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByTestId("document-image-inspector-section-wrap")).ToBeVisibleAsync();
        await WaitForInspectorWrapModeAsync(page, "canvas-image-phase15-main", "Square");
        await Assertions.Expect(page.GetByTestId("document-image-inspector-wrap-tight")).ToBeVisibleAsync();

        await page.GetByTestId("document-image-inspector-section-order").EvaluateAsync("node => node.open = true");
        await page.GetByTestId("document-image-inspector-bring-forward").ClickAsync();
        await Assertions.Expect(page.GetByTestId("document-image-inspector-bring-forward")).ToBeVisibleAsync();

        await page.GetByTestId("document-editor-demo").ScreenshotAsync(new LocatorScreenshotOptions
        {
            Path = selectedPath,
            Type = ScreenshotType.Png
        });

        var selectedObject = await ReadObjectRectAsync(page, "canvas-image-phase15-main");
        var selectedLayout = await ReadObjectModelLayoutAsync(page, "canvas-image-phase15-main");

        var movedLayout = await DragObjectByMouseUntilModelMovesAsync(
            page,
            "canvas-image-phase15-main",
            selectedLayout,
            deltaX: 47,
            deltaY: 21);
        var movedObject = await ReadObjectRectAsync(page, "canvas-image-phase15-main");
        var movedCanvasLayout = await ReadObjectCanvasLayoutRectAsync(page, "canvas-image-phase15-main");
        var moveSnap = await ReadLastObjectSnapAsync(page);
        AssertMoveSnapOrGridAligned(movedCanvasLayout, moveSnap, "pointer move");
        Assert.IsTrue(movedLayout.X > selectedLayout.X + 18, $"Expected pointer move to shift the image right. Before: {selectedLayout.X:N1}, after: {movedLayout.X:N1}.");
        Assert.IsTrue(movedLayout.Y > selectedLayout.Y + 8, $"Expected pointer move to shift the image down. Before: {selectedLayout.Y:N1}, after: {movedLayout.Y:N1}.");

        await page.GetByTestId("document-undo").ClickAsync();
        await WaitForObjectModelNearAsync(page, "canvas-image-phase15-main", selectedLayout, 2);
        await page.GetByTestId("document-redo").ClickAsync();
        movedLayout = await WaitForObjectModelNearAsync(page, "canvas-image-phase15-main", movedLayout, 2);
        movedObject = await ReadObjectRectAsync(page, "canvas-image-phase15-main");
        await WaitForObjectSelectionAsync(page, "canvas-image-phase15-main");

        await ResizeObjectFromHandleByMouseAsync(page, "canvas-image-phase15-main", "se", 56, 34);
        var resizeSnap = await WaitForLastObjectSnapAsync(page, requireX: true, requireY: false);
        var resizedLayout = await WaitForObjectModelLayoutAsync(
            page,
            "canvas-image-phase15-main",
            minimumX: movedLayout.X - 1,
            minimumY: movedLayout.Y - 1,
            minimumWidth: movedLayout.Width + 28,
            minimumHeight: movedLayout.Height + 12);
        var resizedObject = await ReadObjectRectAsync(page, "canvas-image-phase15-main");
        var selectedAspect = selectedObject.Width / selectedObject.Height;
        var resizedAspect = resizedObject.Width / resizedObject.Height;
        Assert.IsTrue(Math.Abs(selectedAspect - resizedAspect) < 0.25, $"Expected pointer resize to keep the image close to its original aspect ratio. Before: {selectedAspect:N3}, after: {resizedAspect:N3}.");
        var resizedCanvasLayout = await ReadObjectCanvasLayoutRectAsync(page, "canvas-image-phase15-main");
        Assert.AreEqual("grid", resizeSnap.XType, "Pointer resize should snap the southeast image edge to the snap grid.");
        AssertCanvasEdgeMatchesSnap(resizedCanvasLayout, resizeSnap, "pointer resize");
        AssertGridAligned(resizedLayout.Width, "pointer-resized image width");
        AssertGridAligned(resizedLayout.Height, "pointer-resized image height");

        await page.GetByTestId("document-undo").ClickAsync();
        await WaitForObjectModelNearAsync(page, "canvas-image-phase15-main", movedLayout, 2);
        await page.GetByTestId("document-redo").ClickAsync();
        var persistedLayout = await WaitForObjectModelNearAsync(page, "canvas-image-phase15-main", resizedLayout, 2);
        var persistedObject = await ReadObjectRectAsync(page, "canvas-image-phase15-main");
        await WaitForObjectSelectionAsync(page, "canvas-image-phase15-main");

        await AssertObjectDoesNotCoverTextAsync(page, "canvas-image-phase15-main", "canvas-images-wrap-text");
        await AssertObjectCaptionDoesNotCoverTextAsync(page, "canvas-image-phase15-main", "canvas-images-wrap-text");
        await DocumentEditorCanvasVisualAssert.AssertNoTextOverlapAsync(page);
        await DocumentEditorCanvasVisualAssert.AssertNoUiOverlapAsync(page);
        await page.GetByTestId("document-editor-demo").ScreenshotAsync(new LocatorScreenshotOptions
        {
            Path = pointerPath,
            Type = ScreenshotType.Png
        });

        var insertResult = await ExecuteCanvasCommandAsync(page, "insertImage", new
        {
            url = TinyPngDataUrl,
            width = 96,
            height = 96,
            anchorBlockId = "canvas-images-intro",
            altText = "Inserted URL image"
        });
        Assert.IsTrue(insertResult.Changed, insertResult.Debug);
        await WaitForLastCanvasCommandAsync(page, "insertImage");
        var afterInsertProbe = await ReadPhase15ProbeAsync(page);
        Assert.IsTrue(afterInsertProbe.ModelObjectCount > initialProbe.ModelObjectCount, $"Expected URL insert to add a canvas image object to the model. Before: {initialProbe.ModelObjectCount}, after: {afterInsertProbe.ModelObjectCount}.");
        Assert.IsTrue(afterInsertProbe.ModelImageCount > initialProbe.ModelImageCount, $"Expected URL insert to add a canvas image to the model. Before: {initialProbe.ModelImageCount}, after: {afterInsertProbe.ModelImageCount}.");

        await page.GetByTestId("document-save").ClickAsync();
        await WaitForSaveBoundaryAsync(page);

        await NavigateWithinBlazorAsync(page, "/canvas-engine-host?documentId=phase-5-canvas-render");
        await page.WaitForFunctionAsync(
            "() => document.querySelector('[data-testid=\"document-canvas-page\"]')?.getAttribute('data-canvas-model-document-id') === 'phase-5-canvas-render'",
            new PageWaitForFunctionOptions { Timeout = 20_000 });
        await NavigateWithinBlazorAsync(page, $"/canvas-engine-host?documentId={Phase15DocumentId}&showToolbar=true");
        await WaitForPhase15ReadyAsync(page);

        var reloadedObject = await ReadObjectRectAsync(page, "canvas-image-phase15-main");
        var reloadedLayout = await ReadObjectModelLayoutAsync(page, "canvas-image-phase15-main");
        await AssertObjectDoesNotCoverTextAsync(page, "canvas-image-phase15-main", "canvas-images-wrap-text");
        await AssertObjectCaptionDoesNotCoverTextAsync(page, "canvas-image-phase15-main", "canvas-images-wrap-text");
        await DocumentEditorCanvasVisualAssert.AssertNoTextOverlapAsync(page);
        await DocumentEditorCanvasVisualAssert.AssertNoUiOverlapAsync(page);
        var contentMetrics = await DocumentEditorCanvasVisualAssert.AssertCanvasNonBlankAsync(page.Locator("[data-canvas-layer='content']").First);
        var objectMetrics = await DocumentEditorCanvasVisualAssert.AssertCanvasNonBlankAsync(page.Locator("[data-canvas-layer='objects']").First);
        await page.GetByTestId("document-editor-demo").ScreenshotAsync(new LocatorScreenshotOptions
        {
            Path = afterPath,
            Type = ScreenshotType.Png
        });

        var afterProbe = await ReadPhase15ProbeAsync(page);
        Assert.IsTrue(reloadedObject.Width >= persistedObject.Width - 2);
        Assert.IsTrue(Math.Abs(reloadedLayout.X - persistedLayout.X) <= 2, $"Expected saved pointer-moved X to survive reload. Saved: {persistedLayout.X:N1}, reloaded: {reloadedLayout.X:N1}.");
        Assert.IsTrue(Math.Abs(reloadedLayout.Y - persistedLayout.Y) <= 2, $"Expected saved pointer-moved Y to survive reload. Saved: {persistedLayout.Y:N1}, reloaded: {reloadedLayout.Y:N1}.");
        Assert.IsTrue(Math.Abs(reloadedLayout.Width - persistedLayout.Width) <= 2, $"Expected saved pointer-resized width to survive reload. Saved: {persistedLayout.Width:N1}, reloaded: {reloadedLayout.Width:N1}.");
        Assert.IsTrue(afterProbe.A11yContainsCaption);
        Assert.IsTrue(afterProbe.SelectedHandleCount is 0 or 8);

        var manifestPath = Path.Combine(output, "manifest.json");
        await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(new
        {
            testName = nameof(Phase15_CanvasImages_RenderSelectResizeMoveAndPersist),
            seedDocumentId = Phase15DocumentId,
            userActions = new[]
            {
                "Open the phase 15 canvas image seed document.",
                "Click the standalone square-wrapped image and verify the object selection handles.",
                "Verify the image inspector exposes the active Square wrap mode and adjacent wrap controls.",
                "Use the inspector z-order command.",
                "Drag the selected image with the mouse, verify grid/object alignment snap guide state, and verify undo/redo restores object geometry.",
                "Resize the selected image from the southeast handle with the mouse, verify snap-to-grid state, and verify undo/redo restores object geometry.",
                "Insert a new URL image through the shared production canvas command runtime.",
                "Save through the production Save command, navigate away, navigate back, and verify object geometry survives reload."
            },
            expectedVisibleChanges = "The objects layer paints image content, URL insertion adds a real image object, the selected image shows eight handles, pointer move and pointer resize expose grid/object alignment snap guide state and update object geometry through undoable canvas commands, the inspector exposes active image state and z-order controls, text wraps around the square object, the missing-alt drawing keeps an accessibility warning marker, and geometry survives save/reload.",
            screenshotPaths = new[] { beforePath, selectedPath, pointerPath, afterPath },
            initialProbe,
            insertResult,
            afterInsertProbe,
            afterProbe,
            beforeGeometry = mainObject,
            selectedGeometry = selectedObject,
            selectedModelLayout = selectedLayout,
            movedGeometry = movedObject,
            movedModelLayout = movedLayout,
            moveSnap,
            resizedGeometry = resizedObject,
            resizedModelLayout = resizedLayout,
            resizeSnap,
            persistedGeometry = persistedObject,
            persistedModelLayout = persistedLayout,
            reloadedGeometry = reloadedObject,
            reloadedModelLayout = reloadedLayout,
            contentMetrics,
            objectMetrics
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));

        TestContext.AddResultFile(beforePath);
        TestContext.AddResultFile(selectedPath);
        TestContext.AddResultFile(pointerPath);
        TestContext.AddResultFile(afterPath);
        TestContext.AddResultFile(manifestPath);
    }

    private async Task OpenPhase15DocumentAsync(IPage page)
    {
        await page.GotoAsync($"{BaseUrl}/canvas-engine-host?documentId={Phase15DocumentId}&showToolbar=true&preferLocalDraft=false", new PageGotoOptions
        {
            WaitUntil = WaitUntilState.DOMContentLoaded,
            Timeout = 60_000
        });

        try
        {
            await WaitForPhase15ReadyAsync(page);
        }
        catch (TimeoutException)
        {
            await page.ReloadAsync(new PageReloadOptions
            {
                WaitUntil = WaitUntilState.DOMContentLoaded,
                Timeout = 60_000
            });
            await WaitForPhase15ReadyAsync(page);
        }
    }

    private static Task WaitForPhase15ReadyAsync(IPage page)
        => page.WaitForFunctionAsync(
            """
            () => {
                const hostReady = document.querySelector('[data-testid="document-canvas-engine-host"]')?.getAttribute('data-canvas-engine-ready') === 'true';
                const first = document.querySelector('[data-testid="document-canvas-page"]');
                return hostReady
                    && first?.getAttribute('data-canvas-model-document-id') === 'phase-15-canvas-images'
                    && document.querySelector('[data-canvas-object][data-object-id="canvas-image-phase15-main"]')
                    && document.querySelector('[data-canvas-object][data-object-role="drawingRun"]');
            }
            """,
            new PageWaitForFunctionOptions { Timeout = 30_000 });

    private static async Task WaitForObjectSelectionAsync(IPage page, string objectId)
    {
        try
        {
            await page.WaitForFunctionAsync(
                """
                objectId => {
                    const root = document.querySelector('[data-testid="document-canvas-engine-root"][data-page-surface-strategy="canvas-per-visible-page"]');
                    return root?.getAttribute('data-canvas-object-selected') === 'true'
                        && root?.getAttribute('data-canvas-object-id') === objectId
                        && root?.getAttribute('data-canvas-object-handle-count') === '8'
                        && document.querySelectorAll(`[data-canvas-object-resize-handle][data-object-id="${objectId}"]`).length === 8;
                }
                """,
                objectId,
                new PageWaitForFunctionOptions { Timeout = 10_000 });
        }
        catch (Exception ex) when (ex is TimeoutException or PlaywrightException)
        {
            Assert.Fail($"Canvas object selection did not target {objectId}.{Environment.NewLine}{await ReadPointerDiagnosticsAsync(page, objectId)}");
        }
    }

    private static async Task WaitForInspectorWrapModeAsync(IPage page, string objectId, string wrapMode)
    {
        try
        {
            await page.WaitForFunctionAsync(
                """
                args => {
                    const panel = document.querySelector('[data-testid="document-image-properties-panel"]');
                    const button = document.querySelector(`[data-testid="document-image-inspector-wrap-${args.wrapMode.toLowerCase().replace(/([a-z])([A-Z])/g, '$1-$2')}"]`);
                    return panel?.getAttribute('data-active-object-id') === args.objectId
                        && button?.getAttribute('aria-pressed') === 'true';
                }
                """,
                new { objectId, wrapMode },
                new PageWaitForFunctionOptions { Timeout = 10_000 });
        }
        catch (Exception ex) when (ex is TimeoutException or PlaywrightException)
        {
            Assert.Fail($"Image inspector did not activate {wrapMode} for {objectId}.{Environment.NewLine}{await ReadInspectorDiagnosticsAsync(page, objectId)}");
        }
    }

    private static Task<string> ReadInspectorDiagnosticsAsync(IPage page, string objectId)
        => page.EvaluateAsync<string>(
            """
            async objectId => {
                const root = document.querySelector('[data-testid="document-canvas-engine-root"][data-page-surface-strategy="canvas-per-visible-page"]');
                const host = document.querySelector('[data-testid="document-canvas-engine-host"]');
                const panel = document.querySelector('[data-testid="document-image-properties-panel"]');
                const buttons = Array.from(document.querySelectorAll('[data-human-testid="document-image-wrap-button"]'))
                    .map(button => ({
                        testId: button.getAttribute('data-testid') || '',
                        wrapMode: button.getAttribute('data-wrap-mode') || '',
                        pressed: button.getAttribute('aria-pressed') || ''
                    }));
                let formatting = null;
                let selection = null;
                const handle = host?.getAttribute('data-canvas-engine-handle') || '';
                if (handle) {
                    const module = await import('/_content/Tempo.Blazor.DocumentEditor/js/document-editor-canvas/interop.mjs');
                    formatting = JSON.parse(module.getFormattingStateJson(handle) || '{}');
                    selection = JSON.parse(module.getSelectionStateJson(handle) || '{}');
                }

                return JSON.stringify({
                    expectedObjectId: objectId,
                    rootObjectId: root?.getAttribute('data-canvas-object-id') || '',
                    rootWrapMode: root?.getAttribute('data-canvas-object-wrap-mode') || '',
                    panelObjectId: panel?.getAttribute('data-active-object-id') || '',
                    buttons,
                    formattingImage: formatting?.image || null,
                    selection
                }, null, 2);
            }
            """,
            objectId);

    private static async Task<ObjectRect> ReadObjectRectAsync(IPage page, string objectId)
    {
        await page.WaitForFunctionAsync(
            """
            objectId => {
                const node = document.querySelector(`[data-canvas-object][data-object-id="${objectId}"]`);
                const rect = node?.getBoundingClientRect();
                return rect && rect.width > 0.5 && rect.height > 0.5;
            }
            """,
            objectId,
            new PageWaitForFunctionOptions { Timeout = 10_000 });

        return await page.EvaluateAsync<ObjectRect>(
            """
            async objectId => {
                // 'instant': the demo sets `scroll-behavior: smooth` on <html>, so a default scrollIntoView
                // animates and the two frames below would read the rect mid-scroll.
                document.querySelector(`[data-canvas-object][data-object-id="${objectId}"]`)
                    ?.scrollIntoView({ block: 'center', inline: 'center', behavior: 'instant' });
                await new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)));

                const node = document.querySelector(`[data-canvas-object][data-object-id="${objectId}"]`);
                if (!node) {
                    throw new Error(`Canvas object metadata not found: ${objectId}`);
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
            objectId);
    }

    private static Task<ObjectHitPoint> ReadObjectHitPointAsync(IPage page, string objectId)
        => page.EvaluateAsync<ObjectHitPoint>(
            """
            async objectId => {
                const metadataNode = document.querySelector(`[data-canvas-object][data-object-id="${objectId}"]`);
                // 'instant' for the same reason as ReadObjectRectAsync: a smooth scroll still in flight
                // moves the object after this point is read, and the drag then starts off the image.
                metadataNode?.scrollIntoView({ block: 'center', inline: 'center', behavior: 'instant' });
                await new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)));
                const metadataRect = metadataNode?.getBoundingClientRect?.();
                if (metadataRect && metadataRect.width > 0.5 && metadataRect.height > 0.5) {
                    return {
                        x: metadataRect.left + metadataRect.width / 2,
                        y: metadataRect.top + metadataRect.height / 2,
                        pageX: 0,
                        pageY: 0,
                        pageIndex: Number(metadataNode.closest?.('[data-testid="document-canvas-page"]')?.getAttribute('data-page-index') || '0') || 0
                    };
                }

                const host = document.querySelector('[data-testid="document-canvas-engine-host"]');
                const handle = host?.getAttribute('data-canvas-engine-handle') || '';
                const module = await import('/_content/Tempo.Blazor.DocumentEditor/js/document-editor-canvas/interop.mjs');
                const debug = JSON.parse(module.getRuntimeDebugSnapshotJson(handle) || '{}');
                const blocks = [
                    ...(debug?.render?.selectionLayout?.blocks || []),
                    ...(debug?.layout?.blocks || [])
                ];
                const block = blocks.find(candidate =>
                    String(candidate?.objectId || candidate?.object?.objectId || '') === objectId);
                if (!block?.rect) {
                    throw new Error(`Canvas object layout not found: ${objectId}`);
                }

                const pageIndex = Number(block.pageIndex || 0) || 0;
                const page = document.querySelector(`[data-testid="document-canvas-page"][data-page-index="${pageIndex}"]`)
                    || metadataNode?.closest?.('[data-testid="document-canvas-page"]')
                    || document.querySelector('[data-testid="document-canvas-page"]');
                if (!page) {
                    throw new Error(`Canvas page not found for object: ${objectId}`);
                }

                const pageRect = page.getBoundingClientRect();
                const scale = Math.max(0.01, Number(page.getAttribute('data-canvas-page-zoom-scale') || '1') || 1);
                const pageX = Number(block.rect.x || 0) + Number(block.rect.width || 0) / 2;
                const pageY = Number(block.rect.y || 0) + Number(block.rect.height || 0) / 2;
                return {
                    x: pageRect.left + pageX * scale,
                    y: pageRect.top + pageY * scale,
                    pageX,
                    pageY,
                    pageIndex
                };
            }
            """,
            objectId);

    private static Task<Phase15Probe> ReadPhase15ProbeAsync(IPage page)
        => page.EvaluateAsync<Phase15Probe>(
            """
            async () => {
                const root = document.querySelector('[data-testid="document-canvas-engine-root"][data-page-surface-strategy="canvas-per-visible-page"]');
                const host = document.querySelector('[data-testid="document-canvas-engine-host"]');
                const handle = host?.getAttribute('data-canvas-engine-handle') || '';
                const module = await import('/_content/Tempo.Blazor.DocumentEditor/js/document-editor-canvas/interop.mjs');
                const model = JSON.parse(module.getModelJson(handle) || '{}');
                const canvasPages = Array.from(document.querySelectorAll('[data-testid="document-canvas-page"]'));
                const phasePage = canvasPages.find(page => page.getAttribute('data-canvas-model-document-id') === 'phase-15-canvas-images');
                const objects = Array.from(document.querySelectorAll('[data-canvas-object]'));
                const main = objects.find(object => object.getAttribute('data-object-id') === 'canvas-image-phase15-main');
                const modelObjects = collectModelObjects(model);
                return {
                    modelDocumentId: phasePage?.getAttribute('data-canvas-model-document-id') || new URLSearchParams(location.search).get('documentId') || '',
                    objectCount: objects.length,
                    imageCount: objects.filter(object => (object.getAttribute('data-object-kind') || '') === 'image').length,
                    modelObjectCount: modelObjects.length,
                    modelImageCount: modelObjects.filter(object => object.kind === 'image').length,
                    mainWrapMode: main?.getAttribute('data-wrap-mode') || '',
                    hasDrawingRun: objects.some(object => object.getAttribute('data-object-role') === 'drawingRun'),
                    hasAltWarning: objects.some(object => object.getAttribute('data-has-alt-warning') === 'true'),
                    selectedObjectId: root?.getAttribute('data-canvas-object-id') || '',
                    selectedHandleCount: Number(root?.getAttribute('data-canvas-object-handle-count') || '0'),
                    a11yContainsCaption: document.querySelector('[data-testid="document-canvas-a11y-mirror"]')?.textContent?.includes('Phase 15 square wrapped image caption') === true
                };

                function collectModelObjects(documentModel) {
                    const result = [];
                    for (const block of documentModel?.body?.blocks || []) {
                        const image = block?.content?.image || block?.Content?.Image;
                        if (image) {
                            result.push({ role: 'imageBlock', kind: 'image' });
                        }

                        const runs = block?.content?.runs || block?.Content?.Runs || block?.content?.inlines || block?.Content?.Inlines || [];
                        for (const run of runs) {
                            const drawing = run?.drawing || run?.Drawing;
                            if (drawing) {
                                const kind = String(drawing?.kind ?? drawing?.Kind ?? 'image').replace(/\s+/g, '').toLowerCase();
                                result.push({ role: 'drawingRun', kind });
                            }
                        }
                    }

                    return result;
                }
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
                const saveButtonDisabled = document.querySelector('[data-testid="document-save"]')?.hasAttribute('disabled') === true;
                return saveButtonDisabled === false
                    && pending.trim().length === 0
                    && (/Saved|Autosaved/i.test(saveMessage) || /saved/i.test(lastSaved));
            }
            """,
            new PageWaitForFunctionOptions { Timeout = 10_000 });
    }

    private static async Task<Phase15CommandProbe> ExecuteCanvasCommandAsync(IPage page, string commandId, object payload)
    {
        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        return await page.EvaluateAsync<Phase15CommandProbe>(
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
                    objectId: parsed?.result?.object?.objectId || '',
                    debug: JSON.stringify(parsed)
                };
            }
            """,
            new { commandId, json });
    }

    private static async Task<ObjectRect> WaitForObjectWidthGreaterThanAsync(IPage page, string objectId, double minWidth)
    {
        await page.WaitForFunctionAsync(
            """
            args => {
                const node = document.querySelector(`[data-canvas-object][data-object-id="${args.objectId}"]`);
                return node?.getBoundingClientRect().width > args.minWidth;
            }
            """,
            new { objectId, minWidth },
            new PageWaitForFunctionOptions { Timeout = 10_000 });
        return await ReadObjectRectAsync(page, objectId);
    }

    private static async Task<ObjectRect> WaitForObjectNearAsync(IPage page, string objectId, ObjectRect expected, double tolerance)
    {
        try
        {
            await page.WaitForFunctionAsync(
                """
                args => {
                    const node = document.querySelector(`[data-canvas-object][data-object-id="${args.objectId}"]`);
                    const rect = node?.getBoundingClientRect();
                    return rect
                        && Math.abs(rect.x - args.x) <= args.tolerance
                        && Math.abs(rect.y - args.y) <= args.tolerance
                        && Math.abs(rect.width - args.width) <= args.tolerance
                        && Math.abs(rect.height - args.height) <= args.tolerance;
                }
                """,
                new { objectId, x = expected.X, y = expected.Y, width = expected.Width, height = expected.Height, tolerance },
                new PageWaitForFunctionOptions { Timeout = 10_000 });
        }
        catch (TimeoutException)
        {
            Assert.Fail($"Canvas object {objectId} did not reach expected viewport geometry x={expected.X:N1}, y={expected.Y:N1}, w={expected.Width:N1}, h={expected.Height:N1} within {tolerance:N1}px.{Environment.NewLine}{await ReadPointerDiagnosticsAsync(page, objectId)}");
        }

        return await ReadObjectRectAsync(page, objectId);
    }

    private static async Task DragObjectByMouseAsync(IPage page, string objectId, double deltaX, double deltaY)
    {
        await ReadObjectRectAsync(page, objectId);
        var start = await ReadObjectHitPointAsync(page, objectId);
        await page.Mouse.MoveAsync((float)start.X, (float)start.Y);
        await page.Mouse.DownAsync();
        await page.Mouse.MoveAsync((float)(start.X + deltaX / 2), (float)(start.Y + deltaY / 2), new MouseMoveOptions { Steps = 4 });
        await page.Mouse.MoveAsync((float)(start.X + deltaX), (float)(start.Y + deltaY), new MouseMoveOptions { Steps = 6 });
        await page.Mouse.UpAsync();
    }

    private static async Task<ObjectModelLayout> DragObjectByMouseUntilModelMovesAsync(
        IPage page,
        string objectId,
        ObjectModelLayout selectedLayout,
        double deltaX,
        double deltaY)
    {
        Exception? lastError = null;
        for (var attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                if (attempt > 0)
                {
                    var hitPoint = await ReadObjectHitPointAsync(page, objectId);
                    await page.Mouse.ClickAsync((float)hitPoint.X, (float)hitPoint.Y);
                    await WaitForObjectSelectionAsync(page, objectId);
                    await page.WaitForTimeoutAsync(150);
                }

                await DragObjectByMouseAsync(page, objectId, deltaX, deltaY);
                return await WaitForObjectModelLayoutAsync(
                    page,
                    objectId,
                    minimumX: selectedLayout.X + 24,
                    minimumY: selectedLayout.Y + 12,
                    minimumWidth: selectedLayout.Width - 1,
                    minimumHeight: selectedLayout.Height - 1);
            }
            catch (Exception ex) when (attempt == 0 && ex is TimeoutException or PlaywrightException)
            {
                lastError = ex;
            }
        }

        var current = await ReadObjectModelLayoutAsync(page, objectId);
        Assert.Fail($"Canvas object drag did not update model layout after retry. Before: x={selectedLayout.X:N1}, y={selectedLayout.Y:N1}; after: x={current.X:N1}, y={current.Y:N1}.{Environment.NewLine}{await ReadPointerDiagnosticsAsync(page, objectId)}");
        throw new InvalidOperationException("Unreachable object drag retry failure.", lastError);
    }

    private static async Task ResizeObjectFromHandleByMouseAsync(IPage page, string objectId, string handleName, double deltaX, double deltaY)
    {
        await WaitForObjectSelectionAsync(page, objectId);
        var handle = page.Locator($"[data-canvas-object-resize-handle='{handleName}'][data-object-id='{objectId}']").First;
        await handle.WaitForAsync(new LocatorWaitForOptions { Timeout = 10_000 });
        var handleBox = await handle.BoundingBoxAsync();
        Assert.IsNotNull(handleBox, $"Resize handle {handleName} for {objectId} must expose a bounding box.");

        await ReadObjectRectAsync(page, objectId);
        var startX = handleBox!.X + handleBox.Width / 2;
        var startY = handleBox.Y + handleBox.Height / 2;
        await page.Mouse.MoveAsync((float)startX, (float)startY);
        await page.Mouse.DownAsync();
        await page.Mouse.MoveAsync((float)(startX + deltaX / 2), (float)(startY + deltaY / 2), new MouseMoveOptions { Steps = 4 });
        await page.Mouse.MoveAsync((float)(startX + deltaX), (float)(startY + deltaY), new MouseMoveOptions { Steps = 6 });
        await page.Mouse.UpAsync();
    }

    private static Task<string> ReadPointerDiagnosticsAsync(IPage page, string objectId)
        => page.EvaluateAsync<string>(
            """
            async objectId => {
                const root = document.querySelector('[data-testid="document-canvas-engine-root"][data-page-surface-strategy="canvas-per-visible-page"]');
                const host = document.querySelector('[data-testid="document-canvas-engine-host"]');
                const object = document.querySelector(`[data-canvas-object][data-object-id="${objectId}"]`);
                const page = object?.closest?.('[data-testid="document-canvas-page"]')
                    || document.querySelector('[data-testid="document-canvas-page"]');
                const handle = host?.getAttribute('data-canvas-engine-handle') || '';
                let selection = {};
                let modelLayout = null;
                let layoutObject = null;
                let layoutImageBlocks = [];
                if (handle) {
                    const module = await import('/_content/Tempo.Blazor.DocumentEditor/js/document-editor-canvas/interop.mjs');
                    selection = JSON.parse(module.getSelectionStateJson(handle) || '{}');
                    const model = JSON.parse(module.getModelJson(handle) || '{}');
                    const debug = JSON.parse(module.getRuntimeDebugSnapshotJson(handle) || '{}');
                    const selectionBlocks = debug?.render?.selectionLayout?.blocks || debug?.layout?.blocks || [];
                    layoutImageBlocks = selectionBlocks
                        .filter(block => block?.type === 'image' || block?.object || block?.objectId)
                        .map(block => ({
                            blockId: block?.blockId || '',
                            runId: block?.runId || '',
                            objectId: block?.objectId || '',
                            nestedObjectId: block?.object?.objectId || '',
                            role: block?.role || block?.object?.role || '',
                            type: block?.type || '',
                            rect: block?.rect || null
                        }));
                    layoutObject = selectionBlocks.find(block =>
                        String(block?.objectId || block?.object?.objectId || '') === objectId) || null;
                    const source = findObjectSource(model, objectId);
                    if (source) {
                        const layout = source.layout || source.Layout || {};
                        const position = layout.position || layout.Position || {};
                        const transform = layout.transform || layout.Transform || {};
                        modelLayout = {
                            x: Number(position.x ?? position.X ?? 0) || 0,
                            y: Number(position.y ?? position.Y ?? 0) || 0,
                            width: Number(transform.width ?? transform.Width ?? source.size?.width ?? source.Size?.Width ?? 0) || 0,
                            height: Number(transform.height ?? transform.Height ?? source.size?.height ?? source.Size?.Height ?? 0) || 0
                        };
                    }
                }

                return JSON.stringify({
                    rootDragging: root?.getAttribute('data-canvas-object-dragging') || '',
                    rootSelected: root?.getAttribute('data-canvas-object-selected') || '',
                    rootObjectId: root?.getAttribute('data-canvas-object-id') || '',
                    rootCommandLast: root?.getAttribute('data-canvas-command-last') || '',
                    pointerPageIndex: root?.getAttribute('data-canvas-pointer-page-index') || '',
                    pointerX: root?.getAttribute('data-canvas-pointer-x') || '',
                    pointerY: root?.getAttribute('data-canvas-pointer-y') || '',
                    pointerObjectId: root?.getAttribute('data-canvas-pointer-object-id') || '',
                    pointerHitBlockId: root?.getAttribute('data-canvas-pointer-hit-block-id') || '',
                    objectRect: object?.getBoundingClientRect?.().toJSON?.() || null,
                    pageRect: page?.getBoundingClientRect?.().toJSON?.() || null,
                    selection,
                    modelLayout,
                    layoutObject,
                    layoutImageBlocks
                }, null, 2);

                function findObjectSource(documentModel, id) {
                    for (const block of documentModel?.body?.blocks || []) {
                        const image = block?.content?.image || block?.Content?.Image;
                        const imageObjectId = String(image?.objectId ?? image?.ObjectId ?? block?.id ?? block?.Id ?? '');
                        if (image && imageObjectId === id) {
                            return image;
                        }

                        const runs = block?.content?.runs || block?.Content?.Runs || block?.content?.inlines || block?.Content?.Inlines || [];
                        for (const run of runs) {
                            const drawing = run?.drawing || run?.Drawing;
                            if (String(drawing?.objectId ?? drawing?.ObjectId ?? '') === id) {
                                return drawing;
                            }
                        }
                    }

                    return null;
                }
            }
            """,
            objectId);

    private static async Task<ObjectRect> WaitForObjectMovedAsync(IPage page, string objectId, ObjectRect before, double minimumDeltaX, double minimumDeltaY)
    {
        await page.WaitForFunctionAsync(
            """
            args => {
                const node = document.querySelector(`[data-canvas-object][data-object-id="${args.objectId}"]`);
                const rect = node?.getBoundingClientRect();
                return rect
                    && rect.x >= args.x + args.minimumDeltaX
                    && rect.y >= args.y + args.minimumDeltaY;
            }
            """,
            new { objectId, x = before.X, y = before.Y, minimumDeltaX, minimumDeltaY },
            new PageWaitForFunctionOptions { Timeout = 10_000 });
        return await ReadObjectRectAsync(page, objectId);
    }

    private static async Task<ObjectRect> WaitForObjectSizeGreaterThanAsync(IPage page, string objectId, double minimumWidth, double minimumHeight)
    {
        await page.WaitForFunctionAsync(
            """
            args => {
                const node = document.querySelector(`[data-canvas-object][data-object-id="${args.objectId}"]`);
                const rect = node?.getBoundingClientRect();
                return rect
                    && rect.width >= args.minimumWidth
                    && rect.height >= args.minimumHeight;
            }
            """,
            new { objectId, minimumWidth, minimumHeight },
            new PageWaitForFunctionOptions { Timeout = 10_000 });
        return await ReadObjectRectAsync(page, objectId);
    }

    private static Task<ObjectModelLayout> ReadObjectModelLayoutAsync(IPage page, string objectId)
        => page.EvaluateAsync<ObjectModelLayout>(
            """
            async objectId => {
                const host = document.querySelector('[data-testid="document-canvas-engine-host"]');
                const handle = host?.getAttribute('data-canvas-engine-handle') || '';
                const module = await import('/_content/Tempo.Blazor.DocumentEditor/js/document-editor-canvas/interop.mjs');
                const model = JSON.parse(module.getModelJson(handle) || '{}');
                const source = findObjectSource(model, objectId);
                if (!source) {
                    throw new Error(`Canvas image source not found in model: ${objectId}`);
                }

                const layout = source.layout || source.Layout || {};
                const position = layout.position || layout.Position || {};
                const transform = layout.transform || layout.Transform || {};
                const size = source.size || source.Size || {};
                return {
                    x: Number(position.x ?? position.X ?? 0) || 0,
                    y: Number(position.y ?? position.Y ?? 0) || 0,
                    width: Number(transform.width ?? transform.Width ?? size.width ?? size.Width ?? 0) || 0,
                    height: Number(transform.height ?? transform.Height ?? size.height ?? size.Height ?? 0) || 0
                };

                function findObjectSource(documentModel, id) {
                    for (const block of documentModel?.body?.blocks || []) {
                        const image = block?.content?.image || block?.Content?.Image;
                        const imageObjectId = String(image?.objectId ?? image?.ObjectId ?? block?.id ?? block?.Id ?? '');
                        if (image && imageObjectId === id) {
                            return image;
                        }

                        const runs = block?.content?.runs || block?.Content?.Runs || block?.content?.inlines || block?.Content?.Inlines || [];
                        for (const run of runs) {
                            const drawing = run?.drawing || run?.Drawing;
                            if (String(drawing?.objectId ?? drawing?.ObjectId ?? '') === id) {
                                return drawing;
                            }
                        }
                    }

                    return null;
                }
            }
            """,
            objectId);

    /// <summary>
    /// Loads the canvas interop module into <c>window.__tmDocumentCanvasInteropModule</c> so the model waits
    /// below can poll it from a SYNCHRONOUS predicate. Playwright's <c>WaitForFunctionAsync</c> does not await
    /// the predicate: an <c>async</c> predicate returns a Promise, which is truthy, so the wait resolved on
    /// its first poll whatever the model said (measured: <c>async () =&gt; false</c> returned after ~10 ms,
    /// <c>() =&gt; false</c> timed out). The model waits here were therefore no waits at all, and the drag
    /// retry in <see cref="DragObjectByMouseUntilModelMovesAsync"/> could never trigger.
    /// </summary>
    private static Task EnsureCanvasInteropModuleAsync(IPage page)
        => page.EvaluateAsync(
            """
            async () => {
                window.__tmDocumentCanvasInteropModule ||= await import('/_content/Tempo.Blazor.DocumentEditor/js/document-editor-canvas/interop.mjs');
            }
            """);

    private static async Task<ObjectModelLayout> WaitForObjectModelLayoutAsync(
        IPage page,
        string objectId,
        double minimumX,
        double minimumY,
        double minimumWidth,
        double minimumHeight)
    {
        await EnsureCanvasInteropModuleAsync(page);
        await page.WaitForFunctionAsync(
            """
            args => {
                const host = document.querySelector('[data-testid="document-canvas-engine-host"]');
                const handle = host?.getAttribute('data-canvas-engine-handle') || '';
                const module = window.__tmDocumentCanvasInteropModule;
                if (!module || !handle) {
                    return false;
                }

                const model = JSON.parse(module.getModelJson(handle) || '{}');
                const source = findObjectSource(model, args.objectId);
                if (!source) {
                    return false;
                }

                const layout = source.layout || source.Layout || {};
                const position = layout.position || layout.Position || {};
                const transform = layout.transform || layout.Transform || {};
                const size = source.size || source.Size || {};
                const x = Number(position.x ?? position.X ?? 0) || 0;
                const y = Number(position.y ?? position.Y ?? 0) || 0;
                const width = Number(transform.width ?? transform.Width ?? size.width ?? size.Width ?? 0) || 0;
                const height = Number(transform.height ?? transform.Height ?? size.height ?? size.Height ?? 0) || 0;
                return x >= args.minimumX
                    && y >= args.minimumY
                    && width >= args.minimumWidth
                    && height >= args.minimumHeight;

                function findObjectSource(documentModel, id) {
                    for (const block of documentModel?.body?.blocks || []) {
                        const image = block?.content?.image || block?.Content?.Image;
                        const imageObjectId = String(image?.objectId ?? image?.ObjectId ?? block?.id ?? block?.Id ?? '');
                        if (image && imageObjectId === id) {
                            return image;
                        }

                        const runs = block?.content?.runs || block?.Content?.Runs || block?.content?.inlines || block?.Content?.Inlines || [];
                        for (const run of runs) {
                            const drawing = run?.drawing || run?.Drawing;
                            if (String(drawing?.objectId ?? drawing?.ObjectId ?? '') === id) {
                                return drawing;
                            }
                        }
                    }

                    return null;
                }
            }
            """,
            new { objectId, minimumX, minimumY, minimumWidth, minimumHeight },
            new PageWaitForFunctionOptions { Timeout = 10_000 });
        return await ReadObjectModelLayoutAsync(page, objectId);
    }

    private static async Task<ObjectModelLayout> WaitForObjectModelNearAsync(IPage page, string objectId, ObjectModelLayout expected, double tolerance)
    {
        await EnsureCanvasInteropModuleAsync(page);
        await page.WaitForFunctionAsync(
            """
            args => {
                const host = document.querySelector('[data-testid="document-canvas-engine-host"]');
                const handle = host?.getAttribute('data-canvas-engine-handle') || '';
                const module = window.__tmDocumentCanvasInteropModule;
                if (!module || !handle) {
                    return false;
                }

                const model = JSON.parse(module.getModelJson(handle) || '{}');
                const source = findObjectSource(model, args.objectId);
                if (!source) {
                    return false;
                }

                const layout = source.layout || source.Layout || {};
                const position = layout.position || layout.Position || {};
                const transform = layout.transform || layout.Transform || {};
                const size = source.size || source.Size || {};
                const x = Number(position.x ?? position.X ?? 0) || 0;
                const y = Number(position.y ?? position.Y ?? 0) || 0;
                const width = Number(transform.width ?? transform.Width ?? size.width ?? size.Width ?? 0) || 0;
                const height = Number(transform.height ?? transform.Height ?? size.height ?? size.Height ?? 0) || 0;
                return Math.abs(x - args.x) <= args.tolerance
                    && Math.abs(y - args.y) <= args.tolerance
                    && Math.abs(width - args.width) <= args.tolerance
                    && Math.abs(height - args.height) <= args.tolerance;

                function findObjectSource(documentModel, id) {
                    for (const block of documentModel?.body?.blocks || []) {
                        const image = block?.content?.image || block?.Content?.Image;
                        const imageObjectId = String(image?.objectId ?? image?.ObjectId ?? block?.id ?? block?.Id ?? '');
                        if (image && imageObjectId === id) {
                            return image;
                        }

                        const runs = block?.content?.runs || block?.Content?.Runs || block?.content?.inlines || block?.Content?.Inlines || [];
                        for (const run of runs) {
                            const drawing = run?.drawing || run?.Drawing;
                            if (String(drawing?.objectId ?? drawing?.ObjectId ?? '') === id) {
                                return drawing;
                            }
                        }
                    }

                    return null;
                }
            }
            """,
            new { objectId, x = expected.X, y = expected.Y, width = expected.Width, height = expected.Height, tolerance },
            new PageWaitForFunctionOptions { Timeout = 10_000 });
        return await ReadObjectModelLayoutAsync(page, objectId);
    }

    private static Task WaitForLastCanvasCommandAsync(IPage page, string commandId)
        => page.WaitForFunctionAsync(
            """
            commandId => {
                const last = document.querySelector('[data-testid="document-canvas-engine-root"][data-page-surface-strategy="canvas-per-visible-page"]')?.getAttribute('data-canvas-command-last') || '';
                return last.toLowerCase() === commandId.toLowerCase();
            }
            """,
            commandId,
            new PageWaitForFunctionOptions { Timeout = 10_000 });

    private static async Task<ObjectSnapState> WaitForLastObjectSnapAsync(IPage page, bool requireX, bool requireY)
    {
        await page.WaitForFunctionAsync(
            """
            args => {
                const root = document.querySelector('[data-testid="document-canvas-engine-root"][data-page-surface-strategy="canvas-per-visible-page"]');
                const active = root?.getAttribute('data-canvas-object-snap-last-active') === 'true';
                const hasX = (root?.getAttribute('data-canvas-object-snap-last-x-edge') || '').length > 0;
                const hasY = (root?.getAttribute('data-canvas-object-snap-last-y-edge') || '').length > 0;
                return active && (!args.requireX || hasX) && (!args.requireY || hasY);
            }
            """,
            new { requireX, requireY },
            new PageWaitForFunctionOptions { Timeout = 10_000 });

        return await ReadLastObjectSnapAsync(page);
    }

    private static Task<ObjectSnapState> ReadLastObjectSnapAsync(IPage page)
    {
        return page.EvaluateAsync<ObjectSnapState>(
            """
            () => {
                const root = document.querySelector('[data-testid="document-canvas-engine-root"][data-page-surface-strategy="canvas-per-visible-page"]');
                const xEdge = root?.getAttribute('data-canvas-object-snap-last-x-edge') || '';
                const yEdge = root?.getAttribute('data-canvas-object-snap-last-y-edge') || '';
                return {
                    active: root?.getAttribute('data-canvas-object-snap-last-active') === 'true',
                    hasX: xEdge.length > 0,
                    hasY: yEdge.length > 0,
                    x: Number(root?.getAttribute('data-canvas-object-snap-last-x') || '0') || 0,
                    y: Number(root?.getAttribute('data-canvas-object-snap-last-y') || '0') || 0,
                    xType: root?.getAttribute('data-canvas-object-snap-last-x-type') || '',
                    yType: root?.getAttribute('data-canvas-object-snap-last-y-type') || '',
                    xEdge,
                    yEdge
                };
            }
            """);
    }

    private static Task<ObjectRect> ReadObjectCanvasLayoutRectAsync(IPage page, string objectId)
        => page.EvaluateAsync<ObjectRect>(
            """
            async objectId => {
                const host = document.querySelector('[data-testid="document-canvas-engine-host"]');
                const handle = host?.getAttribute('data-canvas-engine-handle') || '';
                const module = await import('/_content/Tempo.Blazor.DocumentEditor/js/document-editor-canvas/interop.mjs');
                const debug = JSON.parse(module.getRuntimeDebugSnapshotJson(handle) || '{}');
                const layout = debug?.render?.selectionLayout || debug?.layout || {};
                const blocks = layout?.blocks || [];
                const block = blocks.find(candidate =>
                    String(candidate?.objectId || candidate?.object?.objectId || '') === objectId);
                if (!block) {
                    throw new Error(`Canvas object layout not found: ${objectId}`);
                }

                const rect = block.rect || {};
                return {
                    x: Number(rect.x || 0) || 0,
                    y: Number(rect.y || 0) || 0,
                    width: Number(rect.width || 0) || 0,
                    height: Number(rect.height || 0) || 0,
                    centerX: (Number(rect.x || 0) || 0) + (Number(rect.width || 0) || 0) / 2,
                    centerY: (Number(rect.y || 0) || 0) + (Number(rect.height || 0) || 0) / 2
                };
            }
            """,
            objectId);

    private static void AssertCanvasEdgeMatchesSnap(ObjectRect layout, ObjectSnapState snap, string label)
    {
        Assert.IsTrue(snap.Active, $"{label} should report active object snap state.");
        if (snap.HasX)
        {
            var actualX = snap.XEdge switch
            {
                "left" => layout.X,
                "centerX" => layout.X + layout.Width / 2,
                "right" => layout.X + layout.Width,
                _ => double.NaN
            };
            Assert.IsFalse(double.IsNaN(actualX), $"{label} reported unsupported X snap edge '{snap.XEdge}'.");
            Assert.AreEqual(snap.X, actualX, 1.0, $"{label} model X edge should match the last snap guide.");
            if (snap.XType == "grid")
            {
                AssertGridAligned(snap.X, $"{label} X snap guide");
            }
        }

        if (snap.HasY)
        {
            var actualY = snap.YEdge switch
            {
                "top" => layout.Y,
                "centerY" => layout.Y + layout.Height / 2,
                "bottom" => layout.Y + layout.Height,
                _ => double.NaN
            };
            Assert.IsFalse(double.IsNaN(actualY), $"{label} reported unsupported Y snap edge '{snap.YEdge}'.");
            Assert.AreEqual(snap.Y, actualY, 1.0, $"{label} model Y edge should match the last snap guide.");
            if (snap.YType == "grid")
            {
                AssertGridAligned(snap.Y, $"{label} Y snap guide");
            }
        }
    }

    private static void AssertMoveSnapOrGridAligned(ObjectRect layout, ObjectSnapState snap, string label)
    {
        if (snap.Active && (snap.HasX || snap.HasY))
        {
            if (snap.HasX)
            {
                Assert.AreEqual("grid", snap.XType, $"{label} should snap the image X edge to the snap grid.");
            }

            if (snap.HasY)
            {
                Assert.IsTrue(IsSupportedSnapType(snap.YType), $"{label} should snap the image Y edge to a grid or alignment guide. Actual: {snap.YType}.");
            }

            AssertCanvasEdgeMatchesSnap(layout, snap, label);
            return;
        }

        AssertAnyHorizontalEdgeGridAligned(layout, $"{label} X edge");
    }

    private static void AssertAnyHorizontalEdgeGridAligned(ObjectRect layout, string label)
    {
        var edges = new[] { layout.X, layout.X + layout.Width / 2, layout.X + layout.Width };
        Assert.IsTrue(edges.Any(IsGridAligned), $"{label} should have at least one grid-aligned horizontal edge. Left={layout.X:N3}, Center={layout.X + layout.Width / 2:N3}, Right={layout.X + layout.Width:N3}.");
    }

    private static void AssertAnyVerticalEdgeGridAligned(ObjectRect layout, string label)
    {
        var edges = new[] { layout.Y, layout.Y + layout.Height / 2, layout.Y + layout.Height };
        Assert.IsTrue(edges.Any(IsGridAligned), $"{label} should have at least one grid-aligned vertical edge. Top={layout.Y:N3}, Center={layout.Y + layout.Height / 2:N3}, Bottom={layout.Y + layout.Height:N3}.");
    }

    private static bool IsGridAligned(double value)
    {
        var snapped = Math.Round(value / ImageSnapGrid) * ImageSnapGrid;
        return Math.Abs(value - snapped) <= 1.0;
    }

    private static void AssertGridAligned(double value, string label)
    {
        var snapped = Math.Round(value / ImageSnapGrid) * ImageSnapGrid;
        Assert.IsTrue(Math.Abs(value - snapped) <= 0.01, $"{label} should be aligned to {ImageSnapGrid:N0}px grid. Actual: {value:N3}, nearest: {snapped:N3}.");
    }

    private static bool IsSupportedSnapType(string type)
        => type == "grid"
            || type.StartsWith("object-", StringComparison.Ordinal)
            || type.StartsWith("body-", StringComparison.Ordinal);

    private static async Task AssertObjectDoesNotCoverTextAsync(IPage page, string objectId, string blockId)
    {
        var probe = await page.EvaluateAsync<ObjectTextOverlapProbe>(
            """
            ([objectId, blockId]) => {
                const object = document.querySelector(`[data-canvas-object][data-object-id="${objectId}"]`);
                const objectRect = object?.getBoundingClientRect();
                const textRects = Array.from(document.querySelectorAll(`[data-canvas-text-rect][data-block-id="${blockId}"]`))
                    .map(node => node.getBoundingClientRect())
                    .filter(rect => rect.width > 0.5 && rect.height > 0.5);
                const overlaps = objectRect
                    ? textRects.filter(rect =>
                        rect.left < objectRect.right - 1
                        && rect.right > objectRect.left + 1
                        && rect.top < objectRect.bottom - 1
                        && rect.bottom > objectRect.top + 1)
                    : [];
                return {
                    objectFound: !!objectRect,
                    textRectCount: textRects.length,
                    overlapCount: overlaps.length
                };
            }
            """,
            new object[] { objectId, blockId });

        Assert.IsTrue(probe.ObjectFound, $"Canvas object metadata must exist for {objectId}.");
        Assert.IsTrue(probe.TextRectCount > 0, $"Canvas text metadata must exist for {blockId}.");
        Assert.AreEqual(0, probe.OverlapCount, $"Canvas object {objectId} must not cover text rects in {blockId}.");
    }

    private static async Task AssertObjectCaptionDoesNotCoverTextAsync(IPage page, string objectId, string blockId)
    {
        var probe = await page.EvaluateAsync<CaptionTextOverlapProbe>(
            """
            async ([objectId, blockId]) => {
                const host = document.querySelector('[data-testid="document-canvas-engine-host"]');
                const handle = host?.getAttribute('data-canvas-engine-handle') || '';
                const module = await import('/_content/Tempo.Blazor.DocumentEditor/js/document-editor-canvas/interop.mjs');
                const debug = JSON.parse(module.getRuntimeDebugSnapshotJson(handle) || '{}');
                const blocks = debug?.render?.selectionLayout?.blocks || [];
                const block = blocks.find(candidate =>
                    String(candidate?.objectId || candidate?.object?.objectId || '') === objectId);
                const caption = block?.captionRect || null;
                const pageIndex = Number(block?.pageIndex || 0) || 0;
                const canvasPage = document.querySelector(`[data-testid="document-canvas-page"][data-page-index="${pageIndex}"]`)
                    || document.querySelector('[data-testid="document-canvas-page"]');
                const pageRect = canvasPage?.getBoundingClientRect();
                const scale = Math.max(0.01, Number(canvasPage?.getAttribute('data-canvas-page-zoom-scale') || '1') || 1);
                const captionRect = caption && pageRect
                    ? {
                        left: pageRect.left + Number(caption.x || 0) * scale,
                        top: pageRect.top + Number(caption.y || 0) * scale,
                        right: pageRect.left + (Number(caption.x || 0) + Number(caption.width || 0)) * scale,
                        bottom: pageRect.top + (Number(caption.y || 0) + Number(caption.height || 0)) * scale
                    }
                    : null;
                const textRects = Array.from(document.querySelectorAll(`[data-canvas-text-rect][data-block-id="${blockId}"]`))
                    .map(node => node.getBoundingClientRect())
                    .filter(rect => rect.width > 0.5 && rect.height > 0.5);
                const overlaps = captionRect
                    ? textRects.filter(rect =>
                        rect.left < captionRect.right - 1
                        && rect.right > captionRect.left + 1
                        && rect.top < captionRect.bottom - 1
                        && rect.bottom > captionRect.top + 1)
                    : [];
                return {
                    captionFound: !!captionRect,
                    textRectCount: textRects.length,
                    overlapCount: overlaps.length
                };
            }
            """,
            new object[] { objectId, blockId });

        Assert.IsTrue(probe.CaptionFound, $"Canvas object caption metadata must exist for {objectId}.");
        Assert.IsTrue(probe.TextRectCount > 0, $"Canvas text metadata must exist for {blockId}.");
        Assert.AreEqual(0, probe.OverlapCount, $"Canvas object caption {objectId} must not cover text rects in {blockId}.");
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
            "phase15-images",
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

    private sealed class ObjectRect
    {
        public double X { get; set; }

        public double Y { get; set; }

        public double Width { get; set; }

        public double Height { get; set; }

        public double CenterX { get; set; }

        public double CenterY { get; set; }
    }

    private sealed class ObjectHitPoint
    {
        public double X { get; set; }

        public double Y { get; set; }

        public double PageX { get; set; }

        public double PageY { get; set; }

        public int PageIndex { get; set; }
    }

    private sealed class ObjectModelLayout
    {
        public double X { get; set; }

        public double Y { get; set; }

        public double Width { get; set; }

        public double Height { get; set; }
    }

    private sealed class ObjectSnapState
    {
        public bool Active { get; set; }

        public bool HasX { get; set; }

        public bool HasY { get; set; }

        public double X { get; set; }

        public double Y { get; set; }

        public string XType { get; set; } = string.Empty;

        public string YType { get; set; } = string.Empty;

        public string XEdge { get; set; } = string.Empty;

        public string YEdge { get; set; } = string.Empty;
    }

    private sealed class Phase15Probe
    {
        public string ModelDocumentId { get; set; } = string.Empty;

        public int ObjectCount { get; set; }

        public int ImageCount { get; set; }

        public int ModelObjectCount { get; set; }

        public int ModelImageCount { get; set; }

        public string MainWrapMode { get; set; } = string.Empty;

        public bool HasDrawingRun { get; set; }

        public bool HasAltWarning { get; set; }

        public string SelectedObjectId { get; set; } = string.Empty;

        public int SelectedHandleCount { get; set; }

        public bool A11yContainsCaption { get; set; }
    }

    private sealed class Phase15CommandProbe
    {
        public bool Changed { get; set; }

        public bool Handled { get; set; }

        public string ObjectId { get; set; } = string.Empty;

        public string Debug { get; set; } = string.Empty;
    }

    private sealed class ObjectTextOverlapProbe
    {
        public bool ObjectFound { get; set; }

        public int TextRectCount { get; set; }

        public int OverlapCount { get; set; }
    }

    private sealed class CaptionTextOverlapProbe
    {
        public bool CaptionFound { get; set; }

        public int TextRectCount { get; set; }

        public int OverlapCount { get; set; }
    }
}
