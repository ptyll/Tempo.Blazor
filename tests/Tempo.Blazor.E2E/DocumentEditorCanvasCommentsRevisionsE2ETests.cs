using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Tempo.Blazor.E2E.CanvasEngine;

namespace Tempo.Blazor.E2E;

/// <summary>Phase 17 E2E coverage for canvas comments, revisions, and restricted editing.</summary>
[TestClass]
[TestCategory("DocumentEditor")]
[TestCategory("DocumentEditor:CanvasEngine")]
[DoNotParallelize]
public sealed class DocumentEditorCanvasCommentsRevisionsE2ETests : WasmTestBase
{
    private const string Phase17DocumentId = "phase-17-canvas-comments-revisions";

    [TestInitialize]
    public Task ResetDocumentEditorDemoAsync()
        => DocumentEditorE2EReset.ResetAsync();

    [TestMethod]
    public async Task Phase17_CommentsRevisionsAndRestrictedEditing_RenderAndReviewFromCanvas()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(1440, 1000);
        await OpenPhase17DocumentAsync(page);

        var output = CreateOutputDirectory(nameof(Phase17_CommentsRevisionsAndRestrictedEditing_RenderAndReviewFromCanvas));
        var beforePath = Path.Combine(output, "00-phase17-before.png");
        var addCommentPath = Path.Combine(output, "01-phase17-add-comment-from-selection.png");
        var commentPath = Path.Combine(output, "02-phase17-comment-reply-resolved-reopened.png");
        var trackChangesPath = Path.Combine(output, "03-phase17-track-changes-typing-deletion.png");
        var deleteCommentPath = Path.Combine(output, "04-phase17-delete-comment.png");
        var afterReloadPath = Path.Combine(output, "05-phase17-after-save-reload.png");
        var manifestPath = Path.Combine(output, "manifest.json");
        var replyText = $"Canvas phase 17 reply {DateTimeOffset.UtcNow:HHmmssfff}";
        var createdCommentId = $"canvas-phase17-e2e-comment-{Guid.NewGuid():N}";
        var createdCommentText = $"Canvas phase 17 selection comment {DateTimeOffset.UtcNow:HHmmssfff}";

        await Assertions.Expect(page.Locator("[data-testid='document-canvas-comment-marker'][data-comment-id='canvas-phase17-comment']").First)
            .ToBeVisibleAsync(new() { Timeout = 10_000 });
        await Assertions.Expect(page.Locator("[data-testid='document-canvas-revision-marker'][data-revision-id='canvas-phase17-revision-insert']").First)
            .ToBeVisibleAsync(new() { Timeout = 10_000 });
        await Assertions.Expect(page.Locator("[data-testid='document-canvas-revision-marker'][data-revision-id='canvas-phase17-revision-delete']").First)
            .ToBeVisibleAsync(new() { Timeout = 10_000 });
        await Assertions.Expect(page.Locator("[data-testid='document-canvas-revision-marker'][data-revision-id='canvas-phase17-revision-format']").First)
            .ToBeVisibleAsync(new() { Timeout = 10_000 });
        var initialProbe = await ReadPhase17ProbeAsync(page);
        Assert.AreEqual(3, initialProbe.PendingRevisionCount, "The phase 17 seed must start with insertion, deletion, and formatting revisions.");
        Assert.AreEqual(1, initialProbe.CommentCount, "The phase 17 seed must start with one anchored comment thread.");
        await page.GetByTestId("document-editor-demo").ScreenshotAsync(new LocatorScreenshotOptions
        {
            Path = beforePath,
            Type = ScreenshotType.Png
        });

        await SelectCanvasTextRangeAsync(page, "canvas-phase17-protected", 14, 22);
        var addCommentResult = await ExecuteCanvasCommandAsync(page, "upsertComment", new
        {
            comment = new
            {
                id = createdCommentId,
                status = "Open",
                entries = new[]
                {
                    new
                    {
                        id = $"{createdCommentId}-entry",
                        text = createdCommentText,
                        author = new
                        {
                            id = "canvas-demo-user",
                            displayName = "Canvas Demo User"
                        },
                        createdAt = DateTimeOffset.UtcNow.ToString("O")
                    }
                }
            }
        });
        Assert.IsTrue(addCommentResult.Handled, addCommentResult.Debug);
        Assert.IsTrue(addCommentResult.Changed, addCommentResult.Debug);
        Assert.AreEqual(createdCommentId, addCommentResult.CommentId, addCommentResult.Debug);
        Assert.AreEqual("canvas-phase17-protected", addCommentResult.BlockId, addCommentResult.Debug);
        Assert.IsTrue(addCommentResult.AnchorEndOffset > addCommentResult.AnchorStartOffset, addCommentResult.Debug);
        await WaitForLastCanvasCommandAsync(page, "upsertComment");
        await Assertions.Expect(page.Locator($"[data-testid='document-canvas-comment-marker'][data-comment-id='{createdCommentId}']").First)
            .ToBeVisibleAsync(new() { Timeout = 10_000 });
        await page.GetByTestId("document-side-panel-tab-comments").ClickAsync();
        await Assertions.Expect(page.GetByTestId("document-comment-list"))
            .ToContainTextAsync(createdCommentText, new() { Timeout = 10_000 });
        await page.Locator($"[data-testid='document-canvas-comment-marker'][data-comment-id='{createdCommentId}']").First.ClickAsync();
        await WaitForCanvasSelectionAsync(page, "canvas-phase17-protected", addCommentResult.AnchorStartOffset);
        await Assertions.Expect(page.Locator($"[data-testid='document-comment-thread'][data-comment-id='{createdCommentId}']").First)
            .ToHaveClassAsync(new Regex("selected"), new() { Timeout = 10_000 });
        await page.GetByTestId("document-editor-demo").ScreenshotAsync(new LocatorScreenshotOptions
        {
            Path = addCommentPath,
            Type = ScreenshotType.Png
        });

        await page.GetByTestId("document-save").ClickAsync();
        await WaitForSaveBoundaryAsync(page);
        await page.Locator($"[data-testid='document-comment-thread'][data-comment-id='{createdCommentId}'] [data-testid='document-comment-delete']").ClickAsync();
        await Assertions.Expect(page.Locator($"[data-testid='document-canvas-comment-marker'][data-comment-id='{createdCommentId}']"))
            .ToHaveCountAsync(0, new() { Timeout = 10_000 });
        await Assertions.Expect(page.Locator($"[data-testid='document-comment-thread'][data-comment-id='{createdCommentId}']"))
            .ToHaveCountAsync(0, new() { Timeout = 10_000 });
        await page.GetByTestId("document-editor-demo").ScreenshotAsync(new LocatorScreenshotOptions
        {
            Path = deleteCommentPath,
            Type = ScreenshotType.Png
        });

        await page.Locator("[data-testid='document-canvas-comment-marker'][data-comment-id='canvas-phase17-comment']").First.ClickAsync();
        await Assertions.Expect(page.GetByTestId("document-side-panel-tab-comments"))
            .ToHaveAttributeAsync("aria-selected", "true", new() { Timeout = 10_000 });
        await Assertions.Expect(page.Locator("[data-testid='document-comment-thread'][data-comment-id='canvas-phase17-comment']").First)
            .ToHaveClassAsync(new Regex("selected"), new() { Timeout = 10_000 });

        await page.GetByTestId("document-comment-reply-input").FillAsync(replyText);
        await page.GetByTestId("document-comment-reply-submit").ClickAsync();
        await Assertions.Expect(page.GetByTestId("document-comment-list"))
            .ToContainTextAsync(replyText, new() { Timeout = 10_000 });
        await page.GetByTestId("document-comment-resolve").ClickAsync();
        await Assertions.Expect(page.Locator("[data-testid='document-canvas-comment-marker'][data-comment-id='canvas-phase17-comment']").First)
            .ToHaveAttributeAsync("data-canvas-comment-status", "resolved", new() { Timeout = 10_000 });
        await page.GetByTestId("document-comment-reopen").ClickAsync();
        await Assertions.Expect(page.Locator("[data-testid='document-canvas-comment-marker'][data-comment-id='canvas-phase17-comment']").First)
            .ToHaveAttributeAsync("data-canvas-comment-status", "open", new() { Timeout = 10_000 });
        await page.GetByTestId("document-editor-demo").ScreenshotAsync(new LocatorScreenshotOptions
        {
            Path = commentPath,
            Type = ScreenshotType.Png
        });

        await SetCanvasTrackChangesAsync(page, enabled: true);
        await WaitForCanvasTrackChangesStateAsync(page, enabled: true);
        await ClickCanvasBlockAsync(page, "canvas-phase17-protected", 22);
        await FocusHiddenCanvasInputAsync(page);
        await page.Keyboard.TypeAsync(" TCX");
        await WaitForMirrorTextContainsAsync(page, "canvas-phase17-protected", "editable TCX island");
        await page.Keyboard.PressAsync("Backspace");
        await WaitForTrackedRevisionMarkerCountAsync(page, minimumCount: 5);
        var trackedProbe = await ReadPhase17ProbeAsync(page);
        Assert.IsTrue(trackedProbe.PendingRevisionCount >= 5, $"Track changes typing and deletion must create pending canvas revisions. Probe: {JsonSerializer.Serialize(trackedProbe)}");
        Assert.IsTrue(trackedProbe.TrackedInsertionCount >= 1, $"Track changes typing must create an insertion revision. Probe: {JsonSerializer.Serialize(trackedProbe)}");
        Assert.IsTrue(trackedProbe.TrackedDeletionCount >= 2, $"Track changes deletion must create a deletion revision in addition to the seed deletion. Probe: {JsonSerializer.Serialize(trackedProbe)}");
        await page.GetByTestId("document-editor-demo").ScreenshotAsync(new LocatorScreenshotOptions
        {
            Path = trackChangesPath,
            Type = ScreenshotType.Png
        });

        await page.Locator("[data-testid='document-canvas-revision-marker'][data-revision-id='canvas-phase17-revision-delete']").First.ClickAsync();
        await Assertions.Expect(page.GetByTestId("document-side-panel-tab-revisions"))
            .ToHaveAttributeAsync("aria-selected", "true", new() { Timeout = 10_000 });
        await Assertions.Expect(page.Locator("[data-testid='document-revision-item'][data-revision-id='canvas-phase17-revision-delete']").First)
            .ToHaveClassAsync(new Regex("selected"), new() { Timeout = 10_000 });
        await WaitForRevisionPanelSettledAsync(page);
        await page.Locator("[data-testid='document-revision-item'][data-revision-id='canvas-phase17-revision-delete'] [data-testid='document-revision-accept']").ClickAsync();
        await WaitForRevisionActionAsync(page, "canvas-phase17-revision-delete", "accepted");
        await Assertions.Expect(page.Locator("[data-testid='document-canvas-revision-marker'][data-revision-id='canvas-phase17-revision-delete']"))
            .ToHaveCountAsync(0, new() { Timeout = 30_000 });

        await page.GetByTestId("document-side-panel-tab-revisions").ClickAsync();
        await page.GetByTestId("document-revision-reject-all").ClickAsync();
        await WaitForPendingRevisionCountAsync(page, 0);
        await Assertions.Expect(page.Locator("[data-testid='document-canvas-revision-marker']"))
            .ToHaveCountAsync(0, new() { Timeout = 30_000 });

        await SetCanvasTrackChangesAsync(page, enabled: false);
        await WaitForCanvasTrackChangesStateAsync(page, enabled: false);
        await WaitForMirrorToMatchModelAsync(page, "canvas-phase17-protected");
        var beforeProtectedText = await ReadMirrorTextAsync(page, "canvas-phase17-protected");
        var undoBeforeBlocked = await ReadCanvasUndoDepthAsync(page);
        await ClickCanvasBlockAsync(page, "canvas-phase17-protected", 3);
        await FocusHiddenCanvasInputAsync(page);
        await page.Keyboard.TypeAsync("BLOCKED");
        await Task.Delay(250);
        Assert.AreEqual(beforeProtectedText, await ReadMirrorTextAsync(page, "canvas-phase17-protected"), "Typing outside an editable region in a protected canvas document must be blocked.");
        Assert.AreEqual(undoBeforeBlocked, await ReadCanvasUndoDepthAsync(page), "Blocked protected-region typing must not create an undo transaction.");

        await ClickCanvasBlockAsync(page, "canvas-phase17-protected", 22);
        await FocusHiddenCanvasInputAsync(page);
        await page.Keyboard.TypeAsync(" allowed");
        await WaitForMirrorTextContainsAsync(page, "canvas-phase17-protected", "editable allowed");

        await page.GetByTestId("document-save").ClickAsync();
        await WaitForSaveBoundaryAsync(page);
        await NavigateWithinBlazorAsync(page, "/canvas-engine-host?documentId=phase-5-canvas-render");
        await page.WaitForFunctionAsync(
            "() => document.querySelector('[data-testid=\"document-canvas-page\"]')?.getAttribute('data-canvas-model-document-id') === 'phase-5-canvas-render'",
            options: new PageWaitForFunctionOptions { Timeout = 20_000 });
        await NavigateWithinBlazorAsync(page, $"/canvas-engine-host?documentId={Phase17DocumentId}&showToolbar=true");
        await OpenPhase17DocumentReadyAsync(page);
        await WaitForMirrorTextContainsAsync(page, "canvas-phase17-protected", "editable allowed");
        await Assertions.Expect(page.GetByTestId("document-comment-list"))
            .ToContainTextAsync(replyText, new() { Timeout = 10_000 });
        await Assertions.Expect(page.Locator("[data-testid='document-canvas-comment-marker'][data-comment-id='canvas-phase17-comment']").First)
            .ToHaveAttributeAsync("data-canvas-comment-status", "open", new() { Timeout = 10_000 });
        await Assertions.Expect(page.Locator("[data-testid='document-canvas-revision-marker']"))
            .ToHaveCountAsync(0, new() { Timeout = 10_000 });
        await page.GetByTestId("document-editor-demo").ScreenshotAsync(new LocatorScreenshotOptions
        {
            Path = afterReloadPath,
            Type = ScreenshotType.Png
        });

        var reloadedProbe = await ReadPhase17ProbeAsync(page);
        await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(new
        {
            testName = nameof(Phase17_CommentsRevisionsAndRestrictedEditing_RenderAndReviewFromCanvas),
            seedDocumentId = Phase17DocumentId,
            userActions = new[]
            {
                "Open the phase 17 canvas document through the production TmDocumentEditor route.",
                "Drag a real canvas text selection, create a new anchored comment from the selection, select its marker, and verify the caret returns to the anchor.",
                "Save the newly created comment and delete it through the comment rail using the provider-backed UI action.",
                "Select a canvas comment marker, reply, resolve, and reopen the thread from the comment rail.",
                "Enable Track Changes, type and delete inside the protected editable region, and verify insertion/deletion revision markers.",
                "Accept one deletion revision, reject all remaining revisions, and verify canvas markers disappear.",
                "Attempt typing outside a protected editable region and verify the model and undo depth do not change.",
                "Type inside the editable region with Track Changes disabled, save, navigate away, reload, and verify comments/revisions/restricted edits persist."
            },
            expectedVisibleChanges = "Comment and revision overlays are colored, readable, and do not inflate the text layout; a newly selected comment opens the rail and returns the caret to its anchor; delete removes the marker before reload.",
            expectedModelChanges = "Selection-based add comment, provider-backed delete comment, reply, resolve, reopen, track-changes insertion/deletion, revision review, protected-region blocking, allowed protected-region editing, save, and reload flow through the production TmDocumentEditor canvas route.",
            screenshotPaths = new[] { beforePath, addCommentPath, commentPath, trackChangesPath, deleteCommentPath, afterReloadPath },
            addCommentResult,
            createdCommentId,
            initialProbe,
            trackedProbe,
            reloadedProbe
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));

        TestContext.AddResultFile(beforePath);
        TestContext.AddResultFile(addCommentPath);
        TestContext.AddResultFile(commentPath);
        TestContext.AddResultFile(trackChangesPath);
        TestContext.AddResultFile(deleteCommentPath);
        TestContext.AddResultFile(afterReloadPath);
        TestContext.AddResultFile(manifestPath);
        Assert.IsTrue(reloadedProbe.CommentMarkerCount >= 1, "Comment overlay marker must remain visible after reopen and reload.");
        Assert.IsFalse(reloadedProbe.CommentReplyText.Contains(createdCommentText, StringComparison.Ordinal), $"Deleted E2E-created comment must not survive save/reload. Probe: {JsonSerializer.Serialize(reloadedProbe)}");
        Assert.AreEqual(0, reloadedProbe.RevisionMarkerCount, "All seeded and E2E-created pending revisions should have been reviewed by the E2E flow.");
        Assert.AreEqual(0, reloadedProbe.PendingRevisionCount, "No pending revisions should survive the review-all save/reload boundary.");
        Assert.IsTrue(reloadedProbe.AcceptedRevisionCount >= 1, $"Accepted revision decisions must persist. Probe: {JsonSerializer.Serialize(reloadedProbe)}");
        Assert.IsTrue(reloadedProbe.RejectedRevisionCount >= 1, $"Rejected revision decisions must persist. Probe: {JsonSerializer.Serialize(reloadedProbe)}");
        Assert.IsTrue(reloadedProbe.CommentReplyText.Contains(replyText, StringComparison.Ordinal), $"Comment reply must persist after reload. Probe: {JsonSerializer.Serialize(reloadedProbe)}");
        await DocumentEditorCanvasVisualAssert.AssertNoUiOverlapAsync(page);
        await DocumentEditorCanvasVisualAssert.AssertCanvasNonBlankAsync(page.Locator("[data-canvas-layer='content']").First);
        await DocumentEditorCanvasVisualAssert.AssertCanvasNonBlankAsync(page.Locator("[data-canvas-layer='annotations']").First);
    }

    private async Task OpenPhase17DocumentAsync(IPage page)
    {
        await page.GotoAsync($"{BaseUrl}/canvas-engine-host?documentId={Phase17DocumentId}&showToolbar=true", new PageGotoOptions
        {
            WaitUntil = WaitUntilState.DOMContentLoaded,
            Timeout = 60_000
        });
        await page.WaitForFunctionAsync(
            """
            () => document.querySelector('[data-testid="document-canvas-engine-host"]')
                ?.getAttribute('data-canvas-engine-ready') === 'true'
            """,
            null,
            new PageWaitForFunctionOptions { Timeout = 30_000 });
        await OpenPhase17DocumentReadyAsync(page);
    }

    private static Task OpenPhase17DocumentReadyAsync(IPage page)
        => page.WaitForFunctionAsync(
            """
            () => document.querySelector('[data-testid="document-canvas-comment-overlay"]')
                && document.querySelector('[data-testid="document-canvas-revision-overlay"]')
                && document.querySelector('[data-testid="document-canvas-page"]')?.getAttribute('data-canvas-model-document-id') === 'phase-17-canvas-comments-revisions'
                && document.querySelectorAll('[data-testid="document-canvas-comment-marker"][data-comment-id="canvas-phase17-comment"]').length >= 1
            """,
            new PageWaitForFunctionOptions { Timeout = 30_000 });

    private static Task WaitForTrackedRevisionMarkerCountAsync(IPage page, int minimumCount)
        => page.WaitForFunctionAsync(
            """
            minimumCount => document.querySelectorAll('[data-testid="document-canvas-revision-marker"]').length >= Number(minimumCount)
            """,
            minimumCount,
            new PageWaitForFunctionOptions { Timeout = 10_000 });

    private static Task<int> ReadCanvasUndoDepthAsync(IPage page)
        => page.EvaluateAsync<int>(
            """
            async () => {
                const host = document.querySelector('[data-testid="document-canvas-engine-host"]');
                const handle = host?.getAttribute('data-canvas-engine-handle') || '';
                const module = window.__tmDocumentCanvasInteropModule ||= await import('/_content/Tempo.Blazor.DocumentEditor/js/document-editor-canvas/interop.mjs');
                const state = JSON.parse(module.getUndoStateJson(handle) || '{}');
                return Number(state.undoDepth || (state.canUndo ? 1 : 0) || 0);
            }
            """);

    private static Task SetCanvasTrackChangesAsync(IPage page, bool enabled)
        => page.EvaluateAsync(
            """
            async enabled => {
                const host = document.querySelector('[data-testid="document-canvas-engine-host"]');
                const handle = host?.getAttribute('data-canvas-engine-handle') || '';
                const module = window.__tmDocumentCanvasInteropModule ||= await import('/_content/Tempo.Blazor.DocumentEditor/js/document-editor-canvas/interop.mjs');
                module.setTrackChangesEnabled(handle, enabled === true, JSON.stringify({
                    id: 'canvas-demo-user',
                    displayName: 'Canvas Demo User'
                }));
            }
            """,
            enabled);

    private static Task WaitForCanvasTrackChangesStateAsync(IPage page, bool enabled)
        => page.WaitForFunctionAsync(
            """
            enabled => document.querySelector('[data-testid="document-canvas-engine-root"]')
                ?.getAttribute('data-canvas-track-changes-enabled') === String(enabled === true)
            """,
            enabled,
            new PageWaitForFunctionOptions { Timeout = 10_000 });

    private static async Task WaitForRevisionActionAsync(IPage page, string revisionId, string expectedAction)
    {
        await EnsureCanvasInteropModuleAsync(page);
        await page.WaitForFunctionAsync(
            """
            ({ revisionId, expectedAction }) => {
                const host = document.querySelector('[data-testid="document-canvas-engine-host"]');
                const handle = host?.getAttribute('data-canvas-engine-handle') || '';
                const module = window.__tmDocumentCanvasInteropModule;
                if (!handle || !module) return false;
                const model = JSON.parse(module.getModelJson(handle) || '{}');
                const revision = (Array.isArray(model.revisions) ? model.revisions : [])
                    .find(item => String(item?.id || item?.Id || '') === String(revisionId || ''));
                return String(revision?.action || revision?.Action || '').toLowerCase() === String(expectedAction || '').toLowerCase();
            }
            """,
            new { revisionId, expectedAction },
            new PageWaitForFunctionOptions { Timeout = 30_000 });
    }

    private static async Task WaitForPendingRevisionCountAsync(IPage page, int expectedCount)
    {
        await EnsureCanvasInteropModuleAsync(page);
        await page.WaitForFunctionAsync(
            """
            expectedCount => {
                const host = document.querySelector('[data-testid="document-canvas-engine-host"]');
                const handle = host?.getAttribute('data-canvas-engine-handle') || '';
                const module = window.__tmDocumentCanvasInteropModule;
                if (!handle || !module) return false;
                const model = JSON.parse(module.getModelJson(handle) || '{}');
                const revisions = Array.isArray(model.revisions) ? model.revisions : [];
                const pending = revisions.filter(item => String(item?.action || item?.Action || '').toLowerCase() === 'pending').length;
                return pending === Number(expectedCount);
            }
            """,
            expectedCount,
            new PageWaitForFunctionOptions { Timeout = 30_000 });
    }

    /// <summary>
    /// Loads the canvas interop module into <c>window.__tmDocumentCanvasInteropModule</c> so the waits in this
    /// class can read the engine model from a SYNCHRONOUS predicate. Playwright's <c>WaitForFunctionAsync</c>
    /// does not await its predicate: an <c>async</c> predicate returns a Promise, which is truthy, so such a
    /// wait resolved on its first poll whatever the model said (measured: <c>async () =&gt; false</c> returned
    /// after ~10 ms, <c>() =&gt; false</c> timed out) — the revision-review waits here used to be no waits.
    /// </summary>
    private static Task EnsureCanvasInteropModuleAsync(IPage page)
        => page.EvaluateAsync(
            """
            async () => {
                window.__tmDocumentCanvasInteropModule ||= await import('/_content/Tempo.Blazor.DocumentEditor/js/document-editor-canvas/interop.mjs');
            }
            """);

    /// <summary>
    /// The revision rail is refreshed from the engine by a DEBOUNCED annotation pull (200 ms after the last
    /// model change) and then scrolls the selected item into view. Clicking a marker right after tracked
    /// typing therefore opens a rail that still lists only the seeded revisions, which a moment later gains
    /// the new ones at the TOP and scrolls — the row under a pending click moves by several rows (measured:
    /// the seeded deletion's row went y=563 → 1122 → 812 within ~400 ms of the marker click). An accept
    /// clicked in that window lands on a neighbouring row, the deletion stays pending, and its markers never
    /// clear. Wait until the rail lists exactly the engine's pending revisions and has stopped moving.
    /// </summary>
    private static async Task WaitForRevisionPanelSettledAsync(IPage page)
    {
        await EnsureCanvasInteropModuleAsync(page);
        var settled = await page.EvaluateAsync<bool>(
            """
            async () => {
                const frame = () => new Promise(resolve => requestAnimationFrame(() => resolve()));
                const pendingIds = () => {
                    const host = document.querySelector('[data-testid="document-canvas-engine-host"]');
                    const handle = host?.getAttribute('data-canvas-engine-handle') || '';
                    const module = window.__tmDocumentCanvasInteropModule;
                    if (!handle || !module) return null;
                    const model = JSON.parse(module.getModelJson(handle) || '{}');
                    return (Array.isArray(model.revisions) ? model.revisions : [])
                        .filter(item => String(item?.action || item?.Action || '').toLowerCase() === 'pending')
                        .map(item => String(item?.id || item?.Id || ''))
                        .sort()
                        .join('|');
                };
                const rail = () => [...document.querySelectorAll('[data-testid="document-revision-item"]')]
                    .map(item => ({ id: item.getAttribute('data-revision-id') || '', top: item.getBoundingClientRect().top }));
                const deadline = performance.now() + 15000;
                let previous = null;
                let stableFrames = 0;
                while (performance.now() < deadline) {
                    await frame();
                    const items = rail();
                    const listed = items.map(item => item.id).sort().join('|');
                    const signature = items.map(item => `${item.id}@${item.top}`).join(',');
                    const inSync = listed.length > 0 && listed === pendingIds();
                    stableFrames = inSync && signature === previous ? stableFrames + 1 : 0;
                    if (stableFrames >= 3) {
                        return true;
                    }

                    previous = signature;
                }

                return false;
            }
            """);
        Assert.IsTrue(settled, "the revision rail never listed exactly the engine's pending revisions at rest within 15s — a review click now could land on a row that is still moving");
    }

    /// <summary>
    /// The accessibility mirror is updated after the canvas render, so right after a review it can still show
    /// the pre-review text (measured: it read "editable TCX" after Reject All while the model already held
    /// "editableX", and flipped a moment later — which the protected-typing check below then misread as the
    /// blocked keystrokes having changed the block). Wait until the mirror shows the model's text for the block.
    /// </summary>
    private static async Task WaitForMirrorToMatchModelAsync(IPage page, string blockId)
    {
        await EnsureCanvasInteropModuleAsync(page);
        await page.WaitForFunctionAsync(
            """
            blockId => {
                const host = document.querySelector('[data-testid="document-canvas-engine-host"]');
                const handle = host?.getAttribute('data-canvas-engine-handle') || '';
                const module = window.__tmDocumentCanvasInteropModule;
                if (!handle || !module) return false;
                const model = JSON.parse(module.getModelJson(handle) || '{}');
                const block = (model.body?.blocks || []).find(candidate => candidate.id === blockId);
                const modelText = (block?.content?.runs || []).map(run => run.text || '').join('');
                const mirrorText = document.querySelector(`[data-testid="document-canvas-a11y-mirror"] [data-block-id="${blockId}"]`)?.textContent;
                return modelText.length > 0 && mirrorText === modelText;
            }
            """,
            blockId,
            new PageWaitForFunctionOptions { Timeout = 10_000 });
    }

    private static Task WaitForSaveBoundaryAsync(IPage page)
        => page.WaitForFunctionAsync(
            """
            () => {
                const saveMessage = document.querySelector('[data-testid="document-save-message"]')?.textContent || '';
                const lastSaved = document.querySelector('[data-testid="document-last-saved"]')?.textContent || '';
                const dirty = document.querySelector('[data-testid="document-editor-demo"]')?.getAttribute('data-document-dirty') === 'true';
                const saveButtonDisabled = document.querySelector('[data-testid="document-save"]')?.hasAttribute('disabled') === true;
                return saveButtonDisabled === false
                    && dirty === false
                    && (/Saved|Autosaved|Uloženo|Automaticky uloženo/i.test(saveMessage) || /saved|uloženo/i.test(lastSaved));
            }
            """,
            new PageWaitForFunctionOptions { Timeout = 15_000 });

    private static Task NavigateWithinBlazorAsync(IPage page, string url)
        => page.EvaluateAsync(
            """
            url => window.Blazor?.navigateTo
                ? window.Blazor.navigateTo(url)
                : window.history.pushState({}, '', url)
            """,
            url);

    private static async Task<Phase17CommandProbe> ExecuteCanvasCommandAsync(IPage page, string commandId, object payload)
    {
        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        return await page.EvaluateAsync<Phase17CommandProbe>(
            """
            async ({ commandId, json }) => {
                const payload = JSON.parse(json || '{}');
                const host = document.querySelector('[data-testid="document-canvas-engine-host"]');
                const handle = host?.getAttribute('data-canvas-engine-handle') || '';
                const module = window.__tmDocumentCanvasInteropModule ||= await import('/_content/Tempo.Blazor.DocumentEditor/js/document-editor-canvas/interop.mjs');
                const raw = module.execCommand(handle, commandId, json);
                const parsed = JSON.parse(raw || '{}');
                const model = parsed?.result?.model || {};
                const comments = Array.isArray(model.comments) ? model.comments : [];
                const commentId = String(parsed?.result?.commentId || payload?.comment?.id || payload?.Comment?.Id || '');
                const comment = comments.find(item => String(item?.id || item?.Id || '') === commentId) || null;
                const anchor = comment?.anchor || comment?.Anchor || {};
                return {
                    changed: parsed?.result?.changed === true,
                    handled: parsed?.handled === true,
                    commentId,
                    blockId: String(anchor.blockId || anchor.BlockId || ''),
                    anchorStartOffset: Number(anchor.startOffset ?? anchor.StartOffset ?? 0) || 0,
                    anchorEndOffset: Number(anchor.endOffset ?? anchor.EndOffset ?? 0) || 0,
                    debug: JSON.stringify(parsed)
                };
            }
            """,
            new { commandId, json });
    }

    private static Task WaitForLastCanvasCommandAsync(IPage page, string commandId)
        => page.WaitForFunctionAsync(
            """
            commandId => {
                const last = document.querySelector('[data-testid="document-canvas-engine-root"]')?.getAttribute('data-canvas-command-last') || '';
                return last.toLowerCase() === commandId.toLowerCase();
            }
            """,
            commandId,
            new PageWaitForFunctionOptions { Timeout = 10_000 });

    private static async Task SelectCanvasTextRangeAsync(IPage page, string blockId, int startOffset, int endOffset)
    {
        var start = await ReadCanvasPointAsync(page, blockId, startOffset);
        var end = await ReadCanvasPointAsync(page, blockId, endOffset);
        await page.Mouse.MoveAsync((float)start.X, (float)start.Y);
        await page.Mouse.DownAsync();
        await page.Mouse.MoveAsync((float)end.X, (float)end.Y, new MouseMoveOptions { Steps = 10 });
        await page.Mouse.UpAsync();
        await page.WaitForFunctionAsync(
            """
            blockId => {
                const root = document.querySelector('[data-testid="document-canvas-engine-root"]');
                return root?.getAttribute('data-canvas-selection-collapsed') === 'false'
                    && root?.getAttribute('data-canvas-selection-anchor-block-id') === blockId
                    && root?.getAttribute('data-canvas-selection-focus-block-id') === blockId
                    && document.querySelectorAll('[data-testid="document-canvas-selection-rect"]').length >= 1;
            }
            """,
            blockId,
            new PageWaitForFunctionOptions { Timeout = 10_000 });
    }

    private static Task WaitForCanvasSelectionAsync(IPage page, string blockId, int expectedOffset)
        => page.WaitForFunctionAsync(
            """
            ([blockId, expectedOffset]) => {
                const root = document.querySelector('[data-testid="document-canvas-engine-root"]');
                const actualOffset = Number(root?.getAttribute('data-canvas-selection-focus-offset') || '-1');
                return root?.getAttribute('data-canvas-selection-collapsed') === 'true'
                    && root?.getAttribute('data-canvas-selection-focus-block-id') === blockId
                    && Math.abs(actualOffset - Number(expectedOffset)) <= 1;
            }
            """,
            new object[] { blockId, expectedOffset },
            new PageWaitForFunctionOptions { Timeout = 10_000 });

    private static async Task ClickCanvasBlockAsync(IPage page, string blockId, int offset)
    {
        // Scroll the target line clear of the sticky toolbar first. The block's screen position varies with
        // scroll/layout after preceding edits, and when it lands behind the toolbar the click hits a toolbar
        // button instead of the canvas (the caret never moves) — an intermittent failure otherwise.
        await page.EvaluateAsync(
            """
            ([blockId, offset]) => {
                const rects = Array.from(document.querySelectorAll(`[data-canvas-text-rect][data-block-id="${blockId}"]`));
                const node = rects.find(item =>
                    Number(item.getAttribute('data-canvas-start-offset') || '0') <= offset
                    && Number(item.getAttribute('data-canvas-end-offset') || '0') >= offset) || rects[0];
                node?.scrollIntoView({ block: 'center', inline: 'center', behavior: 'instant' });
            }
            """,
            new object[] { blockId, offset });
        var point = await ReadCanvasPointAsync(page, blockId, offset);
        await page.Mouse.ClickAsync((float)point.X, (float)point.Y);
        await page.WaitForFunctionAsync(
            """
            ([blockId, offset]) => {
                const root = document.querySelector('[data-testid="document-canvas-engine-root"]');
                const actualOffset = Number(root?.getAttribute('data-canvas-selection-focus-offset') || '-1');
                const expectedOffset = Number(offset);
                const inExpectedProtectionZone = expectedOffset >= 14 && expectedOffset <= 29
                    ? actualOffset >= 14 && actualOffset <= 29
                    : actualOffset >= 0 && actualOffset < 14;
                return root?.getAttribute('data-canvas-selection-focus-block-id') === blockId
                    && inExpectedProtectionZone;
            }
            """,
            new object[] { blockId, offset },
            new PageWaitForFunctionOptions { Timeout = 10_000 });
    }

    private static async Task<CanvasPoint> ReadCanvasPointAsync(IPage page, string blockId, int offset)
        => await page.EvaluateAsync<CanvasPoint>(
            """
            ([blockId, offset]) => {
                const rects = Array.from(document.querySelectorAll(`[data-canvas-text-rect][data-block-id="${blockId}"]`));
                const node = rects.find(item => Number(item.getAttribute('data-canvas-start-offset') || '0') <= offset && Number(item.getAttribute('data-canvas-end-offset') || '0') >= offset) || rects[0];
                const rect = node.getBoundingClientRect();
                const start = Number(node.getAttribute('data-canvas-start-offset') || '0');
                const end = Math.max(start + 1, Number(node.getAttribute('data-canvas-end-offset') || '0'));
                const t = Math.max(0, Math.min(1, (offset - start) / (end - start)));
                return {
                    x: rect.left + Math.max(2, rect.width * t),
                    y: rect.top + rect.height / 2
                };
            }
            """,
            new object[] { blockId, offset });

    private static Task FocusHiddenCanvasInputAsync(IPage page)
        => page.GetByTestId("document-canvas-hidden-input").FocusAsync();

    private static async Task<string> ReadMirrorTextAsync(IPage page, string blockId)
        => await page.EvaluateAsync<string>(
            """
            blockId => document.querySelector(`[data-testid="document-canvas-a11y-mirror"] [data-block-id="${blockId}"]`)?.textContent || ''
            """,
            blockId);

    private static Task WaitForMirrorTextContainsAsync(IPage page, string blockId, string expected)
        => page.WaitForFunctionAsync(
            """
            ([blockId, expected]) => {
                const block = document.querySelector(`[data-testid="document-canvas-a11y-mirror"] [data-block-id="${blockId}"]`);
                return block && block.textContent.includes(expected);
            }
            """,
            new object[] { blockId, expected },
            new PageWaitForFunctionOptions { Timeout = 10_000 });

    private static Task<Phase17Probe> ReadPhase17ProbeAsync(IPage page)
        => page.EvaluateAsync<Phase17Probe>(
            """
            async () => {
                const host = document.querySelector('[data-testid="document-canvas-engine-host"]');
                const handle = host?.getAttribute('data-canvas-engine-handle') || '';
                const module = window.__tmDocumentCanvasInteropModule ||= await import('/_content/Tempo.Blazor.DocumentEditor/js/document-editor-canvas/interop.mjs');
                const model = handle ? JSON.parse(module.getModelJson(handle) || '{}') : {};
                const comments = Array.isArray(model.comments) ? model.comments : [];
                const revisions = Array.isArray(model.revisions) ? model.revisions : [];
                const protectedBlock = (model.body?.blocks || []).find(block => block.id === 'canvas-phase17-protected');
                const protectedText = (protectedBlock?.content?.runs || []).map(run => run.text || '').join('');
                const normalize = value => String(value || '').toLowerCase();
                return {
                    commentMarkerCount: document.querySelectorAll('[data-testid="document-canvas-comment-marker"]').length,
                    revisionMarkerCount: document.querySelectorAll('[data-testid="document-canvas-revision-marker"]').length,
                    protectedText: document.querySelector('[data-testid="document-canvas-a11y-mirror"] [data-block-id="canvas-phase17-protected"]')?.textContent || protectedText,
                    modelProtectedText: protectedText,
                    commentOverlayStatus: document.querySelector('[data-testid="document-canvas-comment-marker"][data-comment-id="canvas-phase17-comment"]')?.getAttribute('data-canvas-comment-status') || '',
                    commentCount: comments.length,
                    commentReplyText: comments.flatMap(comment => comment.entries || []).map(entry => entry.text || '').join('\n'),
                    pendingRevisionCount: revisions.filter(revision => normalize(revision.action) === 'pending').length,
                    acceptedRevisionCount: revisions.filter(revision => normalize(revision.action) === 'accepted').length,
                    rejectedRevisionCount: revisions.filter(revision => normalize(revision.action) === 'rejected').length,
                    trackedInsertionCount: revisions.filter(revision => normalize(revision.type) === 'insertion').length,
                    trackedDeletionCount: revisions.filter(revision => normalize(revision.type) === 'deletion').length,
                    trackedFormattingCount: revisions.filter(revision => normalize(revision.type) === 'formatting').length,
                    restrictedMarkerCount: Array.isArray(model.restrictedMarkers) ? model.restrictedMarkers.length : 0,
                    isProtected: model.isProtected === true
                };
            }
            """);

    private static string CreateOutputDirectory(string testName)
    {
        var path = Path.Combine(
            FindRepositoryRoot().FullName,
            "tests",
            "Tempo.Blazor.E2E",
            "TestResults",
            "document-editor-canvas",
            "phase17-comments-revisions",
            "2026-06-04",
            testName,
            DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmssfff"));
        Directory.CreateDirectory(path);
        return path;
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

    private sealed class Phase17CommandProbe
    {
        public bool Changed { get; set; }

        public bool Handled { get; set; }

        public string CommentId { get; set; } = string.Empty;

        public string BlockId { get; set; } = string.Empty;

        public int AnchorStartOffset { get; set; }

        public int AnchorEndOffset { get; set; }

        public string Debug { get; set; } = string.Empty;
    }

    private sealed class CanvasPoint
    {
        public double X { get; set; }

        public double Y { get; set; }
    }

    private sealed class Phase17Probe
    {
        public int CommentMarkerCount { get; set; }

        public int RevisionMarkerCount { get; set; }

        public string ProtectedText { get; set; } = string.Empty;

        public string ModelProtectedText { get; set; } = string.Empty;

        public string CommentOverlayStatus { get; set; } = string.Empty;

        public int CommentCount { get; set; }

        public string CommentReplyText { get; set; } = string.Empty;

        public int PendingRevisionCount { get; set; }

        public int AcceptedRevisionCount { get; set; }

        public int RejectedRevisionCount { get; set; }

        public int TrackedInsertionCount { get; set; }

        public int TrackedDeletionCount { get; set; }

        public int TrackedFormattingCount { get; set; }

        public int RestrictedMarkerCount { get; set; }

        public bool IsProtected { get; set; }
    }
}
