using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// 2026-06-10 fix gates for the six canvas DocumentEditor problems reported from screen recordings on
/// <c>/document-editor</c> (the "Service agreement" contract demo): sticky bold after toggle-off (P1),
/// slow formatting (P2 baseline), images rendered as grey rectangles (P3), and image drop landing far
/// from the pointer (P6). Rotation (P4) and resize/cursor (P5) gates are added with their fix phases.
///
/// Phase 0 establishes the reproduction: <see cref="FixP1_TogglingBoldOff_StopsTypingBold"/>,
/// <see cref="FixP3_FirstImage_RendersBitmapNotGrey"/> and <see cref="FixP6_DroppedImage_LandsUnderPointer"/>
/// are expected RED before their fix phases and GREEN afterwards;
/// <see cref="FixP2_FormattingLatency_Baseline"/> only records numbers (no hard latency assertion until 2.5).
/// </summary>
[TestClass]
[TestCategory("DocumentEditor")]
[TestCategory("DocumentEditor:CanvasEngine")]
[DoNotParallelize]
public sealed class DocumentEditorCanvasImageFormattingFixE2ETests : WasmTestBase
{
    private const string ContractDocumentId = "contract-demo";
    private const string BodyBlockId = "contract-normal-overview";
    private const string LeftWrapImageId = "contract-left-wrap-image";

    // Dominant colour of the demo PNG asset (DemoDocumentImageUrlResolver.DemoPngDataUrl): a saturated blue.
    private const int DemoBlueR = 52;
    private const int DemoBlueG = 91;
    private const int DemoBlueB = 175;

    [TestInitialize]
    public Task ResetDocumentEditorDemoAsync()
        => DocumentEditorE2EReset.ResetAsync();

    /// <summary>
    /// P1 — select text, make it bold, collapse the caret inside the bold run (toolbar shows bold active),
    /// toggle bold OFF, then type. The freshly typed run must NOT be bold. RED before phase 1 because the
    /// engine's pending-marks set is add-only and cannot suppress the inherited bold.
    /// </summary>
    [TestMethod]
    public async Task FixP1_TogglingBoldOff_StopsTypingBold()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(1440, 1000);
        await OpenContractDocumentAsync(page);

        var output = CreateOutputDirectory("fix-p1-bold");
        await ScreenshotAsync(page, Path.Combine(output, "00-before.png"));

        // 1) Bold "The agree" (offsets 0..9).
        await SelectCanvasTextRangeAsync(page, BodyBlockId, 0, 9);
        await page.GetByTestId("document-bold").ClickAsync();
        await WaitForCommandStateAsync(page, "bold", "active");
        await ScreenshotAsync(page, Path.Combine(output, "01-after-bold.png"));

        // 2) Collapse the caret INSIDE the bold run (offset 4) so the toolbar reports bold active.
        await ClickCanvasOffsetAsync(page, BodyBlockId, 4);
        await WaitForCollapsedCaretAsync(page, BodyBlockId);
        await WaitForCommandStateAsync(page, "bold", "active");

        // 3) Toggle bold OFF, then type a unique marker.
        await page.GetByTestId("document-bold").ClickAsync();
        await page.Keyboard.TypeAsync("Qz9", new KeyboardTypeOptions { Delay = 40 });
        await page.WaitForTimeoutAsync(250);
        await ScreenshotAsync(page, Path.Combine(output, "02-after-type.png"));

        var typed = await ReadRunForTextAsync(page, BodyBlockId, "Qz9");
        await File.WriteAllTextAsync(
            Path.Combine(output, "manifest.json"),
            JsonSerializer.Serialize(new { problem = "P1 sticky bold", typed }, JsonWebIndented));

        Assert.IsTrue(typed.Found, $"Could not find the typed 'Qz9' run in block {BodyBlockId}. Probe: {typed.Debug}");
        Assert.IsFalse(
            typed.Bold,
            $"P1 regression: text typed after toggling bold OFF is still bold. Run text='{typed.Text}', marks=[{typed.Marks}].");
    }

    /// <summary>
    /// P2 baseline — measures the JS-only execCommand cost, the cost of each .NET sync interop roundtrip,
    /// and the full toolbar click→repaint latency. Records the numbers (manifest + test output); no hard
    /// latency assertion here (the perf gate lands with the fix in phase 2.5).
    /// </summary>
    [TestMethod]
    public async Task FixP2_FormattingLatency_Baseline()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(1440, 1000);
        await OpenContractDocumentAsync(page);

        await SelectCanvasTextRangeAsync(page, BodyBlockId, 0, 9);

        var measurement = await page.EvaluateAsync<LatencyBaseline>(
            """
            async () => {
                const host = document.querySelector('[data-testid="document-canvas-engine-host"]');
                const handle = host?.getAttribute('data-canvas-engine-handle') || '';
                const module = await import('/_content/Tempo.Blazor.DocumentEditor/js/document-editor-canvas/interop.mjs');
                const median = values => {
                    const sorted = values.slice().sort((a, b) => a - b);
                    const mid = Math.floor(sorted.length / 2);
                    return sorted.length % 2 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2;
                };
                const time = (fn, runs = 7) => {
                    const samples = [];
                    for (let i = 0; i < runs; i += 1) {
                        const start = performance.now();
                        fn();
                        samples.push(performance.now() - start);
                    }
                    return median(samples);
                };

                const execMs = time(() => module.execCommand(handle, 'bold'));
                const isDirtyMs = time(() => module.isDirty(handle));
                const undoMs = time(() => module.getUndoStateJson(handle));
                const formattingMs = time(() => module.getFormattingStateJson(handle));
                const navigationMs = time(() => module.getNavigationStateJson(handle));
                return { execMs, isDirtyMs, undoMs, formattingMs, navigationMs };
            }
            """);

        // Full user-perceived latency: toolbar Bold click → engine repaints (command revision advances).
        var endToEndSamples = new List<double>();
        for (var i = 0; i < 5; i++)
        {
            var revBefore = await ReadIntAttrAsync(page, "data-canvas-command-revision");
            var start = DateTime.UtcNow;
            await page.GetByTestId("document-bold").ClickAsync();
            await page.WaitForFunctionAsync(
                "rev => Number(document.querySelector('[data-testid=\"document-canvas-engine-root\"]')?.getAttribute('data-canvas-command-revision') || '0') > rev",
                revBefore,
                new PageWaitForFunctionOptions { Timeout = 10_000 });
            endToEndSamples.Add((DateTime.UtcNow - start).TotalMilliseconds);
        }
        endToEndSamples.Sort();
        var endToEndMs = endToEndSamples[endToEndSamples.Count / 2];

        var routeBreakdown = await page.EvaluateAsync<string>(
            """
            () => {
                const root = document.querySelector('[data-render-engine]');
                return JSON.stringify({
                    execMs: Number(root?.getAttribute('data-canvas-route-exec-ms') || '0'),
                    applyMs: Number(root?.getAttribute('data-canvas-route-apply-ms') || '0'),
                    focusMs: Number(root?.getAttribute('data-canvas-route-focus-ms') || '0')
                });
            }
            """);
        TestContext.WriteLine($"P2 ROUTE BREAKDOWN (C#): {routeBreakdown}");

        var output = CreateOutputDirectory("fix-p2-latency");
        await File.WriteAllTextAsync(
            Path.Combine(output, "manifest.json"),
            JsonSerializer.Serialize(new
            {
                problem = "P2 formatting latency baseline",
                jsExecMs = measurement.ExecMs,
                jsIsDirtyMs = measurement.IsDirtyMs,
                jsUndoStateMs = measurement.UndoMs,
                jsFormattingMs = measurement.FormattingMs,
                jsNavigationMs = measurement.NavigationMs,
                endToEndToolbarMs = endToEndMs,
                routeBreakdownCSharp = routeBreakdown,
                note = "endToEnd includes JS exec + .NET route (exec roundtrip + apply uiState + focus) + StateHasChanged."
            }, JsonWebIndented));

        TestContext.WriteLine(
            $"P2 BASELINE: jsExec={measurement.ExecMs:N1}ms isDirty={measurement.IsDirtyMs:N1}ms undo={measurement.UndoMs:N1}ms " +
            $"formatting={measurement.FormattingMs:N1}ms navigation={measurement.NavigationMs:N1}ms endToEndToolbar={endToEndMs:N1}ms");

        // Robust regression gate (the wall-clock is noisy on the WASM/VM runner + inflated by Playwright SlowMo,
        // so we gate the GENUINE C# toolbar-route cost, not the Playwright end-to-end). Phase 2 collapsed the
        // five sequential .NET sync round-trips into the single exec response and dropped most model clones, so
        // the route is ~250-360ms here; the pre-fix path round-tripped ~1.5s. A 800ms budget cleanly separates
        // fixed from a regression back to multi-round-trip sync while tolerating jitter.
        var routeBreakdownState = JsonSerializer.Deserialize<RouteBreakdown>(routeBreakdown) ?? new RouteBreakdown();
        var routeTotal = routeBreakdownState.ExecMs + routeBreakdownState.ApplyMs + routeBreakdownState.FocusMs;
        Assert.IsTrue(endToEndMs > 0, "End-to-end measurement must produce a positive duration.");
        Assert.IsTrue(
            routeTotal > 0 && routeTotal < 800,
            $"P2 regression: toolbar route cost {routeTotal:N0}ms (exec {routeBreakdownState.ExecMs:N0} + apply {routeBreakdownState.ApplyMs:N0} + focus {routeBreakdownState.FocusMs:N0}) exceeded the 800ms budget — the formatting command may have regressed to multi-round-trip sync.");
        // The toolbar formatting readback must be the cheap O(selection) path (formattingOnly), not the full
        // multi-group command-state walk.
        Assert.IsTrue(
            measurement.FormattingMs < 50,
            $"P2 regression: getFormattingStateJson took {measurement.FormattingMs:N1}ms — expected the lightweight formattingOnly query (<50ms).");
    }

    /// <summary>
    /// P3 — the first wrapped contract image must render its real bitmap (the demo PNG is a saturated blue),
    /// not a uniform grey placeholder rectangle. RED before phase 3 because the canvas host never resolves
    /// the asset URL through IDocumentImageUrlResolver, so the engine only paints the grey placeholder.
    /// </summary>
    [TestMethod]
    public async Task FixP3_FirstImage_RendersBitmapNotGrey()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(1440, 1000);
        await OpenContractDocumentAsync(page);
        await WaitForObjectPresentAsync(page, LeftWrapImageId);

        var output = CreateOutputDirectory("fix-p3-image");
        await ScreenshotAsync(page, Path.Combine(output, "00-image.png"));

        // Poll for the demo bitmap's blue pixels on the objects layer (async image.onload + repaint).
        ObjectsLayerSample sample = new();
        var deadline = DateTime.UtcNow.AddSeconds(8);
        do
        {
            sample = await SampleObjectsLayerBlueAsync(page);
            if (sample.BluePixels > 100)
            {
                break;
            }

            await page.WaitForTimeoutAsync(250);
        }
        while (DateTime.UtcNow < deadline);

        await File.WriteAllTextAsync(
            Path.Combine(output, "manifest.json"),
            JsonSerializer.Serialize(new { problem = "P3 grey image", sample }, JsonWebIndented));

        Assert.IsTrue(
            sample.BluePixels > 100,
            $"P3 regression: the first contract image renders as a grey placeholder, not the demo bitmap. " +
            $"Blue pixels found: {sample.BluePixels} of {sample.SampledPixels} sampled (canvas {sample.Width}x{sample.Height}).");
    }

    /// <summary>
    /// P6 — dragging a floating image must land it under the pointer. RED before phase 5 because the drop
    /// stores a body-relative Y while the layout treats the offset as paragraph-relative, so the image jumps
    /// far below the drop point.
    /// </summary>
    [TestMethod]
    public async Task FixP6_DroppedImage_LandsUnderPointer()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(1440, 1000);
        // The lighter canvas-engine-host avoids the heavy /document-editor review/versions chrome whose
        // selection reflow fights synthetic drags; the P6 frame bug lives in the JS engine and reproduces
        // identically here with the same paragraph-anchored contract image.
        await OpenContractOnCanvasHostAsync(page);
        await WaitForObjectPresentAsync(page, LeftWrapImageId);

        var output = CreateOutputDirectory("fix-p6-drop");
        await ScreenshotAsync(page, Path.Combine(output, "00-before.png"));

        var geometry = await page.EvaluateAsync<string>(
            """
            async () => {
                const host = document.querySelector('[data-testid="document-canvas-engine-host"]');
                const handle = host?.getAttribute('data-canvas-engine-handle') || '';
                const module = await import('/_content/Tempo.Blazor.DocumentEditor/js/document-editor-canvas/interop.mjs');
                const debug = JSON.parse(module.getRuntimeDebugSnapshotJson(handle) || '{}');
                const blocks = [...(debug?.render?.selectionLayout?.blocks || []), ...(debug?.layout?.blocks || [])];
                const images = blocks.filter(b => b?.type === 'image' || b?.object || b?.objectId).map(b => ({
                    objectId: b?.objectId || b?.object?.objectId || '', blockId: b?.blockId || '',
                    pageIndex: b?.pageIndex, rect: b?.rect || null
                }));
                return JSON.stringify(images);
            }
            """);
        await File.WriteAllTextAsync(Path.Combine(output, "geometry.json"), geometry);
        TestContext.WriteLine($"P6 IMAGE GEOMETRY: {geometry}");

        // Reflow-aware drag: the mousedown selects the image, which opens the inspector and reflows the canvas.
        // We press at the object centre, let the reflow settle, then re-derive the target viewport point from
        // the POST-reflow page offset so the engine sees a clean page-space move (synthetic input otherwise
        // fights the shift). The move asks for +40,+60 page px.
        const string objectId = LeftWrapImageId;
        const double deltaPageX = 40;
        const double deltaPageY = 60;
        var beforeModel = await ReadObjectModelPositionAsync(page, objectId);
        var before = await ReadObjectLayoutRectAsync(page, objectId);
        var scale = before.Scale <= 0 ? 1 : before.Scale;
        var centerPageX = before.X + before.Width / 2;
        var centerPageY = before.Y + before.Height / 2;

        var pageBefore = await ReadPageViewportOriginAsync(page, before.PageIndex);
        await page.Mouse.MoveAsync((float)(pageBefore.Left + centerPageX * scale), (float)(pageBefore.Top + centerPageY * scale));
        await page.Mouse.DownAsync();
        await page.WaitForTimeoutAsync(450);

        var pageAfter = await ReadPageViewportOriginAsync(page, before.PageIndex);
        double TargetX(double pageX) => pageAfter.Left + pageX * scale;
        double TargetY(double pageY) => pageAfter.Top + pageY * scale;
        await page.Mouse.MoveAsync((float)TargetX(centerPageX + deltaPageX / 2), (float)TargetY(centerPageY + deltaPageY / 2), new MouseMoveOptions { Steps = 5 });
        await page.Mouse.MoveAsync((float)TargetX(centerPageX + deltaPageX), (float)TargetY(centerPageY + deltaPageY), new MouseMoveOptions { Steps = 6 });
        await page.Mouse.UpAsync();
        await page.WaitForTimeoutAsync(500);
        await ScreenshotAsync(page, Path.Combine(output, "01-after-drop.png"));

        var afterModel = await ReadObjectModelPositionAsync(page, objectId);

        // The engine owns the model, so its stored layout offset is the fresh ground truth (the C#-rendered
        // object-metadata node + the cached debug-snapshot layout both lag a live in-engine edit, and the page
        // reflow from the inspector opening confounds screenshot comparison). The fix NUDGES the stored offset
        // by the drag delta, so the model delta must equal the page-space drag delta. The pre-fix body-relative
        // offset stored ~(dropY − bodyY) instead — for this paragraph-anchored image that was ~300px, not ~60.
        var modelDeltaX = afterModel.X - beforeModel.X;
        var modelDeltaY = afterModel.Y - beforeModel.Y;
        var expectedDeltaX = deltaPageX / scale;
        var expectedDeltaY = deltaPageY / scale;
        var offX = Math.Abs(modelDeltaX - expectedDeltaX);
        var offY = Math.Abs(modelDeltaY - expectedDeltaY);

        await File.WriteAllTextAsync(
            Path.Combine(output, "manifest.json"),
            JsonSerializer.Serialize(new
            {
                problem = "P6 drop lands wrong",
                objectId,
                scale,
                beforeModel,
                afterModel,
                modelDeltaX,
                modelDeltaY,
                expectedDeltaX,
                expectedDeltaY,
                offX,
                offY
            }, JsonWebIndented));

        // Snap-to-grid (8px) can nudge the landing a few px; the bug displaced the stored offset by hundreds.
        const double tolerance = 16;
        Assert.IsTrue(
            offX <= tolerance && offY <= tolerance,
            $"P6 regression: dropped image did not land under the pointer. The stored layout offset must move by "
            + $"the drag delta (≈{expectedDeltaX:N0},{expectedDeltaY:N0}), but moved by ({modelDeltaX:N0},{modelDeltaY:N0}); "
            + $"off by ({offX:N0},{offY:N0})px. A body-relative regression would displace it by hundreds of px.");
    }

    /// <summary>
    /// P5 — a selected image must show a resize cursor over its handles and resize without jumping. RED before
    /// phase 6: hover gave no cursor feedback and the resize commit shared the body-relative offset bug.
    /// </summary>
    [TestMethod]
    public async Task FixP5_ImageResize_ShowsCursorAndResizesWithoutJumping()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(1440, 1000);
        await OpenContractOnCanvasHostAsync(page);
        await WaitForObjectPresentAsync(page, LeftWrapImageId);

        var output = CreateOutputDirectory("fix-p5-resize");
        await ScreenshotAsync(page, Path.Combine(output, "00-before.png"));

        // Select the image (reflow-aware: selecting opens the inspector and shifts the canvas).
        const string objectId = LeftWrapImageId;
        var beforeRect = await ReadObjectLayoutRectAsync(page, objectId);
        var scale = beforeRect.Scale <= 0 ? 1 : beforeRect.Scale;
        var pageOrigin = await ReadPageViewportOriginAsync(page, beforeRect.PageIndex);
        await page.Mouse.ClickAsync(
            (float)(pageOrigin.Left + (beforeRect.X + beforeRect.Width / 2) * scale),
            (float)(pageOrigin.Top + (beforeRect.Y + beforeRect.Height / 2) * scale));
        await WaitForObjectSelectedAsync(page, objectId);
        await page.WaitForTimeoutAsync(400);

        var beforeModel = await ReadObjectModelPositionAsync(page, objectId);

        // Hover the SE resize handle and assert the cursor feedback.
        var seHandle = page.Locator($"[data-canvas-object-resize-handle='se'][data-object-id='{objectId}']").First;
        await seHandle.WaitForAsync(new LocatorWaitForOptions { Timeout = 10_000 });
        var handleBox = await seHandle.BoundingBoxAsync();
        Assert.IsNotNull(handleBox, "SE resize handle must expose a bounding box.");
        var handleX = handleBox!.X + handleBox.Width / 2;
        var handleY = handleBox.Y + handleBox.Height / 2;

        await page.Mouse.MoveAsync((float)handleX, (float)handleY);
        await page.WaitForTimeoutAsync(120); // let the rAF-throttled hover cursor apply
        var hoverCursor = await ReadRootCursorAsync(page);
        await ScreenshotAsync(page, Path.Combine(output, "01-hover-se-handle.png"));

        // Resize from the SE handle (+44,+44 viewport). Top-left must stay put (SE only grows w/h).
        await page.Mouse.DownAsync();
        await page.Mouse.MoveAsync((float)(handleX + 22), (float)(handleY + 22), new MouseMoveOptions { Steps = 5 });
        await page.Mouse.MoveAsync((float)(handleX + 44), (float)(handleY + 44), new MouseMoveOptions { Steps = 6 });
        var dragCursor = await ReadRootCursorAsync(page);
        await page.Mouse.UpAsync();
        await page.WaitForTimeoutAsync(400);
        await ScreenshotAsync(page, Path.Combine(output, "02-after-resize.png"));

        var afterModel = await ReadObjectModelPositionAsync(page, objectId);
        var widthDelta = afterModel.Width - beforeModel.Width;
        var posDx = Math.Abs(afterModel.X - beforeModel.X);
        var posDy = Math.Abs(afterModel.Y - beforeModel.Y);

        await File.WriteAllTextAsync(
            Path.Combine(output, "manifest.json"),
            JsonSerializer.Serialize(new { problem = "P5 resize + cursor", hoverCursor, dragCursor, beforeModel, afterModel, widthDelta, posDx, posDy }, JsonWebIndented));

        Assert.AreEqual("nwse-resize", hoverCursor, "Hovering the SE handle must show the diagonal resize cursor.");
        Assert.AreEqual("nwse-resize", dragCursor, "While resizing from the SE handle the diagonal resize cursor must persist.");
        Assert.IsTrue(widthDelta > 20, $"SE resize must grow the image width (delta {widthDelta:N0}).");
        // SE resize keeps the top-left anchored — no jump (snap-to-grid tolerance).
        Assert.IsTrue(posDx <= 16 && posDy <= 16, $"SE resize must not move the image top-left (moved {posDx:N0},{posDy:N0}px).");
    }

    /// <summary>
    /// P4 — a selected image must rotate: the bitmap turns, the selection frame + handles turn WITH it (one
    /// frame, no un-rotated ghost), and the rotation is in the model. RED before phase 7: paintImageObject
    /// ignored rotation and the overlay handles stayed axis-aligned beside a rotated outline.
    /// </summary>
    [TestMethod]
    public async Task FixP4_ImageRotation_RotatesBitmapAndFrameTogether()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(1440, 1000);
        await OpenContractOnCanvasHostAsync(page);
        await WaitForObjectPresentAsync(page, LeftWrapImageId);

        var output = CreateOutputDirectory("fix-p4-rotation");
        await ScreenshotAsync(page, Path.Combine(output, "00-before.png"));

        const string objectId = LeftWrapImageId;
        var rect = await ReadObjectLayoutRectAsync(page, objectId);
        var scale = rect.Scale <= 0 ? 1 : rect.Scale;
        var origin = await ReadPageViewportOriginAsync(page, rect.PageIndex);
        await page.Mouse.ClickAsync(
            (float)(origin.Left + (rect.X + rect.Width / 2) * scale),
            (float)(origin.Top + (rect.Y + rect.Height / 2) * scale));
        await WaitForObjectSelectedAsync(page, objectId);
        await page.WaitForTimeoutAsync(300);

        // Rotate 30° through the same updateImageLayout command the rotate handle drives.
        var exec = await ExecuteCanvasCommandAsync(page, "updateImageLayout", new { objectId, rotation = 30 });
        Assert.IsTrue(exec.Changed, $"rotate command must change the model: {exec.Debug}");

        await page.WaitForFunctionAsync(
            """
            () => {
                const root = document.querySelector('[data-testid="document-canvas-engine-root"][data-page-surface-strategy="canvas-per-visible-page"]')
                    || document.querySelector('[data-testid="document-canvas-engine-root"]');
                return Math.abs(Number(root?.getAttribute('data-canvas-object-rotation') || '0') - 30) < 0.6;
            }
            """,
            new PageWaitForFunctionOptions { Timeout = 10_000 });
        var overlayRotation = await ReadOverlayRotationAsync(page);
        await ScreenshotAsync(page, Path.Combine(output, "01-rotated.png"));

        var modelRotation = await page.EvaluateAsync<double>(
            """
            objectId => {
                const host = document.querySelector('[data-testid="document-canvas-engine-host"]');
                const handle = host?.getAttribute('data-canvas-engine-handle') || '';
                return import('/_content/Tempo.Blazor.DocumentEditor/js/document-editor-canvas/interop.mjs').then(module => {
                    const model = JSON.parse(module.getModelJson(handle) || '{}');
                    for (const block of model?.body?.blocks || []) {
                        for (const run of block?.content?.runs || []) {
                            if (run?.drawing && String(run.drawing.objectId ?? '') === objectId) {
                                return Number(run.drawing.layout?.transform?.rotation ?? 0) || 0;
                            }
                        }
                    }
                    return 0;
                });
            }
            """,
            objectId);

        // Exactly ONE selection outline (the rotated frame) carrying its eight directional resize handles, and
        // NO second un-rotated set beside it (the frame + handles rotate together inside one container).
        var outlineCount = await page.Locator($"[data-testid='document-canvas-object-selection'][data-object-id='{objectId}']").CountAsync();
        var resizeHandleCount = await page.Locator($"[data-canvas-object-resize-handle][data-object-id='{objectId}']:not([data-canvas-object-resize-handle='rotate'])").CountAsync();

        await File.WriteAllTextAsync(
            Path.Combine(output, "manifest.json"),
            JsonSerializer.Serialize(new { problem = "P4 rotation", modelRotation, overlayRotation, outlineCount, resizeHandleCount }, JsonWebIndented));

        Assert.IsTrue(Math.Abs(modelRotation - 30) < 0.6, $"model rotation must be 30°, was {modelRotation}.");
        Assert.IsTrue(Math.Abs(overlayRotation - 30) < 0.6, $"selection overlay must reflect the 30° rotation (so the frame + handles turn with the bitmap), was {overlayRotation}.");
        Assert.AreEqual(1, outlineCount, "there must be exactly one (rotated) selection frame — no un-rotated ghost.");
        Assert.AreEqual(8, resizeHandleCount, "the rotated frame must still carry its eight directional resize handles.");
    }

    // ---- helpers ----

    private static Task<double> ReadOverlayRotationAsync(IPage page)
        => page.EvaluateAsync<double>(
            """
            () => {
                const root = document.querySelector('[data-testid="document-canvas-engine-root"][data-page-surface-strategy="canvas-per-visible-page"]')
                    || document.querySelector('[data-testid="document-canvas-engine-root"]');
                return Number(root?.getAttribute('data-canvas-object-rotation') || '0') || 0;
            }
            """);

    private static async Task<CanvasCommandResult> ExecuteCanvasCommandAsync(IPage page, string commandId, object payload)
    {
        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        return await page.EvaluateAsync<CanvasCommandResult>(
            """
            ([commandId, json]) => {
                const host = document.querySelector('[data-testid="document-canvas-engine-host"]');
                const handle = host?.getAttribute('data-canvas-engine-handle') || '';
                return import('/_content/Tempo.Blazor.DocumentEditor/js/document-editor-canvas/interop.mjs').then(module => {
                    const parsed = JSON.parse(module.execCommand(handle, commandId, json) || '{}');
                    return { changed: parsed?.result?.changed === true, debug: JSON.stringify(parsed).slice(0, 400) };
                });
            }
            """,
            new object[] { commandId, json });
    }

    private async Task OpenContractDocumentAsync(IPage page)
    {
        await page.GotoAsync($"{BaseUrl}/document-editor?documentId={ContractDocumentId}", new PageGotoOptions
        {
            WaitUntil = WaitUntilState.Load,
            Timeout = 120_000
        });
        var host = page.GetByTestId("document-canvas-engine-host");
        await Assertions.Expect(host).ToBeVisibleAsync(new() { Timeout = 120_000 });
        await Assertions.Expect(host).ToHaveAttributeAsync("data-canvas-engine-ready", "true", new() { Timeout = 120_000 });
        await page.WaitForFunctionAsync(
            """
            blockId => document.querySelectorAll(`[data-canvas-text-rect][data-block-id="${blockId}"]`).length >= 1
                && document.querySelector('[data-testid="document-bold"]')
            """,
            BodyBlockId,
            new PageWaitForFunctionOptions { Timeout = 60_000 });
    }

    private async Task OpenContractOnCanvasHostAsync(IPage page)
    {
        await page.GotoAsync($"{BaseUrl}/canvas-engine-host?documentId={ContractDocumentId}&showToolbar=true&preferLocalDraft=false", new PageGotoOptions
        {
            WaitUntil = WaitUntilState.Load,
            Timeout = 120_000
        });
        var host = page.GetByTestId("document-canvas-engine-host");
        await Assertions.Expect(host).ToBeVisibleAsync(new() { Timeout = 120_000 });
        await Assertions.Expect(host).ToHaveAttributeAsync("data-canvas-engine-ready", "true", new() { Timeout = 120_000 });
        await page.WaitForFunctionAsync(
            "objectId => !!document.querySelector(`[data-canvas-object][data-object-id=\"${objectId}\"]`)",
            LeftWrapImageId,
            new PageWaitForFunctionOptions { Timeout = 60_000 });
    }

    private static Task WaitForObjectSelectedAsync(IPage page, string objectId)
        => page.WaitForFunctionAsync(
            """
            objectId => {
                const root = document.querySelector('[data-testid="document-canvas-engine-root"][data-page-surface-strategy="canvas-per-visible-page"]')
                    || document.querySelector('[data-testid="document-canvas-engine-root"]');
                return root?.getAttribute('data-canvas-object-selected') === 'true'
                    && root?.getAttribute('data-canvas-object-id') === objectId;
            }
            """,
            objectId,
            new PageWaitForFunctionOptions { Timeout = 10_000 });

    private static Task<string> ReadRootCursorAsync(IPage page)
        => page.EvaluateAsync<string>(
            """
            () => {
                const root = document.querySelector('[data-testid="document-canvas-engine-root"][data-page-surface-strategy="canvas-per-visible-page"]')
                    || document.querySelector('[data-testid="document-canvas-engine-root"]');
                return (root && root.style && root.style.cursor) || '';
            }
            """);

    private static Task WaitForObjectPresentAsync(IPage page, string objectId)
        => page.WaitForFunctionAsync(
            "objectId => !!document.querySelector(`[data-canvas-object][data-object-id=\"${objectId}\"]`)",
            objectId,
            new PageWaitForFunctionOptions { Timeout = 30_000 });

    private static async Task SelectCanvasTextRangeAsync(IPage page, string blockId, int startOffset, int endOffset)
    {
        var target = await ReadCanvasTextRangeAsync(page, blockId, startOffset, endOffset);
        await page.Mouse.MoveAsync((float)target.StartX, (float)target.StartY);
        await page.Mouse.DownAsync();
        await page.Mouse.MoveAsync((float)target.EndX, (float)target.EndY, new MouseMoveOptions { Steps = 10 });
        await page.Mouse.UpAsync();
        await page.WaitForFunctionAsync(
            """
            blockId => {
                const root = document.querySelector('[data-testid="document-canvas-engine-root"]');
                return root?.getAttribute('data-canvas-selection-collapsed') === 'false'
                    && root?.getAttribute('data-canvas-selection-focus-block-id') === blockId
                    && document.querySelectorAll('[data-testid="document-canvas-selection-rect"]').length >= 1;
            }
            """,
            blockId,
            new PageWaitForFunctionOptions { Timeout = 10_000 });
    }

    private static async Task ClickCanvasOffsetAsync(IPage page, string blockId, int offset)
    {
        var target = await ReadCanvasTextRangeAsync(page, blockId, offset, offset + 1);
        await page.Mouse.ClickAsync((float)target.StartX, (float)target.StartY);
    }

    private static Task WaitForCollapsedCaretAsync(IPage page, string blockId)
        => page.WaitForFunctionAsync(
            """
            blockId => {
                const root = document.querySelector('[data-testid="document-canvas-engine-root"]');
                return root?.getAttribute('data-canvas-selection-collapsed') === 'true'
                    && root?.getAttribute('data-canvas-selection-focus-block-id') === blockId;
            }
            """,
            blockId,
            new PageWaitForFunctionOptions { Timeout = 10_000 });

    private static Task<CanvasTextRange> ReadCanvasTextRangeAsync(IPage page, string blockId, int startOffset, int endOffset)
        => page.EvaluateAsync<CanvasTextRange>(
            """
            ([blockId, startOffset, endOffset]) => {
                const items = Array.from(document.querySelectorAll(`[data-canvas-text-rect][data-block-id="${blockId}"]`))
                    .map(node => ({
                        node,
                        start: Number(node.getAttribute('data-canvas-start-offset') || '0'),
                        end: Number(node.getAttribute('data-canvas-end-offset') || '0')
                    }))
                    .filter(item => item.end > item.start);
                if (!items.length) throw new Error(`No canvas text rects found for ${blockId}.`);
                const startItem = items.find(item => startOffset >= item.start && startOffset < item.end) || items[0];
                const endItem = items.find(item => endOffset > item.start && endOffset <= item.end) || items[items.length - 1];
                // The canvas is virtualized: a mounted text rect can sit below the fold, so read the rectangles
                // only after centering the selection's midpoint — otherwise the drag lands outside the viewport
                // (same fix as commit 7bc53945 in the inline-format/Ux suites).
                const midIndex = Math.floor((items.indexOf(startItem) + items.indexOf(endItem)) / 2);
                (items[midIndex] || startItem).node.scrollIntoView({ block: 'center', inline: 'center', behavior: 'instant' });
                const rects = items.map(item => ({ rect: item.node.getBoundingClientRect(), start: item.start, end: item.end }));
                const startRect = rects.find(item => startOffset >= item.start && startOffset < item.end) || rects[0];
                const endRect = rects.find(item => endOffset > item.start && endOffset <= item.end) || rects[rects.length - 1];
                const ratio = (offset, item) => Math.max(0, Math.min(1, (offset - item.start) / Math.max(1, item.end - item.start)));
                return {
                    startX: startRect.rect.left + Math.max(1, startRect.rect.width * ratio(startOffset, startRect)),
                    startY: startRect.rect.top + startRect.rect.height / 2,
                    endX: endRect.rect.left + Math.max(1, endRect.rect.width * ratio(endOffset, endRect)),
                    endY: endRect.rect.top + endRect.rect.height / 2,
                    expectedText: ''
                };
            }
            """,
            new object[] { blockId, startOffset, endOffset });

    private static Task WaitForCommandStateAsync(IPage page, string commandId, string expected)
        => page.WaitForFunctionAsync(
            """
            ([commandId, expected]) => {
                const root = document.querySelector('[data-testid="document-canvas-engine-root"]');
                return root?.getAttribute(`data-canvas-command-${commandId}-state`) === expected;
            }
            """,
            new object[] { commandId, expected },
            new PageWaitForFunctionOptions { Timeout = 10_000 });

    private static Task<TypedRunProbe> ReadRunForTextAsync(IPage page, string blockId, string needle)
        => page.EvaluateAsync<TypedRunProbe>(
            """
            ([blockId, needle]) => {
                const host = document.querySelector('[data-testid="document-canvas-engine-host"]');
                const handle = host?.getAttribute('data-canvas-engine-handle') || '';
                return import('/_content/Tempo.Blazor.DocumentEditor/js/document-editor-canvas/interop.mjs').then(module => {
                    const model = JSON.parse(module.getModelJson(handle) || '{}');
                    const blocks = model?.body?.blocks || [];
                    const block = blocks.find(candidate => String(candidate?.id || '') === blockId);
                    const runs = block?.content?.runs || [];
                    const run = runs.find(candidate => String(candidate?.text || '').includes(needle));
                    if (!run) {
                        return { found: false, bold: false, text: '', marks: '', debug: `runs=${runs.map(r => JSON.stringify(r.text)).join(',')}` };
                    }
                    const marks = Array.isArray(run.marks) ? run.marks : [];
                    const bold = marks.some(mark => String(mark?.type || '').toLowerCase() === 'bold');
                    return { found: true, bold, text: String(run.text || ''), marks: marks.map(m => m.type).join(','), debug: '' };
                });
            }
            """,
            new object[] { blockId, needle });

    // Current viewport top-left of a canvas page (after any reflow), used to map page-space points to clicks.
    private static Task<PageOrigin> ReadPageViewportOriginAsync(IPage page, int pageIndex)
        => page.EvaluateAsync<PageOrigin>(
            """
            pageIndex => {
                const pageEl = document.querySelector(`[data-testid="document-canvas-page"][data-page-index="${pageIndex}"]`)
                    || document.querySelector('[data-testid="document-canvas-page"]');
                const rect = pageEl.getBoundingClientRect();
                return { left: rect.left, top: rect.top };
            }
            """,
            pageIndex);

    // Authoritative stored layout position (body-relative offset) from the model — always fresh after a commit.
    private static Task<ObjectLayoutRect> ReadObjectModelPositionAsync(IPage page, string objectId)
        => page.EvaluateAsync<ObjectLayoutRect>(
            """
            async objectId => {
                const host = document.querySelector('[data-testid="document-canvas-engine-host"]');
                const handle = host?.getAttribute('data-canvas-engine-handle') || '';
                const module = await import('/_content/Tempo.Blazor.DocumentEditor/js/document-editor-canvas/interop.mjs');
                const model = JSON.parse(module.getModelJson(handle) || '{}');
                let source = null;
                for (const block of model?.body?.blocks || []) {
                    const image = block?.content?.image;
                    if (image && String(image.objectId ?? block.id ?? '') === objectId) { source = image; break; }
                    for (const run of block?.content?.runs || []) {
                        if (run?.drawing && String(run.drawing.objectId ?? '') === objectId) { source = run.drawing; break; }
                    }
                    if (source) break;
                }
                if (!source) throw new Error(`Model image source not found: ${objectId}`);
                const layout = source.layout || {};
                const position = layout.position || {};
                const transform = layout.transform || {};
                return {
                    x: Number(position.x ?? 0) || 0,
                    y: Number(position.y ?? 0) || 0,
                    width: Number(transform.width ?? 0) || 0,
                    height: Number(transform.height ?? 0) || 0,
                    pageIndex: 0,
                    scale: 1
                };
            }
            """,
            objectId);

    // Resolved on-screen rect of an object in PAGE coordinates, derived from the [data-canvas-object] metadata
    // node (the reliable post-move painted position used by the phase-15 image test) and the page origin read
    // atomically in the same evaluation so it stays correct across reflows.
    private static async Task<ObjectLayoutRect> ReadObjectLayoutRectAsync(IPage page, string objectId)
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

        return await page.EvaluateAsync<ObjectLayoutRect>(
            """
            objectId => {
                const node = document.querySelector(`[data-canvas-object][data-object-id="${objectId}"]`);
                const nodeRect = node.getBoundingClientRect();
                const pageEl = node.closest('[data-testid="document-canvas-page"]')
                    || document.querySelector('[data-testid="document-canvas-page"]');
                const pageRect = pageEl.getBoundingClientRect();
                const pageIndex = Number(pageEl.getAttribute('data-page-index') || '0') || 0;
                const scale = Math.max(0.01, Number(pageEl.getAttribute('data-canvas-page-zoom-scale') || '1') || 1);
                return {
                    x: (nodeRect.left - pageRect.left) / scale,
                    y: (nodeRect.top - pageRect.top) / scale,
                    width: nodeRect.width / scale,
                    height: nodeRect.height / scale,
                    pageIndex,
                    scale
                };
            }
            """,
            objectId);
    }

    private static Task<ObjectsLayerSample> SampleObjectsLayerBlueAsync(IPage page)
        => page.EvaluateAsync<ObjectsLayerSample>(
            """
            ([r, g, b]) => {
                const canvas = document.querySelector("[data-canvas-layer='objects']");
                if (!canvas) return { width: 0, height: 0, sampledPixels: 0, bluePixels: 0 };
                const ctx = canvas.getContext('2d');
                const width = canvas.width;
                const height = canvas.height;
                let bluePixels = 0;
                let sampled = 0;
                const data = ctx.getImageData(0, 0, width, height).data;
                for (let i = 0; i < data.length; i += 4) {
                    const pr = data[i], pg = data[i + 1], pb = data[i + 2], pa = data[i + 3];
                    if (pa < 16) continue;
                    sampled += 1;
                    if (Math.abs(pr - r) < 60 && Math.abs(pg - g) < 60 && Math.abs(pb - b) < 70 && pb > pr + 25 && pb > pg + 15) {
                        bluePixels += 1;
                    }
                }
                return { width, height, sampledPixels: sampled, bluePixels };
            }
            """,
            new object[] { DemoBlueR, DemoBlueG, DemoBlueB });

    private static async Task<int> ReadIntAttrAsync(IPage page, string attr)
        => await page.EvaluateAsync<int>(
            $"() => Number(document.querySelector('[data-testid=\"document-canvas-engine-root\"]')?.getAttribute('{attr}') || '0')");

    private static Task ScreenshotAsync(IPage page, string path)
        => page.GetByTestId("document-editor-demo").ScreenshotAsync(new LocatorScreenshotOptions
        {
            Path = path,
            Type = ScreenshotType.Png
        });

    private static string CreateOutputDirectory(string scenario)
    {
        var output = Path.Combine(
            FindRepositoryRoot().FullName,
            "tests", "Tempo.Blazor.E2E", "TestResults", "document-editor-canvas",
            "image-formatting-fix", "2026-06-10", scenario);
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

    private static readonly JsonSerializerOptions JsonWebIndented =
        new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private sealed class CanvasTextRange
    {
        [JsonPropertyName("startX")] public double StartX { get; set; }
        [JsonPropertyName("startY")] public double StartY { get; set; }
        [JsonPropertyName("endX")] public double EndX { get; set; }
        [JsonPropertyName("endY")] public double EndY { get; set; }
        [JsonPropertyName("expectedText")] public string ExpectedText { get; set; } = string.Empty;
    }

    private sealed class TypedRunProbe
    {
        [JsonPropertyName("found")] public bool Found { get; set; }
        [JsonPropertyName("bold")] public bool Bold { get; set; }
        [JsonPropertyName("text")] public string Text { get; set; } = string.Empty;
        [JsonPropertyName("marks")] public string Marks { get; set; } = string.Empty;
        [JsonPropertyName("debug")] public string Debug { get; set; } = string.Empty;
    }

    private sealed class PageOrigin
    {
        [JsonPropertyName("left")] public double Left { get; set; }
        [JsonPropertyName("top")] public double Top { get; set; }
    }

    private sealed class ObjectLayoutRect
    {
        [JsonPropertyName("x")] public double X { get; set; }
        [JsonPropertyName("y")] public double Y { get; set; }
        [JsonPropertyName("width")] public double Width { get; set; }
        [JsonPropertyName("height")] public double Height { get; set; }
        [JsonPropertyName("pageIndex")] public int PageIndex { get; set; }
        [JsonPropertyName("scale")] public double Scale { get; set; }
    }

    private sealed class ObjectsLayerSample
    {
        [JsonPropertyName("width")] public int Width { get; set; }
        [JsonPropertyName("height")] public int Height { get; set; }
        [JsonPropertyName("sampledPixels")] public int SampledPixels { get; set; }
        [JsonPropertyName("bluePixels")] public int BluePixels { get; set; }
    }

    private sealed class LatencyBaseline
    {
        [JsonPropertyName("execMs")] public double ExecMs { get; set; }
        [JsonPropertyName("isDirtyMs")] public double IsDirtyMs { get; set; }
        [JsonPropertyName("undoMs")] public double UndoMs { get; set; }
        [JsonPropertyName("formattingMs")] public double FormattingMs { get; set; }
        [JsonPropertyName("navigationMs")] public double NavigationMs { get; set; }
    }

    private sealed class CanvasCommandResult
    {
        [JsonPropertyName("changed")] public bool Changed { get; set; }
        [JsonPropertyName("debug")] public string Debug { get; set; } = string.Empty;
    }

    private sealed class RouteBreakdown
    {
        [JsonPropertyName("execMs")] public double ExecMs { get; set; }
        [JsonPropertyName("applyMs")] public double ApplyMs { get; set; }
        [JsonPropertyName("focusMs")] public double FocusMs { get; set; }
    }
}
