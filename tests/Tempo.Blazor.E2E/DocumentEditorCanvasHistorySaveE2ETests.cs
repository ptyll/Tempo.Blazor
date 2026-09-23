using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Tempo.Blazor.E2E.CanvasEngine;

namespace Tempo.Blazor.E2E;

/// <summary>Phase 12 E2E coverage for canvas history, dirty state, save, autosave, retry, and reload.</summary>
[TestClass]
[TestCategory("DocumentEditor")]
[TestCategory("DocumentEditor:CanvasEngine")]
[TestCategory("Smoke")]
[DoNotParallelize]
public sealed class DocumentEditorCanvasHistorySaveE2ETests : WasmTestBase
{
    private const string Phase12DocumentId = "phase-12-canvas-history-save";

    // State isolation: every test starts from the server-side seeded demo state so
    // markers typed (and saved) by earlier tests/runs cannot accumulate in the
    // persisted phase-12 document and drift its layout over time.
    [TestInitialize]
    public Task ResetDocumentEditorDemoAsync()
        => DocumentEditorE2EReset.ResetAsync();

    [TestMethod]
    public async Task Phase12_HistoryManualSaveReloadAndCategorySmoke_PersistsCanvasModel()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(1440, 1000);
        await OpenPhase12DocumentAsync(page);

        var output = CreateOutputDirectory(nameof(Phase12_HistoryManualSaveReloadAndCategorySmoke_PersistsCanvasModel));
        var beforePath = Path.Combine(output, "00-phase12-before.png");
        var afterReloadPath = Path.Combine(output, "01-phase12-after-reload.png");
        var marker = $"phase12word{DateTimeOffset.UtcNow:HHmmssfff}";

        var beforeProbe = await ReadPhase12ProbeAsync(page);
        AssertPhase12Categories(beforeProbe);
        await page.GetByTestId("document-editor-demo").ScreenshotAsync(new LocatorScreenshotOptions { Path = beforePath, Type = ScreenshotType.Png });

        await ClickCanvasBlockAsync(page, "canvas-history-text", await ReadBlockEndOffsetAsync(page, "canvas-history-text"));
        await FocusHiddenCanvasInputAsync(page);
        await page.Keyboard.TypeAsync($"{marker} ");
        await WaitForA11yTextAsync(page, marker);
        await WaitForDirtyStateAsync(page, expectedDirty: true);
        Assert.IsTrue(await ReadBeforeUnloadGuardActiveAsync(page), "Dirty canvas edits must enable the before-unload guard.");

        await page.GetByTestId("document-undo").ClickAsync();
        await WaitForA11yTextMissingAsync(page, marker);
        await page.GetByTestId("document-redo").ClickAsync();
        await WaitForA11yTextAsync(page, marker);

        var selected = await SelectCanvasTextRangeAsync(page, "canvas-history-format", 0, 10);
        Assert.AreEqual("Formatting", selected.ExpectedText);
        await page.GetByTestId("document-bold").ClickAsync();
        await WaitForCanvasCommandStateAsync(page, "bold", "active");

        await page.GetByTestId("document-save").ClickAsync();
        await Assertions.Expect(page.GetByTestId("document-save-message"))
            .ToContainTextAsync("Saved", new() { Timeout = 10_000 });
        await WaitForDirtyStateAsync(page, expectedDirty: false);
        await page.WaitForFunctionAsync(
            "() => window.tmDocumentEditor?.getBeforeUnloadGuardState?.().active === false",
            options: new PageWaitForFunctionOptions { Timeout = 10_000 });

        await NavigateWithinBlazorAsync(page, "/canvas-engine-host?documentId=phase-5-canvas-render");
        await page.WaitForFunctionAsync(
            "() => document.querySelector('[data-testid=\"document-canvas-page\"]')?.getAttribute('data-canvas-model-document-id') === 'phase-5-canvas-render'",
            options: new PageWaitForFunctionOptions { Timeout = 20_000 });
        await NavigateWithinBlazorAsync(page, $"/canvas-engine-host?documentId={Phase12DocumentId}&showToolbar=true");
        await WaitForPhase12ReadyAsync(page);
        await WaitForA11yTextAsync(page, marker);
        var afterReloadProbe = await ReadPhase12ProbeAsync(page);
        AssertPhase12Categories(afterReloadProbe);
        Assert.AreEqual(beforeProbe.TableBlockCount, afterReloadProbe.TableBlockCount);
        Assert.AreEqual(beforeProbe.ImageBlockCount, afterReloadProbe.ImageBlockCount);
        Assert.AreEqual(beforeProbe.CommentAnchorCount, afterReloadProbe.CommentAnchorCount);
        Assert.IsTrue(
            afterReloadProbe.RevisionAnchorCount >= beforeProbe.RevisionAnchorCount,
            $"Revision anchors must survive save/reload without being dropped. Before: {beforeProbe.RevisionAnchorCount}, after: {afterReloadProbe.RevisionAnchorCount}.");
        await page.GetByTestId("document-editor-demo").ScreenshotAsync(new LocatorScreenshotOptions { Path = afterReloadPath, Type = ScreenshotType.Png });

        await DocumentEditorCanvasVisualAssert.AssertNoTextOverlapAsync(page);
        await DocumentEditorCanvasVisualAssert.AssertNoUiOverlapAsync(page);
        var contentMetrics = await DocumentEditorCanvasVisualAssert.AssertCanvasNonBlankAsync(page.Locator("[data-canvas-layer='content']").First);
        var manifestPath = Path.Combine(output, "manifest.json");
        await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(new
        {
            testName = nameof(Phase12_HistoryManualSaveReloadAndCategorySmoke_PersistsCanvasModel),
            seedDocumentId = Phase12DocumentId,
            userActions = new[]
            {
                "Open the phase 12 canvas document through the production TmDocumentEditor route.",
                "Type one word through the hidden input bridge, undo it as one coalesced transaction, and redo it.",
                "Apply Bold to a canvas selection through the production toolbar.",
                "Save through the production Save command, navigate away, navigate back, and verify the saved canvas model renders again."
            },
            expectedModelChanges = "Text, formatting, table, image, comment anchor, and revision anchor survive the save/reload boundary.",
            screenshotPaths = new[] { beforePath, afterReloadPath },
            beforeProbe = beforeProbe,
            afterReloadProbe = afterReloadProbe,
            contentMetrics = contentMetrics
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));

        TestContext.AddResultFile(beforePath);
        TestContext.AddResultFile(afterReloadPath);
        TestContext.AddResultFile(manifestPath);
    }

    [TestMethod]
    public async Task Phase12_AutosaveDebounce_PersistsCurrentCanvasModelWithoutManualSave()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(1440, 1000);
        await page.GotoAsync($"{BaseUrl}/canvas-engine-host?documentId={Phase12DocumentId}&showToolbar=true&autosaveMs=500", new PageGotoOptions
        {
            WaitUntil = WaitUntilState.DOMContentLoaded,
            Timeout = 60_000
        });
        await WaitForPhase12ReadyAsync(page);

        var marker = $"autosave{DateTimeOffset.UtcNow:HHmmssfff}";
        await ClickCanvasBlockAsync(page, "canvas-history-text", await ReadBlockEndOffsetAsync(page, "canvas-history-text"));
        await FocusHiddenCanvasInputAsync(page);
        await page.Keyboard.TypeAsync($"{marker} ");

        // Assert the transient autosave-pending state BEFORE the a11y poll — the poll can outlast
        // the debounce window and flip the status to "Saving..." before the expect observes it.
        await Assertions.Expect(page.GetByTestId("document-pending-status"))
            .ToContainTextAsync("Autosave pending", new() { Timeout = 5_000 });
        await WaitForA11yTextAsync(page, marker);
        await Assertions.Expect(page.GetByTestId("document-save-message"))
            .ToContainTextAsync("Autosaved", new() { Timeout = 12_000 });
        await WaitForDirtyStateAsync(page, expectedDirty: false);

        await NavigateWithinBlazorAsync(page, "/canvas-engine-host?documentId=phase-5-canvas-render");
        await page.WaitForFunctionAsync(
            "() => document.querySelector('[data-testid=\"document-canvas-page\"]')?.getAttribute('data-canvas-model-document-id') === 'phase-5-canvas-render'",
            options: new PageWaitForFunctionOptions { Timeout = 20_000 });
        await NavigateWithinBlazorAsync(page, $"/canvas-engine-host?documentId={Phase12DocumentId}&showToolbar=true&autosaveMs=500");
        await WaitForPhase12ReadyAsync(page);
        await WaitForA11yTextAsync(page, marker);
    }

    [TestMethod]
    public async Task Phase12_SaveFailure_CreatesOfflineDraftRetryAndBeforeUnloadState()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(1440, 1000);
        await page.GotoAsync($"{BaseUrl}/canvas-engine-host?documentId={Phase12DocumentId}&showToolbar=true&autosaveMs=30000&failSaves=true", new PageGotoOptions
        {
            WaitUntil = WaitUntilState.DOMContentLoaded,
            Timeout = 60_000
        });
        await WaitForPhase12ReadyAsync(page);

        var marker = $"retry{DateTimeOffset.UtcNow:HHmmssfff}";
        Assert.IsFalse(await ReadBeforeUnloadGuardActiveAsync(page));
        await ClickCanvasBlockAsync(page, "canvas-history-text", await ReadBlockEndOffsetAsync(page, "canvas-history-text"));
        await FocusHiddenCanvasInputAsync(page);
        await page.Keyboard.TypeAsync($"{marker} ");
        await WaitForA11yTextAsync(page, marker);
        await page.WaitForFunctionAsync(
            "() => window.tmDocumentEditor?.getBeforeUnloadGuardState?.().active === true",
            options: new PageWaitForFunctionOptions { Timeout = 10_000 });

        await page.GetByTestId("document-save").ClickAsync();
        await Assertions.Expect(page.GetByTestId("document-save-message"))
            .ToContainTextAsync("Demo autosave provider failed", new() { Timeout = 10_000 });
        await Assertions.Expect(page.GetByTestId("document-save-retry"))
            .ToBeVisibleAsync(new() { Timeout = 5_000 });
        await Assertions.Expect(page.GetByTestId("document-offline-banner"))
            .ToBeVisibleAsync(new() { Timeout = 5_000 });

        await NavigateWithinBlazorAsync(page, $"/canvas-engine-host?documentId={Phase12DocumentId}&showToolbar=true&autosaveMs=30000&failSaves=false");
        await page.GetByTestId("document-save-retry").ClickAsync();
        await Assertions.Expect(page.GetByTestId("document-save-message"))
            .ToContainTextAsync("Saved", new() { Timeout = 10_000 });
        await WaitForDirtyStateAsync(page, expectedDirty: false);
        await page.WaitForFunctionAsync(
            "() => window.tmDocumentEditor?.getBeforeUnloadGuardState?.().active === false",
            options: new PageWaitForFunctionOptions { Timeout = 10_000 });
    }

    private async Task OpenPhase12DocumentAsync(IPage page)
    {
        await page.GotoAsync($"{BaseUrl}/canvas-engine-host?documentId={Phase12DocumentId}&showToolbar=true", new PageGotoOptions
        {
            WaitUntil = WaitUntilState.DOMContentLoaded,
            Timeout = 60_000
        });
        await WaitForPhase12ReadyAsync(page);
    }

    private static Task WaitForPhase12ReadyAsync(IPage page)
        => page.WaitForFunctionAsync(
            """
            () => document.querySelector('[data-testid="document-canvas-engine-host"][data-canvas-engine-ready="true"]')
                && document.querySelector('[data-testid="document-save"]')
                && document.querySelectorAll('[data-canvas-text-rect][data-block-id="canvas-history-text"]').length >= 1
            """,
            options: new PageWaitForFunctionOptions { Timeout = 30_000 });

    private static async Task ClickCanvasBlockAsync(IPage page, string blockId, int offset)
    {
        // A click landing inside the short window after a post-mount replaceModel push resolves
        // against a freshly reset (progressive) selection layout and maps to offset 0 instead of
        // the requested offset. Deterministic hardening: verify the caret offset actually taken
        // and re-click until the engine resolves the intended position.
        for (var attempt = 0; ; attempt++)
        {
            var point = await ReadCanvasPointAsync(page, blockId, offset);
            await page.Mouse.ClickAsync((float)point.X, (float)point.Y);
            await page.WaitForFunctionAsync(
                """
                blockId => document.querySelector('[data-testid="document-canvas-engine-root"]')
                    ?.getAttribute('data-canvas-selection-focus-block-id') === blockId
                """,
                blockId,
                new PageWaitForFunctionOptions { Timeout = 10_000 });

            var focusOffset = await page.EvaluateAsync<int>(
                """
                () => Number(document.querySelector('[data-testid="document-canvas-engine-root"]')
                    ?.getAttribute('data-canvas-selection-focus-offset') || '-1')
                """);
            if (focusOffset == offset)
            {
                return;
            }

            if (attempt >= 9)
            {
                Assert.Fail($"Canvas click on block '{blockId}' kept resolving to offset {focusOffset} instead of {offset}.");
            }

            // N194: the settle between attempts is state-based, not a fixed sleep. A wrong-offset
            // click inside the post-mount replaceModel window races a partial progressive layout
            // (data-canvas-layout-complete="false"); wait until the layout reports complete again
            // AND the render counter has advanced past the render that resolved wrong. If the
            // signal never fires the attempt bound above still owns the verdict.
            var renderCountBefore = await ReadCanvasRenderCountAsync(page);
            try
            {
                await page.WaitForFunctionAsync(
                    """
                    prev => {
                        const root = document.querySelector('[data-testid="document-canvas-engine-root"]');
                        if (!root) { return false; }
                        if (root.getAttribute('data-canvas-layout-complete') !== 'true') { return false; }
                        return Number(root.getAttribute('data-canvas-render-count') || '0') !== prev;
                    }
                    """,
                    renderCountBefore,
                    new PageWaitForFunctionOptions { Timeout = 2_500 });
            }
            catch (TimeoutException)
            {
                // Settle signal never fired inside the window — re-click anyway; the bounded
                // retry owns the outcome, not the delay.
            }
        }
    }

    private static Task<int> ReadCanvasRenderCountAsync(IPage page)
        => page.EvaluateAsync<int>(
            """
            () => Number(document.querySelector('[data-testid="document-canvas-engine-root"]')
                ?.getAttribute('data-canvas-render-count') || '0')
            """);

    private static async Task<CanvasTextRange> SelectCanvasTextRangeAsync(IPage page, string blockId, int startOffset, int endOffset)
    {
        var target = await ReadCanvasTextRangeAsync(page, blockId, startOffset, endOffset);
        await page.Mouse.MoveAsync((float)target.StartX, (float)target.StartY);
        await page.Mouse.DownAsync();
        await page.Mouse.MoveAsync((float)target.EndX, (float)target.EndY, new MouseMoveOptions { Steps = 10 });
        await page.Mouse.UpAsync();
        await page.WaitForFunctionAsync(
            """
            blockId => document.querySelector('[data-testid="document-canvas-engine-root"]')
                ?.getAttribute('data-canvas-selection-focus-block-id') === blockId
                && document.querySelector('[data-testid="document-canvas-engine-root"]')
                    ?.getAttribute('data-canvas-selection-collapsed') === 'false'
            """,
            blockId,
            new PageWaitForFunctionOptions { Timeout = 10_000 });
        return target;
    }

    private static Task<int> ReadBlockEndOffsetAsync(IPage page, string blockId)
        => page.EvaluateAsync<int>(
            """
            blockId => Math.max(...Array.from(document.querySelectorAll(`[data-canvas-text-rect][data-block-id="${blockId}"]`))
                .map(node => Number(node.getAttribute('data-canvas-end-offset') || '0')))
            """,
            blockId);

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
                const target = rects.find(item => offset >= item.start && offset <= item.end) || rects.at(-1);
                const ratio = Math.max(0, Math.min(1, (offset - target.start) / Math.max(1, target.end - target.start)));
                return {
                    x: target.rect.left + Math.max(2, target.rect.width * ratio),
                    y: target.rect.top + Math.max(2, target.rect.height / 2)
                };
            }
            """,
            new object[] { blockId, offset });

    private static Task<CanvasTextRange> ReadCanvasTextRangeAsync(IPage page, string blockId, int startOffset, int endOffset)
        => page.EvaluateAsync<CanvasTextRange>(
            """
            ([blockId, startOffset, endOffset]) => {
                const readPoint = offset => {
                    const rects = Array.from(document.querySelectorAll(`[data-canvas-text-rect][data-block-id="${blockId}"]`))
                        .map(node => {
                            const rect = node.getBoundingClientRect();
                            const start = Number(node.getAttribute('data-canvas-start-offset') || '0');
                            const end = Number(node.getAttribute('data-canvas-end-offset') || '0');
                            return { rect, start, end };
                        })
                        .filter(item => item.end > item.start);
                    const target = rects.find(item => offset >= item.start && offset <= item.end) || rects[0];
                    const ratio = Math.max(0, Math.min(1, (offset - target.start) / Math.max(1, target.end - target.start)));
                    return {
                        x: target.rect.left + Math.max(2, target.rect.width * ratio),
                        y: target.rect.top + Math.max(2, target.rect.height / 2)
                    };
                };
                const start = readPoint(startOffset);
                const end = readPoint(endOffset);
                const text = document.querySelector(`[data-testid="document-canvas-a11y-mirror"] [data-block-id="${blockId}"]`)?.textContent || '';
                return {
                    startX: start.x,
                    startY: start.y,
                    endX: end.x,
                    endY: end.y,
                    expectedText: text.slice(startOffset, endOffset)
                };
            }
            """,
            new object[] { blockId, startOffset, endOffset });

    private static Task FocusHiddenCanvasInputAsync(IPage page)
        => page.EvaluateAsync(
            """
            () => {
                const input = document.querySelector('[data-testid="document-canvas-hidden-input"]');
                input?.focus();
            }
            """);

    private static Task WaitForA11yTextAsync(IPage page, string text)
        => Assertions.Expect(page.GetByTestId("document-canvas-a11y-mirror"))
            .ToContainTextAsync(text, new() { Timeout = 10_000 });

    private static Task WaitForA11yTextMissingAsync(IPage page, string text)
        => Assertions.Expect(page.GetByTestId("document-canvas-a11y-mirror"))
            .Not.ToContainTextAsync(text, new() { Timeout = 10_000 });

    private static Task WaitForCanvasCommandStateAsync(IPage page, string commandId, string state)
        => page.WaitForFunctionAsync(
            """
            ([commandId, state]) => document.querySelector('[data-testid="document-canvas-engine-root"]')
                ?.getAttribute(`data-canvas-command-${commandId}-state`) === state
            """,
            new object[] { commandId, state },
            new PageWaitForFunctionOptions { Timeout = 10_000 });

    private static Task NavigateWithinBlazorAsync(IPage page, string url)
        => page.EvaluateAsync(
            """
            url => window.Blazor?.navigateTo
                ? window.Blazor.navigateTo(url)
                : window.history.pushState({}, '', url)
            """,
            url);

    private static Task WaitForDirtyStateAsync(IPage page, bool expectedDirty)
        => page.WaitForFunctionAsync(
            """
            expectedDirty => {
                const dirty = document.querySelector('[data-testid="document-dirty-status"]');
                return expectedDirty ? !!dirty : !dirty;
            }
            """,
            expectedDirty,
            new PageWaitForFunctionOptions { Timeout = 10_000 });

    private static Task<bool> ReadBeforeUnloadGuardActiveAsync(IPage page)
        => page.EvaluateAsync<bool>("() => window.tmDocumentEditor?.getBeforeUnloadGuardState?.().active === true");

    private static Task<Phase12Probe> ReadPhase12ProbeAsync(IPage page)
        => page.EvaluateAsync<Phase12Probe>(
            """
            () => {
                const pages = Array.from(document.querySelectorAll('[data-testid="document-canvas-page"]'));
                const first = pages[0] || null;
                const sum = name => pages.reduce((total, page) => total + Number(page.getAttribute(name) || '0'), 0);
                return {
                    modelDocumentId: first?.getAttribute('data-canvas-model-document-id') || '',
                    blockCount: Number(first?.getAttribute('data-canvas-model-block-count') || '0'),
                    tableBlockCount: Number(first?.getAttribute('data-canvas-model-table-block-count') || '0'),
                    imageBlockCount: Number(first?.getAttribute('data-canvas-model-image-block-count') || '0'),
                    tableCount: sum('data-canvas-table-count'),
                    imageCount: sum('data-canvas-image-count'),
                    commentAnchorCount: sum('data-canvas-comment-anchor-count'),
                    revisionAnchorCount: sum('data-canvas-revision-anchor-count'),
                    commandCount: sum('data-canvas-render-command-count')
                };
            }
            """);

    private static void AssertPhase12Categories(Phase12Probe probe)
    {
        Assert.AreEqual(Phase12DocumentId, probe.ModelDocumentId);
        Assert.IsTrue(probe.BlockCount >= 4, $"Expected text, formatting, table, and drawing-backed image content. Probe: {JsonSerializer.Serialize(probe)}");
        Assert.IsTrue(probe.TableBlockCount >= 1, $"Expected table model block. Probe: {JsonSerializer.Serialize(probe)}");
        Assert.IsTrue(probe.TableCount >= 1, $"Expected table render command. Probe: {JsonSerializer.Serialize(probe)}");
        Assert.IsTrue(probe.ImageCount >= 1, $"Expected drawing-backed image render command. Probe: {JsonSerializer.Serialize(probe)}");
        Assert.IsTrue(probe.CommentAnchorCount >= 1, $"Expected comment anchor render command. Probe: {JsonSerializer.Serialize(probe)}");
        Assert.IsTrue(probe.RevisionAnchorCount >= 1, $"Expected revision anchor render command. Probe: {JsonSerializer.Serialize(probe)}");
    }

    private string CreateOutputDirectory(string testName)
    {
        var output = Path.Combine(
            FindRepositoryRoot().FullName,
            "tests",
            "Tempo.Blazor.E2E",
            "TestResults",
            "document-editor-canvas",
            nameof(DocumentEditorCanvasHistorySaveE2ETests),
            SanitizePathSegment(testName));
        Directory.CreateDirectory(output);
        return output;
    }

    private static string SanitizePathSegment(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string(value.Select(ch => invalid.Contains(ch) ? '-' : ch).ToArray());
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

    private sealed class CanvasPoint
    {
        [JsonPropertyName("x")] public double X { get; set; }
        [JsonPropertyName("y")] public double Y { get; set; }
    }

    private sealed class CanvasTextRange
    {
        [JsonPropertyName("startX")] public double StartX { get; set; }
        [JsonPropertyName("startY")] public double StartY { get; set; }
        [JsonPropertyName("endX")] public double EndX { get; set; }
        [JsonPropertyName("endY")] public double EndY { get; set; }
        [JsonPropertyName("expectedText")] public string ExpectedText { get; set; } = string.Empty;
    }

    private sealed class Phase12Probe
    {
        [JsonPropertyName("modelDocumentId")] public string ModelDocumentId { get; set; } = string.Empty;
        [JsonPropertyName("blockCount")] public int BlockCount { get; set; }
        [JsonPropertyName("tableBlockCount")] public int TableBlockCount { get; set; }
        [JsonPropertyName("imageBlockCount")] public int ImageBlockCount { get; set; }
        [JsonPropertyName("tableCount")] public int TableCount { get; set; }
        [JsonPropertyName("imageCount")] public int ImageCount { get; set; }
        [JsonPropertyName("commentAnchorCount")] public int CommentAnchorCount { get; set; }
        [JsonPropertyName("revisionAnchorCount")] public int RevisionAnchorCount { get; set; }
        [JsonPropertyName("commandCount")] public int CommandCount { get; set; }
    }
}
