using System.Text.Json;
using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Tempo.Blazor.E2E.CanvasEngine;

namespace Tempo.Blazor.E2E;

/// <summary>Phase E10 E2E coverage for canvas autocorrect, autoformat, format painter, symbols, undo, save, and reload.</summary>
[TestClass]
[TestCategory("DocumentEditor")]
[TestCategory("DocumentEditor:CanvasEngine")]
[DoNotParallelize]
public sealed class DocumentEditorCanvasAutocorrectE2ETests : WasmTestBase
{
    private const string PhaseE10DocumentId = "phase-e10-canvas-autocorrect-formatpainter";

    [TestMethod]
    public async Task PhaseE10_AutocorrectFormatPainterSymbolsUndoSaveAndReload()
    {
        await DocumentEditorE2EReset.ResetAsync();
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        page.Console += (_, message) => TestContext.WriteLine($"[browser:{message.Type}] {message.Text}");
        page.PageError += (_, error) => TestContext.WriteLine($"[page-error] {error}");
        await page.SetViewportSizeAsync(1440, 1000);
        await OpenPhaseE10DocumentAsync(page);

        var output = CreateOutputDirectory("phasee10-autocorrect-format-painter");
        var beforePath = Path.Combine(output, "00-phasee10-before.png");
        var symbolMenuPath = Path.Combine(output, "01-phasee10-symbol-menu.png");
        var afterPath = Path.Combine(output, "02-phasee10-after-reload.png");

        await page.GetByTestId("document-editor-demo").ScreenshotAsync(new LocatorScreenshotOptions
        {
            Path = beforePath,
            Type = ScreenshotType.Png
        });

        await ClickTextEndAsync(page, "canvas-e10-autocorrect-target");
        await page.Keyboard.TypeAsync("--");
        await WaitForMirrorBlockTextAsync(page, "canvas-e10-autocorrect-target", "Dash: —");
        var autocorrectUndo = await ExecuteCanvasCommandAsync(page, "undo", new { });
        Assert.IsTrue(autocorrectUndo.Handled && autocorrectUndo.Changed, autocorrectUndo.Debug);
        await WaitForProbeAsync(page, probe => probe.AutocorrectText == "Dash: --");
        var autocorrectRedo = await ExecuteCanvasCommandAsync(page, "redo", new { });
        Assert.IsTrue(autocorrectRedo.Handled && autocorrectRedo.Changed, autocorrectRedo.Debug);
        await WaitForProbeAsync(page, probe => probe.AutocorrectText == "Dash: —");

        await ClickTextEndAsync(page, "canvas-e10-list-target");
        await page.Keyboard.TypeAsync(" ");
        await WaitForProbeAsync(page, probe => probe.ListBlockType == "list" && probe.ListText.Length == 0);

        await ClickTextEndAsync(page, "canvas-e10-link-target");
        await page.Keyboard.TypeAsync(" ");
        await WaitForProbeAsync(page, probe => probe.LinkMarkCount >= 1);

        await SelectCanvasTextRangeAsync(page, "canvas-e10-painter-source", 0, 6);
        var copied = await ExecuteCanvasCommandAsync(page, "copyFormatting", new { });
        Assert.IsTrue(copied.Handled, copied.Debug);
        Assert.IsTrue(copied.FormatPainterActive, copied.Debug);

        await SelectCanvasTextRangeAsync(page, "canvas-e10-painter-target", 0, 6);
        var pasted = await ExecuteCanvasCommandAsync(page, "pasteFormatting", new { });
        Assert.IsTrue(pasted.Handled && pasted.Changed, pasted.Debug);
        await WaitForProbeAsync(page, probe => probe.PainterTargetBold && probe.PainterTargetAlignment.Contains("Right", StringComparison.OrdinalIgnoreCase));

        await ClickTextEndAsync(page, "canvas-e10-symbol-target");
        await page.GetByTestId("document-ribbon-tab-insert").ClickAsync();
        await InsertSymbolFromPaletteAsync(page, "document-symbol-em-dash", symbolMenuPath);
        await InsertSymbolFromPaletteAsync(page, "document-symbol-en-dash");
        await InsertSymbolFromPaletteAsync(page, "document-symbol-non-breaking-space");
        await InsertSymbolFromPaletteAsync(page, "document-symbol-optional-hyphen");
        await InsertSymbolFromPaletteAsync(page, "document-emoji-check");
        await WaitForProbeAsync(page, probe => probe.SymbolText.Contains("—–", StringComparison.Ordinal)
            && probe.SymbolText.Contains("\u00A0", StringComparison.Ordinal)
            && probe.SymbolText.Contains("\u00AD", StringComparison.Ordinal)
            && probe.SymbolText.Contains("✓", StringComparison.Ordinal));

        await DocumentEditorCanvasVisualAssert.AssertNoTextOverlapAsync(page);
        await DocumentEditorCanvasVisualAssert.AssertNoUiOverlapAsync(page);
        var contentMetrics = await DocumentEditorCanvasVisualAssert.AssertCanvasNonBlankAsync(page.Locator("[data-canvas-layer='content']").First);

        await page.GetByTestId("document-save").ClickAsync();
        await WaitForSaveBoundaryAsync(page);
        await NavigateWithinBlazorAsync(page, "/canvas-engine-host?documentId=phase-5-canvas-render");
        await page.WaitForFunctionAsync(
            "() => document.querySelector('[data-testid=\"document-canvas-page\"]')?.getAttribute('data-canvas-model-document-id') === 'phase-5-canvas-render'",
            new PageWaitForFunctionOptions { Timeout = 20_000 });
        await NavigateWithinBlazorAsync(page, $"/canvas-engine-host?documentId={PhaseE10DocumentId}&showToolbar=true");
        await WaitForPhaseE10ReadyAsync(page);
        await WaitForProbeAsync(page, probe => probe.AutocorrectText == "Dash: —"
            && probe.ListBlockType == "list"
            && probe.LinkMarkCount >= 1
            && probe.PainterTargetBold
            && probe.SymbolText.Contains("✓", StringComparison.Ordinal));

        var reloadedProbe = await ReadProbeAsync(page);
        await page.GetByTestId("document-editor-demo").ScreenshotAsync(new LocatorScreenshotOptions
        {
            Path = afterPath,
            Type = ScreenshotType.Png
        });

        var manifestPath = Path.Combine(output, "manifest.json");
        await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(new
        {
            testName = nameof(PhaseE10_AutocorrectFormatPainterSymbolsUndoSaveAndReload),
            seedDocumentId = PhaseE10DocumentId,
            userActions = new[]
            {
                "Open the phase E10 canvas seed document.",
                "Type -- with the real keyboard and verify autocorrect, undo and redo.",
                "Type a space after 1. to autoformat a numbered list and after a URL to create an inline hyperlink.",
                "Copy formatting from a styled source selection, paste it onto another selection, insert special symbols through the Blazor Insert ribbon palette, save, reload, and verify persistence."
            },
            expectedVisibleChanges = "Autocorrect feels native while typing, format painter transfers both run marks and paragraph style, and inserted symbols persist after provider save/reload.",
            screenshotPaths = new[] { beforePath, symbolMenuPath, afterPath },
            reloadedProbe,
            autocorrectUndo,
            autocorrectRedo,
            copied,
            pasted,
            contentMetrics
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));

        TestContext.AddResultFile(beforePath);
        TestContext.AddResultFile(symbolMenuPath);
        TestContext.AddResultFile(afterPath);
        TestContext.AddResultFile(manifestPath);
    }

    private async Task OpenPhaseE10DocumentAsync(IPage page)
    {
        await page.GotoAsync($"{BaseUrl}/canvas-engine-host?documentId={PhaseE10DocumentId}&showToolbar=true", new PageGotoOptions
        {
            WaitUntil = WaitUntilState.Load,
            Timeout = 60_000
        });
        await WaitForPhaseE10ReadyAsync(page);
    }

    private static Task WaitForPhaseE10ReadyAsync(IPage page)
        => page.WaitForFunctionAsync(
            """
            () => {
                const hostReady = document.querySelector('[data-testid="document-canvas-engine-host"]')?.getAttribute('data-canvas-engine-ready') === 'true';
                const first = document.querySelector('[data-testid="document-canvas-page"]');
                return hostReady
                    && first?.getAttribute('data-canvas-model-document-id') === 'phase-e10-canvas-autocorrect-formatpainter'
                    && document.querySelectorAll('[data-canvas-text-rect][data-block-id="canvas-e10-autocorrect-target"]').length >= 1
                    && document.querySelectorAll('[data-canvas-text-rect][data-block-id="canvas-e10-painter-target"]').length >= 1;
            }
            """,
            new PageWaitForFunctionOptions { Timeout = 30_000 });

    private static async Task ClickTextEndAsync(IPage page, string blockId)
    {
        var point = await page.EvaluateAsync<CanvasPoint>(
            """
            blockId => {
                const rects = Array.from(document.querySelectorAll(`[data-canvas-text-rect][data-block-id="${blockId}"]`));
                const node = rects[rects.length - 1];
                if (!node) {
                    throw new Error(`No canvas text rect found for ${blockId}.`);
                }
                const rect = node.getBoundingClientRect();
                return { x: rect.right - 1, y: rect.top + rect.height / 2 };
            }
            """,
            blockId);
        await page.Mouse.ClickAsync((float)point.X, (float)point.Y);
        await DocumentEditorCanvasVisualAssert.AssertCaretVisibleAsync(page.Locator("[data-testid='document-canvas-caret']").First);
    }

    private static async Task InsertSymbolFromPaletteAsync(IPage page, string testId, string? screenshotPath = null)
    {
        await Assertions.Expect(page.GetByTestId("document-toolbar-symbol")).ToBeEnabledAsync(new LocatorAssertionsToBeEnabledOptions { Timeout = 5_000 });
        await page.GetByTestId("document-toolbar-symbol").ClickAsync();
        await page.GetByTestId("document-symbol-menu").WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 10_000
        });

        if (!string.IsNullOrWhiteSpace(screenshotPath))
        {
            await page.GetByTestId("document-editor-demo").ScreenshotAsync(new LocatorScreenshotOptions
            {
                Path = screenshotPath,
                Type = ScreenshotType.Png
            });
        }

        await page.GetByTestId(testId).ClickAsync();
        await Assertions.Expect(page.GetByTestId("document-symbol-menu")).ToBeHiddenAsync(new LocatorAssertionsToBeHiddenOptions { Timeout = 5_000 });
    }

    private static Task WaitForMirrorBlockTextAsync(IPage page, string blockId, string expected)
        => page.WaitForFunctionAsync(
            """
            ([blockId, expected]) => {
                const block = document.querySelector(`[data-testid="document-canvas-a11y-mirror"] [data-block-id="${blockId}"]`);
                return block && block.textContent === expected;
            }
            """,
            new object[] { blockId, expected },
            new PageWaitForFunctionOptions { Timeout = 15_000 });

    private static async Task<CanvasTextRange> SelectCanvasTextRangeAsync(IPage page, string blockId, int startOffset, int endOffset)
    {
        await WaitForCanvasCommandBridgeAsync(page);
        var target = await page.EvaluateAsync<CanvasTextRange>(
            """
            ([blockId, startOffset, endOffset]) => {
                const rects = Array.from(document.querySelectorAll(`[data-canvas-text-rect][data-block-id="${blockId}"]`))
                    .map(node => {
                        const rect = node.getBoundingClientRect();
                        const start = Number(node.getAttribute('data-canvas-start-offset') || '0');
                        const end = Number(node.getAttribute('data-canvas-end-offset') || '0');
                        return { rect, start, end };
                    })
                    .filter(item => item.end > startOffset && item.start < endOffset);
                if (!rects.length) {
                    throw new Error(`No canvas text rects found for ${blockId} ${startOffset}-${endOffset}.`);
                }
                const first = rects[0];
                const last = rects[rects.length - 1];
                const startRatio = Math.max(0, Math.min(1, (startOffset - first.start) / Math.max(1, first.end - first.start)));
                const endRatio = Math.max(0, Math.min(1, (endOffset - last.start) / Math.max(1, last.end - last.start)));
                return {
                    blockId,
                    startOffset,
                    endOffset,
                    startX: first.rect.left + first.rect.width * startRatio,
                    startY: first.rect.top + first.rect.height / 2,
                    endX: last.rect.left + last.rect.width * endRatio,
                    endY: last.rect.top + last.rect.height / 2
                };
            }
            """,
            new object[] { blockId, startOffset, endOffset });
        var resultJson = await CanvasInteropBridge.EvaluateAsync<string>(page,
            """
            ([blockId, startOffset, endOffset]) => {
                const host = document.querySelector('[data-testid="document-canvas-engine-host"]');
                const handle = host?.getAttribute('data-canvas-engine-handle') || '';
                return (module => module.selectTextRange(handle, blockId, startOffset, endOffset) || '')(window.__tmCanvasInterop);
            }
            """,
            new object[] { blockId, startOffset, endOffset });
        using var result = JsonDocument.Parse(resultJson);
        Assert.IsTrue(result.RootElement.GetProperty("selected").GetBoolean(), $"Expected canvas interop selection for {blockId}[{startOffset}..{endOffset}].");
        await page.WaitForFunctionAsync(
            """
            blockId => document.querySelector('[data-testid="document-canvas-engine-root"]')
                ?.getAttribute('data-canvas-selection-anchor-block-id') === blockId
                && document.querySelectorAll('[data-testid="document-canvas-selection-rect"]').length >= 1
            """,
            blockId,
            new PageWaitForFunctionOptions { Timeout = 10_000 });
        return target;
    }

    private static Task WaitForCanvasCommandBridgeAsync(IPage page)
        => page.WaitForFunctionAsync(
            """
            () => {
                const host = document.querySelector('[data-testid="document-canvas-engine-host"]');
                return host?.getAttribute('data-canvas-engine-ready') === 'true'
                    && !!host?.getAttribute('data-canvas-engine-handle');
            }
            """,
            new PageWaitForFunctionOptions { Timeout = 15_000 });

    private static async Task<PhaseE10CommandProbe> ExecuteCanvasCommandAsync(IPage page, string commandId, object payload)
    {
        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        return await CanvasInteropBridge.EvaluateAsync<PhaseE10CommandProbe>(page,
            """
            ({ commandId, json }) => {
                const host = document.querySelector('[data-testid="document-canvas-engine-host"]');
                const handle = host?.getAttribute('data-canvas-engine-handle') || '';
                const module = window.__tmCanvasInterop;
                const raw = module.execCommand(handle, commandId, json);
                const parsed = JSON.parse(raw || '{}');
                return {
                    changed: parsed?.result?.changed === true,
                    handled: parsed?.handled === true,
                    insertedText: parsed?.result?.insertedText || '',
                    formatPainterActive: parsed?.formattingState?.formatPainter?.active === true,
                    debug: JSON.stringify(parsed)
                };
            }
            """,
            new { commandId, json });
    }

    private static async Task WaitForProbeAsync(IPage page, Func<PhaseE10Probe, bool> predicate)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(15);
        PhaseE10Probe lastProbe;
        do
        {
            lastProbe = await ReadProbeAsync(page);
            if (predicate(lastProbe))
            {
                return;
            }

            await CanvasInteropBridge.WaitForFramesAsync(page, 3);
        }
        while (DateTimeOffset.UtcNow < deadline);

        Assert.Fail($"Timed out waiting for E10 probe. Last probe: {JsonSerializer.Serialize(lastProbe, new JsonSerializerOptions(JsonSerializerDefaults.Web))}");
    }

    private static Task<PhaseE10Probe> ReadProbeAsync(IPage page)
        => CanvasInteropBridge.EvaluateAsync<PhaseE10Probe>(page,
            """
            () => {
                const host = document.querySelector('[data-testid="document-canvas-engine-host"]');
                const handle = host?.getAttribute('data-canvas-engine-handle') || '';
                const module = window.__tmCanvasInterop;
                const model = JSON.parse(module.getModelJson(handle));
                const blocks = model?.body?.blocks || [];
                const block = id => blocks.find(item => item.id === id) || {};
                const text = id => ((block(id).content?.runs || []).map(run => run.text || '').join(''));
                const hasMark = (id, type) => (block(id).content?.runs || [])
                    .some(run => (run.marks || []).some(mark => String(mark.type || '').toLowerCase() === type.toLowerCase()));
                const linkMarkCount = (block('canvas-e10-link-target').content?.runs || [])
                    .flatMap(run => run.marks || [])
                    .filter(mark => String(mark.type || '').toLowerCase() === 'link').length;
                return {
                    modelDocumentId: model.documentId || '',
                    autocorrectText: text('canvas-e10-autocorrect-target'),
                    listBlockType: block('canvas-e10-list-target').type || block('canvas-e10-list-target').content?.type || '',
                    listText: text('canvas-e10-list-target'),
                    linkMarkCount,
                    painterTargetBold: hasMark('canvas-e10-painter-target', 'bold'),
                    painterTargetAlignment: JSON.stringify(block('canvas-e10-painter-target').paragraphProperties || {}),
                    symbolText: text('canvas-e10-symbol-target')
                };
            }
            """);

    private static Task NavigateWithinBlazorAsync(IPage page, string url)
        => page.EvaluateAsync(
            """
            url => window.Blazor?.navigateTo
                ? window.Blazor.navigateTo(url)
                : window.history.pushState({}, '', url)
            """,
            url);

    private static async Task WaitForSaveBoundaryAsync(IPage page)
    {
        await page.WaitForFunctionAsync(
            """
            () => {
                const saveMessage = document.querySelector('[data-testid="document-save-message"]')?.textContent || '';
                const lastSaved = document.querySelector('[data-testid="document-last-saved"]')?.textContent || '';
                const dirty = document.querySelector('[data-testid="document-editor-demo"]')?.getAttribute('data-document-dirty') === 'true';
                const saveButtonDisabled = document.querySelector('[data-testid="document-save"]')?.hasAttribute('disabled') === true;
                return !dirty
                    && !saveButtonDisabled
                    && (/Saved|Autosaved/i.test(saveMessage) || /saved/i.test(lastSaved));
            }
            """,
            new PageWaitForFunctionOptions { Timeout = 20_000 });
    }

    private static string CreateOutputDirectory(string testName)
    {
        var path = Path.Combine(
            FindRepositoryRoot().FullName,
            "tests",
            "Tempo.Blazor.E2E",
            "TestResults",
            "document-editor-canvas",
            testName,
            "2026-06-04",
            "desktop-1440x1000");
        Directory.CreateDirectory(path);
        return path;
    }

    private static DirectoryInfo FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "TempoBlazor.slnx")))
        {
            current = current.Parent;
        }

        return current ?? new DirectoryInfo(AppContext.BaseDirectory);
    }

    private sealed class PhaseE10CommandProbe
    {
        public bool Handled { get; set; }
        public bool Changed { get; set; }
        public string InsertedText { get; set; } = string.Empty;
        public bool FormatPainterActive { get; set; }
        public string Debug { get; set; } = string.Empty;
    }

    private sealed class PhaseE10Probe
    {
        public string ModelDocumentId { get; set; } = string.Empty;
        public string AutocorrectText { get; set; } = string.Empty;
        public string ListBlockType { get; set; } = string.Empty;
        public string ListText { get; set; } = string.Empty;
        public int LinkMarkCount { get; set; }
        public bool PainterTargetBold { get; set; }
        public string PainterTargetAlignment { get; set; } = string.Empty;
        public string SymbolText { get; set; } = string.Empty;
    }

    private sealed class CanvasTextRange
    {
        public string BlockId { get; set; } = string.Empty;
        public int StartOffset { get; set; }
        public int EndOffset { get; set; }
        public double StartX { get; set; }
        public double StartY { get; set; }
        public double EndX { get; set; }
        public double EndY { get; set; }
    }

    private sealed class CanvasPoint
    {
        public double X { get; set; }
        public double Y { get; set; }
    }
}
