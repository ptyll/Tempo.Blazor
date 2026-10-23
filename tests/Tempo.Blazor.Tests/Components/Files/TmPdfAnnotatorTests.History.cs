using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.Abstractions.Models;
using Tempo.Blazor.Components.Files;
using Tempo.Blazor.Tests.Localization;
using Xunit;

namespace Tempo.Blazor.Tests.Components.Files;

/// <summary>
/// FZE-15: undo/redo on <see cref="TmPdfAnnotator"/> — the public API, the keyboard shortcuts
/// (Ctrl/⌘+Z, Ctrl/⌘+Shift+Z, Ctrl+Y), the Draw-draft rule, the toolbar history group, the
/// mobile "More" entries, AllowUndo=false and the Continuous anchor scroll.
/// </summary>
public partial class TmPdfAnnotatorTests
{
    // ── Create undo/redo through the component ───────────────────────────────

    [Fact]
    public async Task Undo_Create_DeletesThread_Redo_RecreatesWithRemappedId()
    {
        var provider = new InMemoryPdfAnnotationProvider();
        var cut = Render(provider);
        cut.Find("[data-testid='pdf-annotator-mode-stamp']").Click();
        await cut.Find("[data-testid='pdf-annotator-surface']")
            .TriggerEventAsync("onclick", new MouseEventArgs { OffsetX = 400, OffsetY = 100 });
        var original = (await provider.GetThreadsAsync(DocId)).Single();

        await cut.InvokeAsync(() => cut.Instance.UndoAsync());
        (await provider.GetThreadsAsync(DocId)).Should().BeEmpty();
        cut.Instance.CanUndo.Should().BeFalse();
        cut.Instance.CanRedo.Should().BeTrue();

        await cut.InvokeAsync(() => cut.Instance.RedoAsync());
        var restored = (await provider.GetThreadsAsync(DocId)).Single();
        restored.Id.Should().NotBe(original.Id, "the re-created thread gets a fresh provider id");
        restored.Kind.Should().Be(DocumentAnnotationKind.Stamp);
        restored.StampText.Should().Be(original.StampText);
    }

    [Fact]
    public async Task History_ClearedOnUrlChange()
    {
        var provider = new InMemoryPdfAnnotationProvider();
        var cut = Render(provider);
        cut.Find("[data-testid='pdf-annotator-mode-stamp']").Click();
        await cut.Find("[data-testid='pdf-annotator-surface']")
            .TriggerEventAsync("onclick", new MouseEventArgs { OffsetX = 400, OffsetY = 100 });
        cut.Instance.CanUndo.Should().BeTrue();

        await cut.InvokeAsync(() => cut.Instance.SetParametersAsync(ParameterView.FromDictionary(
            new Dictionary<string, object?> { [nameof(TmPdfAnnotator.DocumentId)] = "doc-2" })));

        cut.Instance.CanUndo.Should().BeFalse("a new document invalidates the whole stack");
        cut.Instance.CanRedo.Should().BeFalse();
    }

    [Fact]
    public async Task OnHistoryChanged_RaisedOnEveryStackChange()
    {
        var provider = new InMemoryPdfAnnotationProvider();
        var changes = 0;
        var cut = Render(provider, p => p.Add(
            x => x.OnHistoryChanged, EventCallback.Factory.Create(this, () => changes++)));

        cut.Find("[data-testid='pdf-annotator-mode-stamp']").Click();
        await cut.Find("[data-testid='pdf-annotator-surface']")
            .TriggerEventAsync("onclick", new MouseEventArgs { OffsetX = 400, OffsetY = 100 });
        changes.Should().BeGreaterThan(0, "the executed step raises the callback");

        changes = 0;
        await cut.InvokeAsync(() => cut.Instance.UndoAsync());
        changes.Should().BeGreaterThan(0, "the undo raises the callback");
    }

    // ── Keyboard shortcuts (JS-driven) ───────────────────────────────────────

    // FZE-15 review: the shortcut FILTERING (exactly one of Ctrl/⌘, z/y, !repeat, no editable
    // focus, preventDefault) lives in pdf-annotator.js — ONE keydown listener per annotator
    // root — and is covered by the node suite (pdf-annotator-shortcuts.test.mjs). bUnit drives
    // the [JSInvokable] bridge directly.

    private Task Shortcut(IRenderedComponent<TmPdfAnnotator> cut, bool redo)
        => cut.InvokeAsync(() => cut.Instance.OnHistoryShortcut(redo));

    /// <summary>Places a stamp through the active surface and returns the newest thread
    /// (a cut may already hold earlier ones).</summary>
    private async Task<DocumentCommentThread> CreateStampThreadAsync(IRenderedComponent<TmPdfAnnotator> cut, InMemoryPdfAnnotationProvider provider)
    {
        cut.Find("[data-testid='pdf-annotator-mode-stamp']").Click();
        await cut.Find("[data-testid='pdf-annotator-surface']")
            .TriggerEventAsync("onclick", new MouseEventArgs { OffsetX = 400, OffsetY = 100 });
        await cut.WaitForAssertionAsync(() => { });
        var threads = await provider.GetThreadsAsync(DocId);
        threads.Should().NotBeEmpty();
        return threads[^1];
    }

    [Fact]
    public async Task Shortcut_CtrlZ_Undo_CtrlShiftZ_And_CtrlY_Redo()
    {
        var provider = new InMemoryPdfAnnotationProvider();
        var cut = Render(provider);
        await CreateStampThreadAsync(cut, provider);

        await Shortcut(cut, redo: false);
        (await provider.GetThreadsAsync(DocId)).Should().BeEmpty("Ctrl+Z undoes");

        await Shortcut(cut, redo: true);
        (await provider.GetThreadsAsync(DocId)).Should().ContainSingle("Ctrl+Shift+Z redoes");

        await Shortcut(cut, redo: false);
        (await provider.GetThreadsAsync(DocId)).Should().BeEmpty();

        await Shortcut(cut, redo: true);
        (await provider.GetThreadsAsync(DocId)).Should().ContainSingle("Ctrl+Y redoes");
    }

    [Fact]
    public async Task DraftDraw_Shortcut_RemovesLastStroke_NotHistory()
    {
        var provider = new InMemoryPdfAnnotationProvider();
        var cut = Render(provider);
        cut.Find("[data-testid='pdf-annotator-mode-draw']").Click();
        var surface = cut.Find("[data-testid='pdf-annotator-surface']");

        for (var stroke = 0; stroke < 2; stroke++)
        {
            await surface.TriggerEventAsync("onpointerdown", new PointerEventArgs { OffsetX = 80, OffsetY = 100 });
            await surface.TriggerEventAsync("onpointermove", new PointerEventArgs { OffsetX = 160, OffsetY = 200 });
            await surface.TriggerEventAsync("onpointerup", new PointerEventArgs { OffsetX = 160, OffsetY = 200 });
        }

        await Shortcut(cut, redo: false);

        cut.Instance.CanUndo.Should().BeFalse("drafts never enter the command stack");
        cut.Find("[data-testid='pdf-annotator-new-submit']").Click();

        var threads = await provider.GetThreadsAsync(DocId);
        threads.Should().HaveCount(1);
        threads[0].InkStrokes.Should().HaveCount(1, "Ctrl+Z removed the draft's last stroke");
    }

    // ── Toolbar history group ────────────────────────────────────────────────

    [Fact]
    public async Task HistoryGroup_ButtonsDisabledWhenNothingToUndo()
    {
        var provider = new InMemoryPdfAnnotationProvider();
        var cut = Render(provider);

        var undo = cut.Find("[data-testid='pdf-annotator-undo']");
        var redo = cut.Find("[data-testid='pdf-annotator-redo']");
        undo.HasAttribute("disabled").Should().BeTrue();
        undo.GetAttribute("aria-disabled").Should().Be("true");
        redo.HasAttribute("disabled").Should().BeTrue();
        redo.GetAttribute("aria-disabled").Should().Be("true");

        cut.Find("[data-testid='pdf-annotator-mode-stamp']").Click();
        await cut.Find("[data-testid='pdf-annotator-surface']")
            .TriggerEventAsync("onclick", new MouseEventArgs { OffsetX = 400, OffsetY = 100 });

        cut.Find("[data-testid='pdf-annotator-undo']").HasAttribute("disabled").Should().BeFalse();
        cut.Find("[data-testid='pdf-annotator-redo']").HasAttribute("disabled").Should().BeTrue();
    }

    [Fact]
    public async Task HistoryGroup_AriaLabelsNameTheAction()
    {
        var provider = new InMemoryPdfAnnotationProvider();
        var cut = Render(provider);

        cut.Find("[data-testid='pdf-annotator-undo']")
            .GetAttribute("title").Should().Be("Nothing to undo");

        cut.Find("[data-testid='pdf-annotator-mode-stamp']").Click();
        await cut.Find("[data-testid='pdf-annotator-surface']")
            .TriggerEventAsync("onclick", new MouseEventArgs { OffsetX = 400, OffsetY = 100 });
        cut.Render();

        cut.Find("[data-testid='pdf-annotator-undo']")
            .GetAttribute("title").Should().Be("Undo: Place stamp");

        // The phase example: after a DELETE the undo names the delete action.
        var seeded = Seeded(Highlight("h1"));
        var deleteCut = Render(seeded);
        deleteCut.WaitForElement("[data-testid='pdf-annotator-thread']").Click();
        deleteCut.WaitForElement(".tm-pdf-annotator-panel .tm-pdf-comment-item__menu .tm-dropdown-trigger").Click();
        deleteCut.WaitForElement("[data-testid='pdf-annotator-delete']").Click();

        deleteCut.WaitForAssertion(() =>
            deleteCut.Find("[data-testid='pdf-annotator-undo']")
                .GetAttribute("title").Should().Be("Undo: Delete comment"));
    }

    [Fact]
    public void HistoryGroup_CarriesTheFrozenDataTmGroupName()
    {
        var cut = Render();
        var group = cut.Find("[data-testid='pdf-annotator-history']");
        group.GetAttribute("data-tm-group").Should().Be("history");
        group.GetAttribute("aria-label").Should().Be("History");
        cut.Find("[data-testid='pdf-annotator-undo']").GetAttribute("aria-keyshortcuts").Should().Be("Control+Z");
        cut.Find("[data-testid='pdf-annotator-redo']").GetAttribute("aria-keyshortcuts").Should().Be("Control+Shift+Z Control+Y");
    }

    [Fact]
    public async Task UndoRedo_Continuous_ScrollsToAffectedAnchor()
    {
        var provider = new InMemoryPdfAnnotationProvider();
        var cut = Render(provider, p => p.Add(x => x.ViewMode, PdfViewMode.Continuous));
        MountContinuousPages(cut);

        cut.Find("[data-testid='pdf-annotator-mode-stamp']").Click();
        // Stamp on page 3: the per-page slot surface normalizes against its own page.
        await cut.Find(SurfaceSelector(3))
            .TriggerEventAsync("onclick", new MouseEventArgs { OffsetX = 200, OffsetY = 100 });
        await cut.WaitForAssertionAsync(async () =>
            (await provider.GetThreadsAsync(DocId)).Should().ContainSingle());

        // Undoing a create removes the thread (nothing to scroll to); the REDO re-creates
        // it — the affected thread is selected and the Continuous column scrolls to its anchor.
        await cut.InvokeAsync(() => cut.Instance.UndoAsync());
        await cut.InvokeAsync(() => cut.Instance.RedoAsync());
        cut.Render();

        cut.WaitForAssertion(() =>
        {
            var scroll = JSInterop.Invocations.Where(i => i.Identifier == "tmPdfViewer.scrollToAnchor").ToList();
            scroll.Should().NotBeEmpty("the undo selection scrolls the Continuous column to the anchor");
            scroll[^1].Arguments[1].Should().Be(3);
        });
    }

    // ── AllowUndo=false and mobile ───────────────────────────────────────────

    [Fact]
    public async Task AllowUndoFalse_HidesGroupAndShortcuts()
    {
        var provider = new InMemoryPdfAnnotationProvider();
        var cut = Render(provider, p => p.Add(x => x.AllowUndo, false));

        cut.FindAll("[data-testid='pdf-annotator-history']").Should().BeEmpty();
        cut.Instance.CanUndo.Should().BeFalse();
        cut.Instance.CanRedo.Should().BeFalse();

        cut.Find("[data-testid='pdf-annotator-mode-stamp']").Click();
        await cut.Find("[data-testid='pdf-annotator-surface']")
            .TriggerEventAsync("onclick", new MouseEventArgs { OffsetX = 400, OffsetY = 100 });
        (await provider.GetThreadsAsync(DocId)).Should().ContainSingle();

        await Shortcut(cut, redo: false);
        (await provider.GetThreadsAsync(DocId)).Should().ContainSingle("the shortcut is hidden with AllowUndo=false");
        cut.Instance.CanUndo.Should().BeFalse("mutations bypass the stack entirely");
    }

    [Fact]
    public void Mobile_UndoRedoInMoreMenu()
    {
        var cut = RenderInLayout(TmLayoutMode.Mobile);

        cut.Find(".tm-mobile-action-bar__more").Click();
        cut.FindAll(".tm-overlay-panel-sheet [data-action-id='undo']")
            .Should().NotBeEmpty("undo rides the shared More menu on a phone");
        cut.FindAll(".tm-overlay-panel-sheet [data-action-id='redo']")
            .Should().NotBeEmpty("redo rides the shared More menu on a phone");
    }

    // ── FZE-15 review: sibling group, stack-clear rules, failure + chip ──────

    [Fact]
    public void HistoryGroup_IsSiblingOfTools_NotNestedInside()
    {
        var cut = Render();

        var tools = cut.Find("[data-tm-group='tools']");
        tools.QuerySelectorAll("[data-testid='pdf-annotator-undo']").Should().BeEmpty(
            "[data-tm-group='tools'] contains no undo button");
        tools.QuerySelectorAll("[data-testid='pdf-annotator-redo']").Should().BeEmpty(
            "[data-tm-group='tools'] contains no redo button");

        // The history group is the adjacent SIBLING rendered before the tools.
        var history = cut.Find("[data-tm-group='history']");
        history.QuerySelector("[data-testid='pdf-annotator-undo']").Should().NotBeNull();
        history.NextElementSibling!.GetAttribute("data-tm-group").Should().Be("tools",
            "the history group renders immediately before the tools group");
    }

    [Fact]
    public async Task History_ClearedOnUrlChange_WithFixedDocumentId()
    {
        var provider = new InMemoryPdfAnnotationProvider();
        var cut = Render(provider);
        await CreateStampThreadAsync(cut, provider);
        cut.Instance.CanUndo.Should().BeTrue();

        // The DocumentId stays fixed; only the Url rotates (e.g. a re-signed link).
        await cut.InvokeAsync(() => cut.Instance.SetParametersAsync(ParameterView.FromDictionary(
            new Dictionary<string, object?> { [nameof(TmPdfAnnotator.Url)] = "/rotated.pdf" })));

        cut.Instance.CanUndo.Should().BeFalse("a Url change invalidates the stack even with a fixed DocumentId");
        cut.Instance.CanRedo.Should().BeFalse();
    }

    [Fact]
    public async Task History_ClearedWhenAllowUndoTurnsOff()
    {
        var provider = new InMemoryPdfAnnotationProvider();
        var cut = Render(provider);
        await CreateStampThreadAsync(cut, provider);
        cut.Instance.CanUndo.Should().BeTrue();

        await cut.InvokeAsync(() => cut.Instance.SetParametersAsync(ParameterView.FromDictionary(
            new Dictionary<string, object?> { [nameof(TmPdfAnnotator.AllowUndo)] = false })));

        cut.Instance.CanUndo.Should().BeFalse();
        cut.Instance.CanRedo.Should().BeFalse();

        // Re-enabling must not resurrect the stale steps recorded before the switch.
        await cut.InvokeAsync(() => cut.Instance.SetParametersAsync(ParameterView.FromDictionary(
            new Dictionary<string, object?> { [nameof(TmPdfAnnotator.AllowUndo)] = true })));
        cut.Instance.CanUndo.Should().BeFalse("the steps were dropped when AllowUndo turned off");
    }

    [Fact]
    public async Task Undo_ProviderFailure_AnnouncesUndoFailed_AndKeepsStack()
    {
        var provider = new GetThreadsFailingProvider();
        var cut = Render(provider);
        cut.Find("[data-testid='pdf-annotator-mode-stamp']").Click();
        await cut.Find("[data-testid='pdf-annotator-surface']")
            .TriggerEventAsync("onclick", new MouseEventArgs { OffsetX = 400, OffsetY = 100 });
        cut.Instance.CanUndo.Should().BeTrue();

        await cut.InvokeAsync(() => cut.Instance.UndoAsync());

        cut.Find("[data-testid='pdf-annotator-announcer']").TextContent.Should().Be("Undo failed",
            "the failure is announced through the live region");
        cut.Find("[data-testid='pdf-annotator-status']").TextContent.Should().Contain("Undo failed",
            "the failure also surfaces as the visible status chip");
        cut.Instance.CanUndo.Should().BeTrue("a failed undo leaves the stack unchanged");
    }

    [Fact]
    public async Task Conflict_ShowsVisibleChip_AndDismisses()
    {
        var provider = new InMemoryPdfAnnotationProvider();
        var cut = Render(provider);
        var thread = await CreateStampThreadAsync(cut, provider);
        // Another user deletes the thread behind the history's back.
        await provider.DeleteAsync(DocId, new DocumentCommentDeleteRequest { ThreadId = thread.Id, CommentId = thread.Comments[0].Id });

        await cut.InvokeAsync(() => cut.Instance.UndoAsync());

        var chip = cut.Find("[data-testid='pdf-annotator-status']");
        chip.TextContent.Should().Contain("changed in the meantime", "the conflict hint is visible, not only announced");
        cut.Find("[data-testid='pdf-annotator-announcer']").TextContent.Should().Contain("changed in the meantime");

        await cut.Find("[data-testid='pdf-annotator-status-dismiss']").ClickAsync();
        cut.FindAll("[data-testid='pdf-annotator-status']").Should().BeEmpty("the chip is dismissible");
    }

    [Fact]
    public async Task Conflict_RepeatedIdenticalAnnouncement_ReRendered()
    {
        var provider = new InMemoryPdfAnnotationProvider();
        var cut = Render(provider);
        var first = await CreateStampThreadAsync(cut, provider);
        var second = await CreateStampThreadAsync(cut, provider);
        // Both threads die behind the history's back: two consecutive SKIPPED steps announce
        // the IDENTICAL conflict text — the live region must be cleared and re-set, or the
        // second announcement is swallowed by the unchanged-DOM rule.
        await provider.DeleteAsync(DocId, new DocumentCommentDeleteRequest { ThreadId = first.Id, CommentId = first.Comments[0].Id });
        await provider.DeleteAsync(DocId, new DocumentCommentDeleteRequest { ThreadId = second.Id, CommentId = second.Comments[0].Id });

        var before = cut.RenderCount;
        await cut.InvokeAsync(() => cut.Instance.UndoAsync());
        var afterFirst = cut.RenderCount - before;

        before = cut.RenderCount;
        await cut.InvokeAsync(() => cut.Instance.UndoAsync());
        var afterSecond = cut.RenderCount - before;

        (afterSecond - afterFirst).Should().BeGreaterThanOrEqualTo(1,
            "the identical announcement clears the region, renders it empty and sets it again");
        cut.Find("[data-testid='pdf-annotator-announcer']").TextContent.Should().Contain("changed in the meantime");
    }

    [Fact]
    public async Task UndoRedo_DisabledWhileHistoryBusy()
    {
        var inner = new InMemoryPdfAnnotationProvider();
        var provider = new GatedGetThreadsProvider(inner);
        var cut = Render(provider);
        cut.Find("[data-testid='pdf-annotator-mode-stamp']").Click();
        await cut.Find("[data-testid='pdf-annotator-surface']")
            .TriggerEventAsync("onclick", new MouseEventArgs { OffsetX = 400, OffsetY = 100 });
        cut.Instance.CanUndo.Should().BeTrue();

        provider.GateNext = true; // the undo's thread fetch parks mid-flight
        var undo = Task.Run(() => cut.Instance.UndoAsync());
        await provider.CallEntered.Task; // the undo is parked inside the provider now
        cut.Instance.CanUndo.Should().BeFalse("the history reports IsBusy while the step runs");
        cut.Instance.CanRedo.Should().BeFalse();

        cut.Render(); // a render DURING the operation shows the disabled state
        cut.Find("[data-testid='pdf-annotator-undo']").HasAttribute("disabled").Should().BeTrue();

        provider.Release();
        await undo;
        cut.Instance.CanUndo.Should().BeFalse("the undo completed and the stack is empty");
        cut.Instance.CanRedo.Should().BeTrue();
    }

    private async Task<DocumentCommentThread> CreateAsync(InMemoryPdfAnnotationProvider provider)
    {
        var cut = Render(provider);
        cut.Find("[data-testid='pdf-annotator-mode-stamp']").Click();
        await cut.Find("[data-testid='pdf-annotator-surface']")
            .TriggerEventAsync("onclick", new MouseEventArgs { OffsetX = 400, OffsetY = 100 });
        var threads = await provider.GetThreadsAsync(DocId);
        return threads.Single();
    }

    /// <summary>A provider whose <c>GetThreadsAsync</c> can be armed to park once — lets a
    /// test hold a history step mid-flight (the undo path fetches the threads first).</summary>
    private sealed class GatedGetThreadsProvider : IPdfAnnotationProvider
    {
        private readonly InMemoryPdfAnnotationProvider _inner;

        public GatedGetThreadsProvider(InMemoryPdfAnnotationProvider inner) => _inner = inner;

        /// <summary>Arm before the step that must park; the NEXT GetThreadsAsync parks.</summary>
        public volatile bool GateNext;

        /// <summary>Completed when the gated call has been entered.</summary>
        public TaskCompletionSource CallEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        private TaskCompletionSource? _release;

        public void Release()
        {
            _release ??= new(TaskCreationOptions.RunContinuationsAsynchronously);
            _release.TrySetResult();
        }

        public async Task<IReadOnlyList<DocumentCommentThread>> GetThreadsAsync(string documentId, CancellationToken cancellationToken = default)
        {
            if (GateNext)
            {
                GateNext = false;
                CallEntered.TrySetResult();
                var release = _release ??= new(TaskCreationOptions.RunContinuationsAsynchronously);
                await release.Task;
            }

            return await _inner.GetThreadsAsync(documentId, cancellationToken);
        }

        public Task<DocumentCommentThread> CreateThreadAsync(string documentId, DocumentCommentThreadCreateRequest request, DocumentCommentUser author, CancellationToken cancellationToken = default)
            => _inner.CreateThreadAsync(documentId, request, author, cancellationToken);

        public Task<DocumentCommentThread> ReplyAsync(string documentId, DocumentCommentReplyRequest request, DocumentCommentUser author, CancellationToken cancellationToken = default)
            => _inner.ReplyAsync(documentId, request, author, cancellationToken);

        public Task<DocumentCommentThread> EditAsync(string documentId, DocumentCommentEditRequest request, CancellationToken cancellationToken = default)
            => _inner.EditAsync(documentId, request, cancellationToken);

        public Task DeleteAsync(string documentId, DocumentCommentDeleteRequest request, CancellationToken cancellationToken = default)
            => _inner.DeleteAsync(documentId, request, cancellationToken);

        public Task<DocumentCommentThread> ResolveAsync(string documentId, string threadId, DocumentCommentUser? resolvedBy = null, CancellationToken cancellationToken = default)
            => _inner.ResolveAsync(documentId, threadId, resolvedBy, cancellationToken);

        public Task<DocumentCommentThread> ReopenAsync(string documentId, string threadId, DocumentCommentUser? reopenedBy = null, CancellationToken cancellationToken = default)
            => _inner.ReopenAsync(documentId, threadId, reopenedBy, cancellationToken);
    }

    /// <summary>A provider whose GetThreadsAsync always throws (the undo path's first call) —
    /// a deterministic provider failure for the "Undo failed" announcement.</summary>
    private sealed class GetThreadsFailingProvider : IPdfAnnotationProvider
    {
        public Task<IReadOnlyList<DocumentCommentThread>> GetThreadsAsync(string documentId, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("get threads failed");

        public Task<DocumentCommentThread> CreateThreadAsync(string documentId, DocumentCommentThreadCreateRequest request, DocumentCommentUser author, CancellationToken cancellationToken = default)
            => Task.FromResult(new DocumentCommentThread
            {
                Id = Guid.NewGuid().ToString("N"),
                Anchor = request.Anchor,
                Kind = request.Kind,
                Color = request.Color,
                StampText = request.StampText,
                InkStrokes = request.InkStrokes,
                Comments = [new DocumentComment { Id = Guid.NewGuid().ToString("N"), Body = request.Body }]
            });

        public Task<DocumentCommentThread> ReplyAsync(string documentId, DocumentCommentReplyRequest request, DocumentCommentUser author, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("reply failed");

        public Task<DocumentCommentThread> EditAsync(string documentId, DocumentCommentEditRequest request, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("edit failed");

        public Task DeleteAsync(string documentId, DocumentCommentDeleteRequest request, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<DocumentCommentThread> ResolveAsync(string documentId, string threadId, DocumentCommentUser? resolvedBy = null, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("resolve failed");

        public Task<DocumentCommentThread> ReopenAsync(string documentId, string threadId, DocumentCommentUser? reopenedBy = null, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("reopen failed");
    }
}
