using System.Text.Json;
using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Tempo.Blazor.E2E.CanvasEngine;

namespace Tempo.Blazor.E2E;

/// <summary>E6 E2E coverage for canvas advanced character formatting, toolbar commands, undo, save, and reload.</summary>
[TestClass]
[TestCategory("DocumentEditor")]
[TestCategory("DocumentEditor:CanvasEngine")]
[DoNotParallelize]
public sealed class DocumentEditorCanvasAdvancedCharE2ETests : WasmTestBase
{
    private const string PhaseE6DocumentId = "phase-e6-canvas-advanced-char";

    [TestMethod]
    public async Task PhaseE6_AdvancedCharacterFormattingPersistsThroughToolbarUndoSaveAndReload()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        page.Console += (_, message) => TestContext.WriteLine($"[browser:{message.Type}] {message.Text}");
        page.PageError += (_, error) => TestContext.WriteLine($"[page-error] {error}");
        await page.SetViewportSizeAsync(1440, 1000);
        await OpenPhaseE6DocumentAsync(page);

        var output = CreateOutputDirectory("desktop-1440x1000");
        var beforePath = Path.Combine(output, "00-phasee6-advanced-char-before.png");
        var afterPath = Path.Combine(output, "01-phasee6-advanced-char-after-reload.png");

        var initialProbe = await ReadProbeAsync(page);
        Assert.AreEqual(PhaseE6DocumentId, initialProbe.ModelDocumentId);
        Assert.IsTrue(initialProbe.AdvancedMarkCount >= 6);
        Assert.IsTrue(initialProbe.MirrorText.Contains("H2O", StringComparison.Ordinal));

        await page.GetByTestId("document-editor-demo").ScreenshotAsync(new LocatorScreenshotOptions
        {
            Path = beforePath,
            Type = ScreenshotType.Png
        });

        await SelectCanvasTextRangeAsync(page, "canvas-e6-command-target", 0, 23);
        await page.GetByTestId("document-change-case").SelectOptionAsync("titleCase");
        await WaitForMirrorTextAsync(page, "Phase E6 Command Target");
        await page.GetByTestId("document-undo").ClickAsync();
        await WaitForMirrorTextAsync(page, "phase e6 command target");
        await page.GetByTestId("document-redo").ClickAsync();
        await WaitForMirrorTextAsync(page, "Phase E6 Command Target");

        await SelectCanvasTextRangeAsync(page, "canvas-e6-command-target", 0, 5);
        await page.GetByTestId("document-superscript").ClickAsync();
        await WaitForCommandStateAsync(page, "superscript", "active");
        await page.GetByTestId("document-increase-font-size").ClickAsync();
        await WaitForCommandStateAsync(page, "fontsize", "active");

        await DocumentEditorCanvasVisualAssert.AssertNoUiOverlapAsync(page);
        var contentMetrics = await DocumentEditorCanvasVisualAssert.AssertCanvasNonBlankAsync(page.Locator("[data-canvas-layer='content']").First);

        await page.GetByTestId("document-save").ClickAsync();
        await WaitForSaveBoundaryAsync(page);
        await NavigateWithinBlazorAsync(page, "/");
        await WaitForPredicateAsync(page, "() => window.location.pathname === '/'", 20_000);
        await NavigateWithinBlazorAsync(page, $"/canvas-engine-host?documentId={PhaseE6DocumentId}&showToolbar=true");
        await WaitForPhaseE6ReadyAsync(page);
        await WaitForMirrorTextAsync(page, "Phase E6 Command Target");

        var reloadedProbe = await ReadProbeAsync(page);
        Assert.AreEqual(PhaseE6DocumentId, reloadedProbe.ModelDocumentId);
        Assert.IsTrue(reloadedProbe.AdvancedMarkCount > initialProbe.AdvancedMarkCount);
        Assert.IsTrue(reloadedProbe.MirrorText.Contains("small caps sample", StringComparison.Ordinal));
        Assert.IsTrue(reloadedProbe.MirrorText.Contains("Phase E6 Command Target", StringComparison.Ordinal));

        await page.GetByTestId("document-editor-demo").ScreenshotAsync(new LocatorScreenshotOptions
        {
            Path = afterPath,
            Type = ScreenshotType.Png
        });

        var manifestPath = Path.Combine(output, "manifest.json");
        await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(new
        {
            testName = nameof(PhaseE6_AdvancedCharacterFormattingPersistsThroughToolbarUndoSaveAndReload),
            seedDocumentId = PhaseE6DocumentId,
            userActions = new[]
            {
                "Open the phase E6 canvas advanced character seed document with the production toolbar.",
                "Select canvas text, apply title case, undo it, and redo it.",
                "Apply superscript and increase font size from the Home ribbon.",
                "Save, navigate away, navigate back, and verify advanced marks and transformed text survive reload."
            },
            expectedVisibleChanges = "The canvas keeps H2O/x2 baseline examples, paints small caps, expanded/scaled text, double strikethrough, and persists toolbar-applied advanced character formatting after save and reload.",
            screenshotPaths = new[] { beforePath, afterPath },
            initialProbe,
            reloadedProbe,
            contentMetrics
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));

        TestContext.AddResultFile(beforePath);
        TestContext.AddResultFile(afterPath);
        TestContext.AddResultFile(manifestPath);
    }

    private async Task OpenPhaseE6DocumentAsync(IPage page)
    {
        await GotoAppReadyAsync(page, $"{BaseUrl}/canvas-engine-host?documentId={PhaseE6DocumentId}&showToolbar=true");
        await WaitForPhaseE6ReadyAsync(page);
    }

    private static Task WaitForPhaseE6ReadyAsync(IPage page)
        => WaitForPredicateAsync(page, """
            () => {
                const hostReady = document.querySelector('[data-testid="document-canvas-engine-host"]')?.getAttribute('data-canvas-engine-ready') === 'true';
                const first = document.querySelector('[data-testid="document-canvas-page"]');
                return hostReady
                    && first?.getAttribute('data-canvas-model-document-id') === 'phase-e6-canvas-advanced-char'
                    && Number(first.getAttribute('data-canvas-model-advanced-char-mark-count') || '0') >= 6
                    && document.querySelectorAll('[data-canvas-text-rect][data-block-id="canvas-e6-command-target"]').length >= 1;
            }
            """, 30_000);

    private static Task WaitForCommandStateAsync(IPage page, string command, string state)
        => page.WaitForFunctionAsync(
            """
            ([command, state]) => document.querySelector('[data-testid="document-canvas-engine-root"]')
                ?.getAttribute(`data-canvas-command-${command}-state`) === state
            """,
            new[] { command, state },
            new PageWaitForFunctionOptions { Timeout = 10_000 });

    private static Task WaitForMirrorTextAsync(IPage page, string expected)
        => page.WaitForFunctionAsync(
            """
            expected => document.querySelector('[data-testid="document-canvas-a11y-mirror"]')
                ?.textContent?.includes(expected) === true
            """,
            expected,
            new PageWaitForFunctionOptions { Timeout = 10_000 });

    private static async Task<CanvasTextRange> SelectCanvasTextRangeAsync(IPage page, string blockId, int startOffset, int endOffset)
    {
        var target = await ReadCanvasTextRangeAsync(page, blockId, startOffset, endOffset);
        await page.Mouse.MoveAsync((float)target.StartX, (float)target.StartY);
        await page.Mouse.DownAsync();
        await page.Mouse.MoveAsync((float)target.EndX, (float)target.EndY, new MouseMoveOptions { Steps = 10 });
        await page.Mouse.UpAsync();
        await WaitForSelectionVisibleAsync(page, blockId);
        return target;
    }

    private static Task WaitForSelectionVisibleAsync(IPage page, string blockId)
        => page.WaitForFunctionAsync(
            """
            blockId => {
                const root = document.querySelector('[data-testid="document-canvas-engine-root"]');
                return root?.getAttribute('data-canvas-selection-collapsed') === 'false'
                    && root?.getAttribute('data-canvas-selection-anchor-block-id') === blockId
                    && document.querySelectorAll('[data-testid="document-canvas-selection-rect"]').length >= 1;
            }
            """,
            blockId,
            new PageWaitForFunctionOptions { Timeout = 10_000 });

    private static Task<CanvasTextRange> ReadCanvasTextRangeAsync(IPage page, string blockId, int startOffset, int endOffset)
        => page.EvaluateAsync<CanvasTextRange>(
            """
            ([blockId, startOffset, endOffset]) => {
                const rects = Array.from(document.querySelectorAll(`[data-canvas-text-rect][data-block-id="${blockId}"]`))
                    .map(node => {
                        const rect = node.getBoundingClientRect();
                        const start = Number(node.getAttribute('data-canvas-start-offset') || '0');
                        const end = Number(node.getAttribute('data-canvas-end-offset') || '0');
                        return { rect, start, end, text: node.getAttribute('data-canvas-text') || '' };
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
                    endY: last.rect.top + last.rect.height / 2,
                    expectedText: rects.map(item => item.text).join('')
                };
            }
            """,
            new object[] { blockId, startOffset, endOffset });

    private static Task<PhaseE6Probe> ReadProbeAsync(IPage page)
        => page.EvaluateAsync<PhaseE6Probe>(
            """
            () => {
                const first = document.querySelector('[data-testid="document-canvas-page"]');
                const root = document.querySelector('[data-testid="document-canvas-engine-root"]');
                return {
                    modelDocumentId: first?.getAttribute('data-canvas-model-document-id') || '',
                    advancedMarkCount: Number(first?.getAttribute('data-canvas-model-advanced-char-mark-count') || '0'),
                    mirrorText: document.querySelector('[data-testid="document-canvas-a11y-mirror"]')?.textContent || '',
                    lastCommand: root?.getAttribute('data-canvas-command-last') || '',
                    superscriptState: root?.getAttribute('data-canvas-command-superscript-state') || '',
                    fontSizeState: root?.getAttribute('data-canvas-command-fontsize-state') || ''
                };
            }
            """);

    private static async Task WaitForSaveBoundaryAsync(IPage page)
    {
        try
        {
            await WaitForPredicateAsync(page, """
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
                """, 10_000);
        }
        catch (TimeoutException ex)
        {
            var state = await ReadProbeAsync(page);
            Assert.Fail($"Timed out waiting for the phase E6 save boundary. Probe: {JsonSerializer.Serialize(state, new JsonSerializerOptions(JsonSerializerDefaults.Web))}. {ex.Message}");
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

    private static string CreateOutputDirectory(string viewport)
    {
        var output = Path.Combine(
            FindRepositoryRoot().FullName,
            "tests",
            "Tempo.Blazor.E2E",
            "TestResults",
            "document-editor-canvas",
            "phasee6-advanced-char",
            viewport);
        Directory.CreateDirectory(output);
        return output;
    }

    private static DirectoryInfo FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TempoBlazor.slnx")))
        {
            directory = directory.Parent;
        }

        return directory ?? new DirectoryInfo(AppContext.BaseDirectory);
    }

    public sealed class PhaseE6Probe
    {
        public string ModelDocumentId { get; set; } = string.Empty;

        public int AdvancedMarkCount { get; set; }

        public string MirrorText { get; set; } = string.Empty;

        public string LastCommand { get; set; } = string.Empty;

        public string SuperscriptState { get; set; } = string.Empty;

        public string FontSizeState { get; set; } = string.Empty;
    }

    public sealed class CanvasTextRange
    {
        public string BlockId { get; set; } = string.Empty;

        public int StartOffset { get; set; }

        public int EndOffset { get; set; }

        public double StartX { get; set; }

        public double StartY { get; set; }

        public double EndX { get; set; }

        public double EndY { get; set; }

        public string ExpectedText { get; set; } = string.Empty;
    }
}
