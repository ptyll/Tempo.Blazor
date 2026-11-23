using FluentAssertions;
using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>Phase 20 E2E coverage for live canvas collaboration, presence, and convergence.</summary>
[TestClass]
[TestCategory("DocumentEditor")]
[TestCategory("DocumentEditor:CanvasEngine")]
[DoNotParallelize]
public sealed class DocumentEditorCanvasCollaborationE2ETests : WasmTestBase
{
    private const string BodyBlockId = "canvas-typing-body";
    private const string CollaborationDocumentPrefix = "phase-20-canvas-collaboration-offline";

    [TestMethod]
    public async Task ReceiverCanTypeAfterRemoteApply_PresenceCaretDoesNotBlockClicks()
    {
        // Regression for the Phase B operation-relay collaboration bug: a remote collaborator's presence
        // caret renders at the end of the text — exactly where the local user clicks to resume editing. The
        // overlay used to capture that click (pointer-events), so the canvas never received the mousedown,
        // the hidden input was not focused and the user could no longer type. The overlay is now
        // pointer-events:none. Here: A receives B's edit, then A must still be able to click + type.
        var documentId = $"{CollaborationDocumentPrefix}-{Guid.NewGuid():N}";
        var ctxA = await CreateContextAsync();
        var ctxB = await CreateContextAsync();
        var pageA = await OpenCanvasCollaborationTabAsync(ctxA, documentId);
        var pageB = await OpenCanvasCollaborationTabAsync(ctxB, documentId);
        await WaitForMirrorTextAsync(pageA, BodyBlockId, "Start");
        await WaitForMirrorTextAsync(pageB, BodyBlockId, "Start");

        // B edits -> A receives the remote apply and shows B's presence caret at the text end.
        await TypeAtEndAsync(pageB, BodyBlockId, "-B");
        await WaitForMirrorTextContainsAsync(pageA, BodyBlockId, "-B");

        // The remote caret overlay must not intercept pointer events.
        var caretBlocksClicks = await pageA.EvaluateAsync<bool>(
            @"() => { const c = document.querySelector('[data-testid=""document-canvas-remote-caret""]'); return c ? getComputedStyle(c).pointerEvents !== 'none' : false; }");
        caretBlocksClicks.Should().BeFalse("the presence caret overlay must be pointer-events:none");

        // A clicks where B's caret is (end of text) and types — this must work.
        await TypeAtEndAsync(pageA, BodyBlockId, "-A");
        await WaitForMirrorTextContainsAsync(pageA, BodyBlockId, "-B-A");
        (await ReadMirrorTextAsync(pageA, BodyBlockId)).Should().Be("Start-B-A");
    }

    [TestMethod]
    public async Task Phase20_TwoCanvasEditors_ConvergeAndRenderRemoteCaret_OverSignalR()
    {
        var documentId = $"{CollaborationDocumentPrefix}-{Guid.NewGuid():N}";
        var ctxA = await CreateContextAsync();
        var ctxB = await CreateContextAsync();
        var pageA = await OpenCanvasCollaborationTabAsync(ctxA, documentId);
        var pageB = await OpenCanvasCollaborationTabAsync(ctxB, documentId);

        (await ReadMirrorTextAsync(pageA, BodyBlockId)).Should().Be("Start");
        (await ReadMirrorTextAsync(pageB, BodyBlockId)).Should().Be("Start");

        await TypeAtEndAsync(pageA, BodyBlockId, "-A");
        await WaitForMirrorTextAsync(pageB, BodyBlockId, "Start-A");

        await pageA.Keyboard.PressAsync("ArrowLeft");
        await WaitForRemoteCaretAsync(pageB);
        var presenceProbe = await ReadPresenceProbeAsync(pageB);
        presenceProbe.CursorCount.Should().BeGreaterThan(0);
        presenceProbe.LabelWidth.Should().BeLessThan(224);
        presenceProbe.LabelBottom.Should().BeLessThanOrEqualTo(presenceProbe.CaretTop + 1);

        var screenshotDirectory = Path.Combine(TestContext.TestResultsDirectory ?? ".", "phase20-canvas-collaboration");
        Directory.CreateDirectory(screenshotDirectory);
        var remoteCaretScreenshot = Path.Combine(screenshotDirectory, "remote-caret.png");
        await pageB.GetByTestId("document-canvas-engine-host").ScreenshotAsync(new LocatorScreenshotOptions
        {
            Path = remoteCaretScreenshot,
            Type = ScreenshotType.Png
        });
        TestContext.AddResultFile(remoteCaretScreenshot);

        await TypeAtEndAsync(pageB, BodyBlockId, "-B");
        await WaitForMirrorTextContainsAsync(pageA, BodyBlockId, "-B");

        var finalA = await ReadMirrorTextAsync(pageA, BodyBlockId);
        var finalB = await ReadMirrorTextAsync(pageB, BodyBlockId);
        finalA.Should().Be(finalB);
        finalA.Should().Be("Start-A-B");
    }

    [TestMethod]
    public async Task Phase20_OfflineDraft_ReconnectsAndSyncsCanvasModel()
    {
        var documentId = $"{CollaborationDocumentPrefix}-{Guid.NewGuid():N}";
        var context = await CreateContextAsync();
        var page = await OpenCanvasCollaborationTabAsync(context, documentId, failSaves: true);
        var output = CreateOutputDirectory(nameof(Phase20_OfflineDraft_ReconnectsAndSyncsCanvasModel));
        var offlineScreenshot = Path.Combine(output, "offline-draft.png");
        var syncedScreenshot = Path.Combine(output, "synced-after-reconnect.png");

        await TypeAtEndAsync(page, BodyBlockId, "-offline");
        await WaitForMirrorTextAsync(page, BodyBlockId, "Start-offline");
        await page.GetByTestId("document-save").ClickAsync();
        await Assertions.Expect(page.GetByTestId("document-save-message"))
            .ToContainTextAsync("Demo autosave provider failed", new() { Timeout = 10_000 });
        await Assertions.Expect(page.GetByTestId("document-offline-banner"))
            .ToBeVisibleAsync(new() { Timeout = 5_000 });
        await page.GetByTestId("document-editor-demo").ScreenshotAsync(new LocatorScreenshotOptions
        {
            Path = offlineScreenshot,
            Type = ScreenshotType.Png
        });

        await NavigateWithinBlazorAsync(
            page,
            $"/canvas-engine-host?documentId={Uri.EscapeDataString(documentId)}&showToolbar=true&autosaveMs=30000&failSaves=false");
        await page.GetByTestId("document-save-retry").ClickAsync();
        await Assertions.Expect(page.GetByTestId("document-save-message"))
            .ToContainTextAsync("Saved", new() { Timeout = 10_000 });
        await WaitForDirtyStateAsync(page, expectedDirty: false);
        await Assertions.Expect(page.GetByTestId("document-offline-banner"))
            .Not.ToBeVisibleAsync(new() { Timeout = 10_000 });

        var verificationContext = await CreateContextAsync();
        var verificationPage = await OpenCanvasCollaborationTabAsync(verificationContext, documentId);
        await WaitForMirrorTextAsync(verificationPage, BodyBlockId, "Start-offline");
        await verificationPage.GetByTestId("document-editor-demo").ScreenshotAsync(new LocatorScreenshotOptions
        {
            Path = syncedScreenshot,
            Type = ScreenshotType.Png
        });

        TestContext.AddResultFile(offlineScreenshot);
        TestContext.AddResultFile(syncedScreenshot);
    }

    private async Task<IPage> OpenCanvasCollaborationTabAsync(IBrowserContext context, string documentId, bool failSaves = false)
    {
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(1440, 1000);
        await GotoAppReadyAsync(page, $"{BaseUrl}/canvas-engine-host?documentId={Uri.EscapeDataString(documentId)}&showToolbar=true&autosaveMs=30000&failSaves={failSaves.ToString().ToLowerInvariant()}");
        await page.WaitForSelectorAsync(
            "[data-testid='document-canvas-engine-host'][data-canvas-engine-ready='true']",
            new PageWaitForSelectorOptions { Timeout = 45_000 });
        await page.WaitForFunctionAsync(
            """
            (blockId) => document.querySelectorAll(`[data-canvas-text-rect][data-block-id="${blockId}"]`).length >= 1
                && document.querySelector('[data-testid="document-canvas-hidden-input"]')
            """,
            BodyBlockId,
            new PageWaitForFunctionOptions { Timeout = 45_000 });
        return page;
    }

    private static async Task TypeAtEndAsync(IPage page, string blockId, string text)
    {
        var point = await ReadTextPointAsync(page, blockId);
        await page.Mouse.ClickAsync((float)point.X, (float)point.Y);
        await page.Keyboard.PressAsync("End");
        await page.Keyboard.TypeAsync(text, new KeyboardTypeOptions { Delay = 20 });
    }

    private static Task<CanvasPoint> ReadTextPointAsync(IPage page, string blockId)
        => page.EvaluateAsync<CanvasPoint>(
            """
            (blockId) => {
                const rects = Array.from(document.querySelectorAll(`[data-canvas-text-rect][data-block-id="${blockId}"]`));
                const node = rects[rects.length - 1];
                const rect = node.getBoundingClientRect();
                return { x: rect.right - 1, y: rect.top + rect.height / 2 };
            }
            """,
            blockId);

    private static Task<string> ReadMirrorTextAsync(IPage page, string blockId)
        => page.EvaluateAsync<string>(
            """
            (blockId) => document.querySelector(`[data-testid="document-canvas-a11y-mirror"] [data-block-id="${blockId}"]`)?.textContent || ''
            """,
            blockId);

    private static Task WaitForMirrorTextAsync(IPage page, string blockId, string expected)
        => page.WaitForFunctionAsync(
            """
            ([blockId, expected]) => document.querySelector(`[data-testid="document-canvas-a11y-mirror"] [data-block-id="${blockId}"]`)?.textContent === expected
            """,
            new object[] { blockId, expected },
            new PageWaitForFunctionOptions { Timeout = 30_000 });

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

    private string CreateOutputDirectory(string testName)
    {
        var output = Path.Combine(
            FindRepositoryRoot().FullName,
            "tests",
            "Tempo.Blazor.E2E",
            "TestResults",
            "document-editor-canvas",
            nameof(DocumentEditorCanvasCollaborationE2ETests),
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

    private static Task WaitForMirrorTextContainsAsync(IPage page, string blockId, string expected)
        => page.WaitForFunctionAsync(
            """
            ([blockId, expected]) => (document.querySelector(`[data-testid="document-canvas-a11y-mirror"] [data-block-id="${blockId}"]`)?.textContent || '').includes(expected)
            """,
            new object[] { blockId, expected },
            new PageWaitForFunctionOptions { Timeout = 30_000 });

    private static Task WaitForRemoteCaretAsync(IPage page)
        => WaitForPredicateAsync(page, """
            () => {
                const caret = document.querySelector('[data-testid="document-canvas-remote-caret"]');
                return caret && caret.getBoundingClientRect().height > 8 && caret.querySelector('.tm-document-canvas-presence__label')?.textContent?.trim();
            }
            """, 15_000);

    private static Task<PresenceProbe> ReadPresenceProbeAsync(IPage page)
        => page.EvaluateAsync<PresenceProbe>(
            """
            () => {
                const caret = document.querySelector('[data-testid="document-canvas-remote-caret"]');
                const label = caret?.querySelector('.tm-document-canvas-presence__label');
                const caretRect = caret?.getBoundingClientRect() || new DOMRect();
                const labelRect = label?.getBoundingClientRect() || new DOMRect();
                return {
                    cursorCount: document.querySelectorAll('[data-testid="document-canvas-remote-caret"]').length,
                    caretTop: caretRect.top,
                    labelBottom: labelRect.bottom,
                    labelWidth: labelRect.width
                };
            }
            """);

    private sealed class CanvasPoint
    {
        public double X { get; set; }

        public double Y { get; set; }
    }

    private sealed class PresenceProbe
    {
        public int CursorCount { get; set; }

        public double CaretTop { get; set; }

        public double LabelBottom { get; set; }

        public double LabelWidth { get; set; }
    }
}
