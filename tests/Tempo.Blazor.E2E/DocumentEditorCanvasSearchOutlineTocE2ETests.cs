using System.Text.Json;
using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Tempo.Blazor.E2E.CanvasEngine;

namespace Tempo.Blazor.E2E;

/// <summary>Phase 18 E2E coverage for canvas regex search, outline navigation, bookmarks, and table of contents persistence.</summary>
[TestClass]
[TestCategory("DocumentEditor")]
[TestCategory("DocumentEditor:CanvasEngine")]
[DoNotParallelize]
public sealed class DocumentEditorCanvasSearchOutlineTocE2ETests : WasmTestBase
{
    private const string Phase18DocumentId = "phase-18-canvas-search-outline-toc";

    [TestMethod]
    public async Task Phase18_CanvasSearchReplaceOutlineAndTableOfContentsPersist()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        page.Console += (_, message) => TestContext.WriteLine($"[browser:{message.Type}] {message.Text}");
        page.PageError += (_, error) => TestContext.WriteLine($"[page-error] {error}");
        await page.SetViewportSizeAsync(1440, 1000);
        var output = CreateOutputDirectory("phase18-search-outline-toc");
        await ResetPhase18ApiDocumentAsync();
        await OpenPhase18DocumentAsync(page);

        var initial = await ReadProbeAsync(page);
        Assert.AreEqual(Phase18DocumentId, initial.ModelDocumentId);
        Assert.IsTrue(initial.Text.Contains("Tempo-18", StringComparison.Ordinal));

        await page.GetByTestId("document-canvas-engine-host").ClickAsync();
        await page.Keyboard.PressAsync("Control+H");
        await Assertions.Expect(page.GetByTestId("document-find-panel")).ToBeVisibleAsync(new() { Timeout = 10_000 });
        await page.GetByTestId("document-find-regex").CheckAsync();
        await page.GetByTestId("document-find-input").FillAsync("Tempo-(\\d+)");
        await WaitForSearchMatchesAsync(page, 4);
        var findScreenshotPath = Path.Combine(output, "active-find-result.png");
        await page.ScreenshotAsync(new PageScreenshotOptions { Path = findScreenshotPath, FullPage = true });
        TestContext.AddResultFile(findScreenshotPath);
        await page.GetByTestId("document-replace-input").FillAsync("Milestone-$1");
        await page.GetByTestId("document-find-replace-all").ClickAsync();
        await WaitForTextAsync(page, "Milestone-18");

        await page.GetByTestId("document-ribbon-tab-references").ClickAsync();
        await page.GetByTestId("document-insert-toc").ClickAsync();
        await WaitForTocEntryCountAtLeastAsync(page, 4);
        await WaitForTocHitCountAtLeastAsync(page, 4);
        var tocScreenshotPath = Path.Combine(output, "table-of-contents.png");
        await page.ScreenshotAsync(new PageScreenshotOptions { Path = tocScreenshotPath, FullPage = true });
        TestContext.AddResultFile(tocScreenshotPath);
        await DocumentEditorCanvasVisualAssert.AssertNoUiOverlapAsync(page);
        var initialContentMetrics = await DocumentEditorCanvasVisualAssert.AssertCanvasNonBlankAsync(page.Locator("[data-canvas-layer='content']").First);

        var outline = await ReadOutlineAsync(page);
        var deliveryScope = outline.First(item => item.Level == 2 && item.Text == "Delivery Scope");
        await page.GetByTestId("document-side-panel-tab-outline").ClickAsync();
        await Assertions.Expect(page.GetByTestId("document-outline-panel")).ToBeVisibleAsync(new() { Timeout = 10_000 });
        await page.Locator($"[data-testid='document-outline-item'][data-block-id='{deliveryScope.BlockId}']").ClickAsync();
        await WaitForSelectionBlockAsync(page, deliveryScope.BlockId);
        var outlineScreenshotPath = Path.Combine(output, "outline-panel-navigation.png");
        await page.ScreenshotAsync(new PageScreenshotOptions { Path = outlineScreenshotPath, FullPage = true });
        TestContext.AddResultFile(outlineScreenshotPath);

        var firstTocTarget = await page.GetByTestId("document-canvas-toc-entry").First.GetAttributeAsync("data-canvas-toc-target-block-id");
        Assert.IsFalse(string.IsNullOrWhiteSpace(firstTocTarget));
        await page.GetByTestId("document-canvas-toc-entry").First.ClickAsync();
        await WaitForTocNavigationAsync(page, firstTocTarget!);
        await page.GetByTestId("document-undo").ClickAsync();
        await WaitForTocEntryCountAsync(page, 0);
        await page.GetByTestId("document-redo").ClickAsync();
        await WaitForTocEntryCountAtLeastAsync(page, 4);

        var introText = await ReadBlockTextAsync(page, "phase18-intro");
        var bookmarkBatch = await ExecuteCanvasCommandBatchAsync(page,
            ("addBookmark", new
            {
                name = "phase18-intro-bookmark",
                blockId = "phase18-intro",
                start = 0,
                end = "Milestone-18".Length
            }),
            ("replacerange", new
            {
                blockId = "phase18-intro",
                start = introText.Length,
                end = introText.Length,
                text = " Bookmark persistence check."
            }),
            ("gotoBookmark", new { name = "phase18-intro-bookmark" }));
        var bookmarkProbe = bookmarkBatch[0];
        Assert.IsTrue(bookmarkProbe.Handled, bookmarkProbe.Debug);
        Assert.IsTrue(bookmarkProbe.Changed, bookmarkProbe.Debug);
        var bookmarkEditProbe = bookmarkBatch[1];
        Assert.IsTrue(bookmarkEditProbe.Handled, bookmarkEditProbe.Debug);
        Assert.IsTrue(bookmarkEditProbe.Changed, bookmarkEditProbe.Debug);
        var gotoBookmarkProbe = bookmarkBatch[2];
        Assert.IsTrue(gotoBookmarkProbe.Handled, gotoBookmarkProbe.Debug);
        Assert.IsTrue(gotoBookmarkProbe.SelectionChanged, gotoBookmarkProbe.Debug);
        await WaitForBookmarkAsync(page, "phase18-intro-bookmark");
        await WaitForSelectionBlockAsync(page, "phase18-intro");

        var renameProbe = await ExecuteCanvasCommandAsync(page, "replacerange", new
        {
            blockId = deliveryScope.BlockId,
            start = 0,
            end = deliveryScope.Text.Length,
            text = "Delivery Roadmap"
        });
        Assert.IsTrue(renameProbe.Handled, renameProbe.Debug);
        Assert.IsTrue(renameProbe.Changed, renameProbe.Debug);
        await WaitForBlockTextAsync(page, deliveryScope.BlockId, "Delivery Roadmap");
        await page.GetByTestId("document-update-fields").ClickAsync();
        await WaitForTocEntryCountAtLeastAsync(page, 4);
        await WaitForTocTextAsync(page, "Delivery Roadmap");
        var updatedTocScreenshotPath = Path.Combine(output, "updated-table-of-contents.png");
        await page.ScreenshotAsync(new PageScreenshotOptions { Path = updatedTocScreenshotPath, FullPage = true });
        TestContext.AddResultFile(updatedTocScreenshotPath);

        // Wait for the provider PUT of THIS document: the status-bar "boundary" alone can already look clean before the click's save has
        // been issued, and navigating away while the save (and the provider's local mirror of it) is in flight reloads the old seed.
        var saved = page.WaitForResponseAsync(
            response => response.Request.Method == "PUT" && response.Url.Contains("/api/document-editor/documents/" + Phase18DocumentId, StringComparison.Ordinal),
            new PageWaitForResponseOptions { Timeout = 30_000 });
        await page.GetByTestId("document-save").ClickAsync();
        Assert.IsTrue((await saved).Ok, "the save request succeeded");
        await WaitForSaveBoundaryAsync(page);
        await NavigateWithinBlazorAsync(page, "/canvas-engine-host?documentId=phase-5-canvas-render");
        await page.WaitForFunctionAsync(
            "() => document.querySelector('[data-testid=\"document-canvas-page\"]')?.getAttribute('data-canvas-model-document-id') === 'phase-5-canvas-render'",
            new PageWaitForFunctionOptions { Timeout = 20_000 });
        await NavigateWithinBlazorAsync(page, $"/canvas-engine-host?documentId={Phase18DocumentId}&showToolbar=true&autosaveMs=30000");
        await WaitForPhase18ReadyAsync(page);
        await WaitForTocModelEntryCountAtLeastAsync(page, 4);
        await WaitForModelTextAsync(page, "Milestone-18");
        await WaitForModelTextAsync(page, "Milestone-204");

        var reloaded = await ReadProbeAsync(page);
        Assert.AreEqual(Phase18DocumentId, reloaded.ModelDocumentId);
        await WaitForTocTextAsync(page, "Project Tempo");
        await WaitForTocTextAsync(page, "Delivery Roadmap");
        var reloadedTocTexts = await ReadTableOfContentsTextsAsync(page);
        await WaitForBookmarkAsync(page, "phase18-intro-bookmark");
        var reloadedNavigation = await ReadNavigationProbeAsync(page);
        await DocumentEditorCanvasVisualAssert.AssertNoUiOverlapAsync(page);
        var reloadedContentMetrics = await DocumentEditorCanvasVisualAssert.AssertCanvasNonBlankAsync(page.Locator("[data-canvas-layer='content']").First);

        var manifestPath = Path.Combine(output, "manifest.json");
        await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(new
        {
            testName = nameof(Phase18_CanvasSearchReplaceOutlineAndTableOfContentsPersist),
            seedDocumentId = Phase18DocumentId,
            screenshots = new[]
            {
                findScreenshotPath,
                tocScreenshotPath,
                outlineScreenshotPath,
                updatedTocScreenshotPath
            },
            userActions = new[]
            {
                "Open the phase 18 canvas seed document with the production toolbar.",
                "Open replace, enable regex, replace Tempo-number matches with a backreference replacement.",
                "Insert a table of contents from the References ribbon and click the outline panel H2 entry to move the canvas caret.",
                "Define a bookmark on the intro paragraph, edit around it, and navigate back to it with gotoBookmark.",
                "Click a canvas TOC entry, undo it, redo it, rename an H2, and update fields.",
                "Save, navigate away, return, and verify the generated TOC, bookmark, and replaced text survive reload."
            },
            initial,
            reloaded,
            reloadedNavigation,
            visualMetrics = new
            {
                initialContentMetrics,
                reloadedContentMetrics
            }
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));
        TestContext.AddResultFile(manifestPath);
    }

    private async Task OpenPhase18DocumentAsync(IPage page)
    {
        await page.GotoAsync($"{BaseUrl}/canvas-engine-host?documentId={Phase18DocumentId}&showToolbar=true&autosaveMs=30000", new PageGotoOptions
        {
            WaitUntil = WaitUntilState.Load,
            Timeout = 60_000
        });
        await WaitForPhase18ReadyAsync(page);
    }

    private static async Task ResetPhase18ApiDocumentAsync()
    {
        using var client = DemoApiTestHttp.CreateClient();

        using var response = await client.PostAsync("/api/document-editor/reset", content: null);
        response.EnsureSuccessStatusCode();
    }

    private static Task WaitForPhase18ReadyAsync(IPage page)
        => page.WaitForFunctionAsync(
            """
            () => {
                const hostReady = document.querySelector('[data-testid="document-canvas-engine-host"]')?.getAttribute('data-canvas-engine-ready') === 'true';
                const first = document.querySelector('[data-testid="document-canvas-page"]');
                return hostReady && first?.getAttribute('data-canvas-model-document-id') === 'phase-18-canvas-search-outline-toc';
            }
            """,
            new PageWaitForFunctionOptions { Timeout = 30_000 });

    private static Task WaitForSearchMatchesAsync(IPage page, int minimum)
        => page.WaitForFunctionAsync(
            """
            minimum => Number(document.querySelector('[data-testid="document-canvas-engine-root"]')?.getAttribute('data-canvas-search-match-count') || '0') >= minimum
            """,
            minimum,
            new PageWaitForFunctionOptions { Timeout = 10_000 });

    private static Task WaitForTocEntryCountAsync(IPage page, int expectedMinimum)
        => page.WaitForFunctionAsync(
            """
            expected => Number(document.querySelector('[data-testid="document-canvas-page"]')?.getAttribute('data-canvas-model-toc-entry-count') || '0') === expected
            """,
            expectedMinimum,
            new PageWaitForFunctionOptions { Timeout = 10_000 });

    private static Task WaitForTocEntryCountAtLeastAsync(IPage page, int expectedMinimum)
        => page.WaitForFunctionAsync(
            """
            expectedMinimum => Number(document.querySelector('[data-testid="document-canvas-page"]')?.getAttribute('data-canvas-model-toc-entry-count') || '0') >= expectedMinimum
            """,
            expectedMinimum,
            new PageWaitForFunctionOptions { Timeout = 10_000 });

    private static Task WaitForTocHitCountAtLeastAsync(IPage page, int expectedMinimum)
        => page.WaitForFunctionAsync(
            """
            expectedMinimum => Array.from(document.querySelectorAll('[data-testid="document-canvas-page"]'))
                .reduce((total, page) => total + Number(page.getAttribute('data-canvas-toc-hit-count') || '0'), 0) >= expectedMinimum
            """,
            expectedMinimum,
            new PageWaitForFunctionOptions { Timeout = 10_000 });

    private static Task WaitForTocModelEntryCountAtLeastAsync(IPage page, int expectedMinimum)
        => CanvasInteropBridge.WaitForFunctionAsync(page,
            """
            expectedMinimum => {
                const host = document.querySelector('[data-testid="document-canvas-engine-host"]');
                const handle = host?.getAttribute('data-canvas-engine-handle') || '';
                const interop = window.__tmCanvasInterop;
                const model = JSON.parse(interop.getModelJson(handle) || '{}');
                const count = (model?.body?.blocks || []).filter(block => block?.content?.tableOfContents?.isEntry === true).length;
                return count >= expectedMinimum;
            }
            """,
            expectedMinimum,
            new PageWaitForFunctionOptions { Timeout = 10_000 });

    private static Task WaitForSelectionBlockAsync(IPage page, string blockId)
        => page.WaitForFunctionAsync(
            """
            blockId => document.querySelector('[data-testid="document-canvas-engine-root"]')?.getAttribute('data-canvas-selection-focus-block-id') === blockId
            """,
            blockId,
            new PageWaitForFunctionOptions { Timeout = 10_000 });

    private static Task WaitForTocNavigationAsync(IPage page, string targetBlockId)
        => page.WaitForFunctionAsync(
            """
            targetBlockId => {
                const root = document.querySelector('[data-testid="document-canvas-engine-root"]');
                return root?.getAttribute('data-canvas-toc-last-target-block-id') === targetBlockId
                    && root?.getAttribute('data-canvas-toc-last-navigation') === 'true'
                    && root?.getAttribute('data-canvas-selection-focus-block-id') === targetBlockId;
            }
            """,
            targetBlockId,
            new PageWaitForFunctionOptions { Timeout = 10_000 });

    private static Task WaitForTextAsync(IPage page, string text)
        => page.WaitForFunctionAsync(
            """
            text => Array.from(document.querySelectorAll('[data-canvas-text-rect]'))
                .some(item => String(item.getAttribute('data-canvas-text') || '').includes(text))
            """,
            text,
            new PageWaitForFunctionOptions { Timeout = 10_000 });

    private static Task WaitForTocTextAsync(IPage page, string text)
        => CanvasInteropBridge.WaitForFunctionAsync(page,
            """
            text => {
                const host = document.querySelector('[data-testid="document-canvas-engine-host"]');
                const handle = host?.getAttribute('data-canvas-engine-handle') || '';
                const interop = window.__tmCanvasInterop;
                const model = JSON.parse(interop.getModelJson(handle) || '{}');
                const blocks = Array.isArray(model?.body?.blocks) ? model.body.blocks : [];
                return blocks
                    .map(block => block?.content?.tableOfContents?.text || '')
                    .some(value => String(value).includes(text));
            }
            """,
            text,
            new PageWaitForFunctionOptions { Timeout = 10_000 });

    private static Task WaitForBlockTextAsync(IPage page, string blockId, string text)
        => CanvasInteropBridge.WaitForFunctionAsync(page,
            """
            ({ blockId, text }) => {
                const host = document.querySelector('[data-testid="document-canvas-engine-host"]');
                const handle = host?.getAttribute('data-canvas-engine-handle') || '';
                const interop = window.__tmCanvasInterop;
                const model = JSON.parse(interop.getModelJson(handle) || '{}');
                const block = (model?.body?.blocks || []).find(item => item?.id === blockId);
                const value = (block?.content?.runs || []).map(run => String(run?.text || run?.field?.displayText || '')).join('');
                return value.includes(text);
            }
            """,
            new { blockId, text },
            new PageWaitForFunctionOptions { Timeout = 10_000 });

    private static Task WaitForModelTextAsync(IPage page, string text)
        => CanvasInteropBridge.WaitForFunctionAsync(page,
            """
            text => {
                const host = document.querySelector('[data-testid="document-canvas-engine-host"]');
                const handle = host?.getAttribute('data-canvas-engine-handle') || '';
                const interop = window.__tmCanvasInterop;
                const model = JSON.parse(interop.getModelJson(handle) || '{}');
                const value = (model?.body?.blocks || [])
                    .flatMap(block => block?.content?.runs || [])
                    .map(run => String(run?.text || run?.field?.displayText || ''))
                    .join(' ');
                return value.includes(text);
            }
            """,
            text,
            new PageWaitForFunctionOptions { Timeout = 10_000 });

    private static Task WaitForBookmarkAsync(IPage page, string bookmarkName)
        => CanvasInteropBridge.WaitForFunctionAsync(page,
            """
            bookmarkName => {
                const host = document.querySelector('[data-testid="document-canvas-engine-host"]');
                const handle = host?.getAttribute('data-canvas-engine-handle') || '';
                const interop = window.__tmCanvasInterop;
                const navigation = JSON.parse(interop.getNavigationStateJson(handle) || '{}');
                return Array.isArray(navigation?.bookmarks) && navigation.bookmarks.some(item => item?.name === bookmarkName);
            }
            """,
            bookmarkName,
            new PageWaitForFunctionOptions { Timeout = 10_000 });

    private static Task<Phase18Probe> ReadProbeAsync(IPage page)
        => CanvasInteropBridge.EvaluateAsync<Phase18Probe>(page,
            """
            () => {
                const first = document.querySelector('[data-testid="document-canvas-page"]');
                const host = document.querySelector('[data-testid="document-canvas-engine-host"]');
                const handle = host?.getAttribute('data-canvas-engine-handle') || '';
                const interop = window.__tmCanvasInterop;
                const model = JSON.parse(interop.getModelJson(handle) || '{}');
                const text = (model?.body?.blocks || [])
                    .flatMap(block => block?.content?.runs || [])
                    .map(run => String(run?.text || run?.field?.displayText || ''))
                    .join(' ');
                return {
                    modelDocumentId: first?.getAttribute('data-canvas-model-document-id') || '',
                    tocEntryCount: (model?.body?.blocks || []).filter(block => block?.content?.tableOfContents?.isEntry === true).length,
                    searchMatchCount: Number(document.querySelector('[data-testid="document-canvas-engine-root"]')?.getAttribute('data-canvas-search-match-count') || '0'),
                    text
                };
            }
            """);

    private static Task<string[]> ReadTableOfContentsTextsAsync(IPage page)
        => CanvasInteropBridge.EvaluateAsync<string[]>(page,
            """
            () => {
                const host = document.querySelector('[data-testid="document-canvas-engine-host"]');
                const handle = host?.getAttribute('data-canvas-engine-handle') || '';
                if (!handle) {
                    return [];
                }

                const interop = window.__tmCanvasInterop;
                const model = JSON.parse(interop.getModelJson(handle));
                const blocks = Array.isArray(model?.body?.blocks) && model.body.blocks.length > 0
                    ? model.body.blocks
                    : Array.isArray(model?.sections)
                        ? model.sections.flatMap(section => Array.isArray(section?.blocks) ? section.blocks : [])
                        : [];
                return blocks
                    .map(block => block?.content?.tableOfContents?.text || '')
                    .filter(Boolean);
            }
            """);

    private static Task<Phase18HeadingProbe[]> ReadOutlineAsync(IPage page)
        => CanvasInteropBridge.EvaluateAsync<Phase18HeadingProbe[]>(page,
            """
            () => {
                const host = document.querySelector('[data-testid="document-canvas-engine-host"]');
                const handle = host?.getAttribute('data-canvas-engine-handle') || '';
                const interop = window.__tmCanvasInterop;
                const navigation = JSON.parse(interop.getNavigationStateJson(handle) || '{}');
                return (navigation?.outline || []).map(item => ({
                    blockId: String(item?.blockId || ''),
                    text: String(item?.text || ''),
                    level: Number(item?.level || 0),
                    pageNumber: Number(item?.pageNumber || 0)
                }));
            }
            """);

    private static Task<string> ReadBlockTextAsync(IPage page, string blockId)
        => CanvasInteropBridge.EvaluateAsync<string>(page,
            """
            blockId => {
                const host = document.querySelector('[data-testid="document-canvas-engine-host"]');
                const handle = host?.getAttribute('data-canvas-engine-handle') || '';
                const interop = window.__tmCanvasInterop;
                const model = JSON.parse(interop.getModelJson(handle) || '{}');
                const block = (model?.body?.blocks || []).find(item => item?.id === blockId);
                return (block?.content?.runs || []).map(run => String(run?.text || run?.field?.displayText || '')).join('');
            }
            """,
            blockId);

    private static async Task<Phase18CommandProbe> ExecuteCanvasCommandAsync(IPage page, string commandId, object payload)
    {
        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        return await CanvasInteropBridge.EvaluateAsync<Phase18CommandProbe>(page,
            """
            ({ commandId, json }) => {
                const host = document.querySelector('[data-testid="document-canvas-engine-host"]');
                const handle = host?.getAttribute('data-canvas-engine-handle') || '';
                const interop = window.__tmCanvasInterop;
                const raw = interop.execCommand(handle, commandId, json);
                const parsed = JSON.parse(raw || '{}');
                return {
                    handled: parsed?.handled === true,
                    changed: parsed?.result?.changed === true,
                    selectionChanged: parsed?.result?.selectionChanged === true,
                    bookmarkCount: Number(parsed?.result?.bookmarkCount || 0),
                    targetBlockId: String(parsed?.result?.target?.blockId || parsed?.result?.bookmark?.blockId || ''),
                    debug: JSON.stringify(parsed)
                };
            }
            """,
            new { commandId, json });
    }

    private static async Task<Phase18CommandProbe[]> ExecuteCanvasCommandBatchAsync(
        IPage page,
        params (string CommandId, object Payload)[] commands)
    {
        var commandPayload = commands
            .Select(command => new
            {
                commandId = command.CommandId,
                json = JsonSerializer.Serialize(command.Payload, new JsonSerializerOptions(JsonSerializerDefaults.Web))
            })
            .ToArray();

        return await CanvasInteropBridge.EvaluateAsync<Phase18CommandProbe[]>(page,
            """
            commands => {
                const host = document.querySelector('[data-testid="document-canvas-engine-host"]');
                const handle = host?.getAttribute('data-canvas-engine-handle') || '';
                const interop = window.__tmCanvasInterop;
                return commands.map(command => {
                    const raw = interop.execCommand(handle, command.commandId, command.json);
                    const parsed = JSON.parse(raw || '{}');
                    return {
                        handled: parsed?.handled === true,
                        changed: parsed?.result?.changed === true,
                        selectionChanged: parsed?.result?.selectionChanged === true,
                        bookmarkCount: Number(parsed?.result?.bookmarkCount || 0),
                        targetBlockId: String(parsed?.result?.target?.blockId || parsed?.result?.bookmark?.blockId || ''),
                        debug: JSON.stringify(parsed)
                    };
                });
            }
            """,
            commandPayload);
    }

    private static Task<Phase18NavigationProbe> ReadNavigationProbeAsync(IPage page)
        => CanvasInteropBridge.EvaluateAsync<Phase18NavigationProbe>(page,
            """
            () => {
                const host = document.querySelector('[data-testid="document-canvas-engine-host"]');
                const handle = host?.getAttribute('data-canvas-engine-handle') || '';
                const interop = window.__tmCanvasInterop;
                const navigation = JSON.parse(interop.getNavigationStateJson(handle) || '{}');
                return {
                    outline: (navigation?.outline || []).map(item => ({
                        blockId: String(item?.blockId || ''),
                        text: String(item?.text || ''),
                        level: Number(item?.level || 0),
                        pageNumber: Number(item?.pageNumber || 0)
                    })),
                    bookmarks: (navigation?.bookmarks || []).map(item => ({
                        name: String(item?.name || ''),
                        blockId: String(item?.blockId || ''),
                        start: Number(item?.start || 0),
                        end: Number(item?.end || 0)
                    }))
                };
            }
            """);

    private static async Task WaitForSaveBoundaryAsync(IPage page)
    {
        try
        {
            await page.WaitForFunctionAsync(
                """
                () => {
                    const saveMessage = document.querySelector('[data-testid="document-save-message"]')?.textContent || '';
                    const lastSaved = document.querySelector('[data-testid="document-last-saved"]')?.textContent || '';
                    const pending = document.querySelector('[data-testid="document-pending-status"]')?.textContent || '';
                    const dirty = document.querySelector('[data-testid="document-dirty-status"]')?.textContent || '';
                    const saveButtonDisabled = document.querySelector('[data-testid="document-save"]')?.hasAttribute('disabled') === true;
                    return saveButtonDisabled === false
                        && pending.trim().length === 0
                        && dirty.trim().length === 0
                        && (saveMessage.trim().length > 0 || lastSaved.trim().length > 0);
                }
                """,
                new PageWaitForFunctionOptions { Timeout = 10_000 });
        }
        catch (TimeoutException ex)
        {
            var probe = await ReadProbeAsync(page);
            Assert.Fail($"Timed out waiting for phase 18 save boundary. Probe: {JsonSerializer.Serialize(probe, new JsonSerializerOptions(JsonSerializerDefaults.Web))}. {ex.Message}");
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

    private static string CreateOutputDirectory(string testName)
    {
        var output = Path.Combine(
            FindRepositoryRoot().FullName,
            "tests",
            "Tempo.Blazor.E2E",
            "TestResults",
            "document-editor-canvas",
            "phase18-search-outline-toc",
            "2026-06-04",
            testName);
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

    private sealed class Phase18Probe
    {
        public string ModelDocumentId { get; set; } = string.Empty;

        public int TocEntryCount { get; set; }

        public int SearchMatchCount { get; set; }

        public string Text { get; set; } = string.Empty;
    }

    private sealed class Phase18HeadingProbe
    {
        public string BlockId { get; set; } = string.Empty;

        public string Text { get; set; } = string.Empty;

        public int Level { get; set; }

        public int PageNumber { get; set; }
    }

    private sealed class Phase18BookmarkProbe
    {
        public string Name { get; set; } = string.Empty;

        public string BlockId { get; set; } = string.Empty;

        public int Start { get; set; }

        public int End { get; set; }
    }

    private sealed class Phase18NavigationProbe
    {
        public Phase18HeadingProbe[] Outline { get; set; } = [];

        public Phase18BookmarkProbe[] Bookmarks { get; set; } = [];
    }

    private sealed class Phase18CommandProbe
    {
        public bool Handled { get; set; }

        public bool Changed { get; set; }

        public bool SelectionChanged { get; set; }

        public int BookmarkCount { get; set; }

        public string TargetBlockId { get; set; } = string.Empty;

        public string Debug { get; set; } = string.Empty;
    }
}
