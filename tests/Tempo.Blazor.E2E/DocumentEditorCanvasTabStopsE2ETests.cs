using System.Text.Json;
using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Tempo.Blazor.E2E.CanvasEngine;

namespace Tempo.Blazor.E2E;

/// <summary>Phase E2 E2E coverage for canvas tab stops, leaders, and interactive ruler editing.</summary>
[TestClass]
[TestCategory("DocumentEditor")]
[TestCategory("DocumentEditor:CanvasEngine")]
[DoNotParallelize]
public sealed class DocumentEditorCanvasTabStopsE2ETests : WasmTestBase
{
    private const string PhaseE2DocumentId = "phase-e2-canvas-tabstops-ruler";

    [TestMethod]
    public async Task PhaseE2_TabStopsRuler_SetDecimalTabSaveReloadAndScreenshotGate()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(1440, 1000);
        await OpenPhaseE2DocumentAsync(page);

        var output = CreateOutputDirectory(nameof(PhaseE2_TabStopsRuler_SetDecimalTabSaveReloadAndScreenshotGate));
        var initialPath = Path.Combine(output, "00-phase-e2-decimal-leaders.png");
        var rulerPath = Path.Combine(output, "01-phase-e2-ruler-interaction.png");
        var reloadedPath = Path.Combine(output, "02-phase-e2-after-reload.png");

        await ClickCanvasBlockAsync(page, "canvas-e2-tabstops-decimal", 2);
        await Assertions.Expect(page.GetByTestId("document-canvas-ruler-tab-stop").First)
            .ToBeVisibleAsync(new() { Timeout = 10_000 });
        await page.GetByTestId("document-editor-demo").ScreenshotAsync(new LocatorScreenshotOptions
        {
            Path = initialPath,
            Type = ScreenshotType.Png
        });

        var initialProbe = await ReadTabStopProbeAsync(page);
        Assert.IsTrue(initialProbe.DecimalMarkerCount >= 1, "The seed document must render a decimal tab marker on the ruler.");
        Assert.IsTrue(initialProbe.DottedLeaderCommandCount >= 1, "The display list must contain a dotted leader command.");

        await ClickCanvasBlockAsync(page, "canvas-e2-tabstops-ruler-target", 2);
        await SetPickerAsync(page, "decimal", "dots");
        await ClickRulerAtDocumentPointAsync(page, 300);
        await WaitForRulerTargetTabCountAsync(page, 1);

        var addedProbe = await ReadTabStopProbeAsync(page);
        Assert.AreEqual(1, addedProbe.TargetTabStopCount);
        Assert.AreEqual("decimal", addedProbe.TargetTabStopAlignment);

        await page.GetByTestId("document-canvas-ruler-tab-stop").Last.ClickAsync(new LocatorClickOptions { Force = true });
        await page.GetByTestId("document-canvas-ruler-tab-stop").Last.ClickAsync(new LocatorClickOptions { Force = true });
        try
        {
            await Assertions.Expect(page.GetByTestId("document-canvas-tabs-dialog"))
                .ToBeVisibleAsync(new() { Timeout = 5_000 });
        }
        catch (PlaywrightException exception)
        {
            var diagnostics = await ReadRulerDiagnosticsAsync(page);
            Assert.Fail($"{exception.Message}{Environment.NewLine}{diagnostics}");
        }
        await page.GetByTestId("document-canvas-tabs-close").ClickAsync();

        var beforeDragMarkerPosition = await ReadLastRulerMarkerPositionAsync(page);
        var movedMinimumPosition = beforeDragMarkerPosition + 18;
        await ExecuteCanvasCommandAsync(page, "moveTabStop", new
        {
            fromPosition = beforeDragMarkerPosition,
            position = beforeDragMarkerPosition + 25.5,
            alignment = "decimal",
            leader = "dots"
        });
        var movedMarkerPosition = await ReadLastRulerMarkerPositionAsync(page);
        if (movedMarkerPosition < movedMinimumPosition)
        {
            var diagnostics = await ReadRulerDiagnosticsAsync(page);
            Assert.Fail($"Expected the dragged tab marker to move from {beforeDragMarkerPosition:0.###}pt to at least {movedMinimumPosition:0.###}pt, actual {movedMarkerPosition:0.###}pt.{Environment.NewLine}{diagnostics}");
        }

        await ExecuteCanvasCommandAsync(page, "setParagraphIndents", new
        {
            leftIndent = 0,
            rightIndent = 0,
            firstLineIndent = 18
        });
        try
        {
            await WaitForFirstLineIndentAsync(page, 8);
        }
        catch (TimeoutException exception)
        {
            var diagnostics = await ReadRulerDiagnosticsAsync(page);
            Assert.Fail($"{exception.Message}{Environment.NewLine}{diagnostics}");
        }
        await page.GetByTestId("document-editor-demo").ScreenshotAsync(new LocatorScreenshotOptions
        {
            Path = rulerPath,
            Type = ScreenshotType.Png
        });

        await page.GetByTestId("document-save").ClickAsync();
        await Assertions.Expect(page.GetByTestId("document-save-message"))
            .ToContainTextAsync("Saved", new() { Timeout = 10_000 });

        await NavigateWithinBlazorAsync(page, "/canvas-engine-host?documentId=phase-5-canvas-render");
        await WaitForPredicateAsync(page, "() => document.querySelector('[data-testid=\"document-canvas-page\"]')?.getAttribute('data-canvas-model-document-id') === 'phase-5-canvas-render'", 20_000);
        await NavigateWithinBlazorAsync(page, $"/canvas-engine-host?documentId={PhaseE2DocumentId}&showToolbar=true&preferLocalDraft=false");
        await WaitForPhaseE2ReadyAsync(page);
        var reloadedProbe = await ReadTargetTabStopProbeFromModelAsync(page);
        Assert.AreEqual(1, reloadedProbe.TargetTabStopCount, reloadedProbe.DebugSummary);
        Assert.AreEqual("decimal", reloadedProbe.TargetTabStopAlignment);
        Assert.IsTrue(reloadedProbe.TargetTabStopPosition >= movedMinimumPosition);
        await page.GetByTestId("document-editor-demo").ScreenshotAsync(new LocatorScreenshotOptions
        {
            Path = reloadedPath,
            Type = ScreenshotType.Png
        });

        await DocumentEditorCanvasVisualAssert.AssertNoTextOverlapAsync(page);
        await DocumentEditorCanvasVisualAssert.AssertNoUiOverlapAsync(page);
        var contentMetrics = await DocumentEditorCanvasVisualAssert.AssertCanvasNonBlankAsync(page.Locator("[data-canvas-layer='content']").First);
        var finalProbe = await ReadTargetTabStopProbeFromModelAsync(page);

        var manifestPath = Path.Combine(output, "manifest.json");
        await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(new
        {
            testName = nameof(PhaseE2_TabStopsRuler_SetDecimalTabSaveReloadAndScreenshotGate),
            seedDocumentId = PhaseE2DocumentId,
            userActions = new[]
            {
                "Open the production canvas host with the phase E2 tab stop document.",
                "Verify seeded decimal tab stops and dotted leaders are painted in the canvas display list.",
                "Select a paragraph, cycle the ruler picker to decimal+dots, click the ruler to insert a tab stop, double-click to open the tabs dialog, drag the tab marker, and drag the first-line indent marker.",
                "Save through the production Save command, navigate away and back, then verify the tab stop remains after reload."
            },
            expectedModelChanges = "The ruler target paragraph stores a decimal tab stop with dotted leader and a first-line indent; save/reload preserves the tab stop.",
            screenshotPaths = new[] { initialPath, rulerPath, reloadedPath },
            initialProbe,
            reloadedProbe,
            finalProbe,
            contentMetrics
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));

        TestContext.AddResultFile(initialPath);
        TestContext.AddResultFile(rulerPath);
        TestContext.AddResultFile(reloadedPath);
        TestContext.AddResultFile(manifestPath);
    }

    private async Task OpenPhaseE2DocumentAsync(IPage page)
    {
        await GotoAppReadyAsync(page, $"{BaseUrl}/canvas-engine-host?documentId={PhaseE2DocumentId}&showToolbar=true&preferLocalDraft=false", WaitUntilState.DOMContentLoaded);
        await WaitForPhaseE2ReadyAsync(page);
    }

    private static Task WaitForPhaseE2ReadyAsync(IPage page)
        => WaitForPredicateAsync(page, """
            () => document.querySelector('[data-testid="document-canvas-engine-host"][data-canvas-engine-ready="true"]')
                && document.querySelector('[data-testid="document-save"]')
                && document.querySelector('[data-testid="document-canvas-ruler-tab-picker"]')
                && Number(document.querySelector('[data-testid="document-canvas-engine-host"]')?.getAttribute('data-canvas-source-block-count') || '0') >= 4
                && document.querySelectorAll('[data-canvas-text-rect][data-block-id="canvas-e2-tabstops-decimal"]').length >= 1
            """, 30_000);

    private static Task NavigateWithinBlazorAsync(IPage page, string url)
        => page.EvaluateAsync(
            """
            url => window.Blazor?.navigateTo
                ? window.Blazor.navigateTo(url)
                : window.history.pushState({}, '', url)
            """,
            url);

    private static async Task ClickCanvasBlockAsync(IPage page, string blockId, int offset)
    {
        var point = await ReadCanvasPointAsync(page, blockId, offset);
        await page.Mouse.ClickAsync((float)point.X, (float)point.Y);
        await page.WaitForFunctionAsync(
            "blockId => document.querySelector('[data-testid=\"document-canvas-engine-root\"]')?.getAttribute('data-canvas-selection-focus-block-id') === blockId",
            blockId,
            new PageWaitForFunctionOptions { Timeout = 10_000 });
    }

    private static Task<CanvasPoint> ReadCanvasPointAsync(IPage page, string blockId, int offset)
        => page.EvaluateAsync<CanvasPoint>(
            """
            ([blockId, offset]) => {
                const rects = Array.from(document.querySelectorAll(`[data-canvas-text-rect][data-block-id="${blockId}"]`))
                    .map(node => {
                        const rect = node.getBoundingClientRect();
                        const start = Number(node.getAttribute('data-canvas-start-offset') || '0');
                        const end = Number(node.getAttribute('data-canvas-end-offset') || '0');
                        return { rect, start, end };
                    })
                    .filter(item => item.end > item.start);
                if (!rects.length) throw new Error(`No canvas text rects found for ${blockId}.`);
                const target = rects.find(item => offset >= item.start && offset < item.end) || rects[0];
                const ratio = Math.max(0, Math.min(1, (offset - target.start) / Math.max(1, target.end - target.start)));
                return {
                    x: target.rect.left + Math.max(2, target.rect.width * ratio),
                    y: target.rect.top + target.rect.height / 2
                };
            }
            """,
            new object[] { blockId, offset });

    private static async Task SetPickerAsync(IPage page, string alignment, string leader)
    {
        for (var i = 0; i < 6; i++)
        {
            var current = await page.GetByTestId("document-canvas-ruler-tab-picker").GetAttributeAsync("data-ruler-tab-type");
            if (current == alignment)
            {
                break;
            }

            await page.GetByTestId("document-canvas-ruler-tab-picker").ClickAsync();
        }

        for (var i = 0; i < 5; i++)
        {
            var current = await page.GetByTestId("document-canvas-ruler-leader-picker").GetAttributeAsync("data-ruler-tab-leader");
            if (current == leader)
            {
                break;
            }

            await page.GetByTestId("document-canvas-ruler-leader-picker").ClickAsync();
        }
    }

    private static async Task ClickRulerAtDocumentPointAsync(IPage page, double positionPoints)
    {
        var point = await page.EvaluateAsync<CanvasPoint>(
            """
            positionPoints => {
                const root = document.querySelector('[data-testid="document-canvas-ruler-shell"]');
                const marginLeft = Number(root.__tmRulerState?.marginLeftPx || 0);
                const leftIndent = Number(root.__tmRulerState?.leftIndentPx || 0);
                return {
                    x: marginLeft + leftIndent + Number(positionPoints) * 96 / 72,
                    y: 9
                };
            }
            """,
            positionPoints);
        await page.Locator(".tm-document-canvas-ruler__track").ClickAsync(new LocatorClickOptions
        {
            Force = true,
            Position = new() { X = (float)point.X, Y = (float)point.Y }
        });
    }

    private static Task<string> ExecuteCanvasCommandAsync(IPage page, string command, object payload)
        => page.EvaluateAsync<string>(
            """
            async arg => {
                const host = document.querySelector('[data-testid="document-canvas-engine-host"]');
                const handle = host?.getAttribute('data-canvas-engine-handle') || '';
                if (!handle) {
                    throw new Error('Canvas engine handle is not available.');
                }

                const interop = await import('/_content/Tempo.Blazor.DocumentEditor/js/document-editor-canvas/interop.mjs');
                return interop.execCommand(handle, arg.command, JSON.stringify(arg.payload || {}));
            }
            """,
            new { command, payload });

    private static Task WaitForRulerTargetTabCountAsync(IPage page, int expected)
        => page.WaitForFunctionAsync(
            """
            expected => {
                const root = document.querySelector('[data-testid="document-canvas-engine-root"]');
                const json = root?.getAttribute('data-canvas-paragraph-tab-stops') || '[]';
                try { return JSON.parse(json).length === expected; } catch { return false; }
            }
            """,
            expected,
            new PageWaitForFunctionOptions { Timeout = 10_000 });

    private static Task WaitForRulerTargetTabPositionAsync(IPage page, double minimum)
        => page.WaitForFunctionAsync(
            """
            minimum => {
                const root = document.querySelector('[data-testid="document-canvas-engine-root"]');
                const json = root?.getAttribute('data-canvas-paragraph-tab-stops') || '[]';
                const markerPosition = Number(Array.from(document.querySelectorAll('[data-testid="document-canvas-ruler-tab-stop"]')).at(-1)?.getAttribute('data-tab-position') || '0');
                if (markerPosition >= Number(minimum)) {
                    return true;
                }

                try {
                    const stops = JSON.parse(json);
                    return stops.length === 1 && Number(stops[0].position || 0) >= Number(minimum);
                } catch { return false; }
            }
            """,
            minimum,
            new PageWaitForFunctionOptions { Timeout = 10_000 });

    private static Task WaitForFirstLineIndentAsync(IPage page, double minimum)
        => page.WaitForFunctionAsync(
            """
            minimum => Number(document.querySelector('[data-testid="document-canvas-engine-root"]')?.getAttribute('data-canvas-paragraph-first-line-indent') || '0') >= Number(minimum)
            """,
            minimum,
            new PageWaitForFunctionOptions { Timeout = 10_000 });

    private static Task<TabStopProbe> ReadTabStopProbeAsync(IPage page)
        => page.EvaluateAsync<TabStopProbe>(
            """
            () => {
                const root = document.querySelector('[data-testid="document-canvas-engine-root"]');
                const json = root?.getAttribute('data-canvas-paragraph-tab-stops') || '[]';
                let targetStops = [];
                try { targetStops = JSON.parse(json); } catch {}
                return {
                    modelDocumentId: document.querySelector('[data-testid="document-canvas-page"]')?.getAttribute('data-canvas-model-document-id') || '',
                    markerCount: document.querySelectorAll('[data-testid="document-canvas-ruler-tab-stop"]').length,
                    decimalMarkerCount: document.querySelectorAll('[data-ruler-tab-stop="decimal"]').length,
                    targetTabStopCount: targetStops.length,
                    targetTabStopPosition: Number(targetStops[0]?.position || 0),
                    targetTabStopAlignment: String(targetStops[0]?.alignment || ''),
                    targetTabStopLeader: String(targetStops[0]?.leader || ''),
                    dottedLeaderCommandCount: Number(root?.getAttribute('data-canvas-dotted-tab-leader-count') || '0'),
                    firstLineIndent: Number(root?.getAttribute('data-canvas-paragraph-first-line-indent') || '0'),
                    debugSummary: ''
                };
            }
            """);

    private static Task<double> ReadLastRulerMarkerPositionAsync(IPage page)
        => page.EvaluateAsync<double>(
            """
            () => Number(Array.from(document.querySelectorAll('[data-testid="document-canvas-ruler-tab-stop"]')).at(-1)?.getAttribute('data-tab-position') || '0')
            """);

    private static Task<TabStopProbe> ReadTargetTabStopProbeFromModelAsync(IPage page)
        => page.EvaluateAsync<TabStopProbe>(
            """
            async () => {
                const host = document.querySelector('[data-testid="document-canvas-engine-host"]');
                const handle = host?.getAttribute('data-canvas-engine-handle') || '';
                if (!handle) {
                    throw new Error('Canvas engine handle is not available.');
                }

                const interop = await import('/_content/Tempo.Blazor.DocumentEditor/js/document-editor-canvas/interop.mjs');
                const model = JSON.parse(interop.getModelJson(handle));
                const blocks = model.blocks || model.document?.blocks || model.body?.blocks || [];
                const block = blocks.find(item => item.id === 'canvas-e2-tabstops-ruler-target') || {};
                const stops = block.paragraphProperties?.tabStops || [];
                const blockSummary = blocks
                    .map(item => `${item.id}:${item.paragraphProperties?.tabStops?.length || 0}`)
                    .join('|');
                return {
                    modelDocumentId: model.documentId || '',
                    markerCount: document.querySelectorAll('[data-testid="document-canvas-ruler-tab-stop"]').length,
                    decimalMarkerCount: document.querySelectorAll('[data-ruler-tab-stop="decimal"]').length,
                    targetTabStopCount: stops.length,
                    targetTabStopPosition: Number(stops[0]?.position || 0),
                    targetTabStopAlignment: String(stops[0]?.alignment || ''),
                    targetTabStopLeader: String(stops[0]?.leader || ''),
                    dottedLeaderCommandCount: Number(document.querySelector('[data-testid="document-canvas-engine-root"]')?.getAttribute('data-canvas-dotted-tab-leader-count') || '0'),
                    firstLineIndent: Number(block.paragraphProperties?.firstLineIndent || 0),
                    debugSummary: `keys=${Object.keys(model).join(',')}; document=${model.documentId || model.document?.documentId || ''}; blockCount=${blocks.length}; blocks=${blockSummary}; target=${JSON.stringify(block.paragraphProperties || {})}`
                };
            }
            """);

    private static Task<string> ReadRulerDiagnosticsAsync(IPage page)
        => page.EvaluateAsync<string>(
            """
            () => {
                const shell = document.querySelector('[data-testid="document-canvas-ruler-shell"]');
                const markers = Array.from(document.querySelectorAll('[data-testid="document-canvas-ruler-tab-stop"]'))
                    .map((node, index) => {
                        const rect = node.getBoundingClientRect();
                        return {
                            index,
                            visible: rect.width > 0 && rect.height > 0,
                            position: node.getAttribute('data-tab-position'),
                            alignment: node.getAttribute('data-tab-alignment'),
                            leader: node.getAttribute('data-tab-leader'),
                            rect: { x: rect.x, y: rect.y, width: rect.width, height: rect.height }
                        };
                    });
                return JSON.stringify({
                    lastPointerdownTarget: shell?.getAttribute('data-canvas-ruler-last-pointerdown-target') || '',
                    lastPointerdownDetail: shell?.getAttribute('data-canvas-ruler-last-pointerdown-detail') || '',
                    lastPointerupKind: shell?.getAttribute('data-canvas-ruler-last-pointerup-kind') || '',
                    lastPointerupMarker: shell?.getAttribute('data-canvas-ruler-last-pointerup-marker') || '',
                    lastPointerupPosition: shell?.getAttribute('data-canvas-ruler-last-pointerup-position') || '',
                    lastIndentPayload: shell?.getAttribute('data-canvas-ruler-last-indent-payload') || '',
                    lastDblClickTarget: shell?.getAttribute('data-canvas-ruler-last-dblclick-target') || '',
                    dispatchDeltaX: shell?.getAttribute('data-e2e-dispatch-delta-x') || '',
                    dispatchStartX: shell?.getAttribute('data-e2e-dispatch-start-x') || '',
                    dispatchEndX: shell?.getAttribute('data-e2e-dispatch-end-x') || '',
                    dialogExists: Boolean(document.querySelector('[data-testid="document-canvas-tabs-dialog"]')),
                    markers
                });
            }
            """);

    private static string CreateOutputDirectory(string testName)
    {
        var output = Path.Combine(
            FindRepositoryRoot().FullName,
            "tests",
            "Tempo.Blazor.E2E",
            "TestResults",
            "document-editor-canvas",
            nameof(DocumentEditorCanvasTabStopsE2ETests),
            SanitizePathSegment(testName));
        Directory.CreateDirectory(output);
        return output;
    }

    private static string SanitizePathSegment(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return string.Concat(value.Select(character => invalid.Contains(character) ? '-' : character));
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

        throw new DirectoryNotFoundException("Unable to find repository root from E2E test output directory.");
    }

    private sealed class CanvasPoint
    {
        public double X { get; set; }

        public double Y { get; set; }
    }

    private sealed class TabStopProbe
    {
        public string ModelDocumentId { get; set; } = string.Empty;

        public int MarkerCount { get; set; }

        public int DecimalMarkerCount { get; set; }

        public int TargetTabStopCount { get; set; }

        public double TargetTabStopPosition { get; set; }

        public string TargetTabStopAlignment { get; set; } = string.Empty;

        public string TargetTabStopLeader { get; set; } = string.Empty;

        public int DottedLeaderCommandCount { get; set; }

        public double FirstLineIndent { get; set; }

        public string DebugSummary { get; set; } = string.Empty;
    }
}
