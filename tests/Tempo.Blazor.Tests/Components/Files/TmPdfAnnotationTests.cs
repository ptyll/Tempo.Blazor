using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Tempo.Blazor.Abstractions.Models;
using Tempo.Blazor.Components.Files;
using Tempo.Blazor.Tests.Localization;
using Xunit;

namespace Tempo.Blazor.Tests.Components.Files;

/// <summary>bUnit tests for the PDF annotation layer, thread panel, and viewer integration.</summary>
public class TmPdfAnnotationTests : LocalizationTestBase
{
    public TmPdfAnnotationTests()
    {
        JSInterop.Setup<bool>("tmPdfViewer.isAvailable").SetResult(true);
    }

    private static DocumentCommentThread TextRangeThread(string id, int page = 1, string body = "note",
        DocumentCommentThreadStatus status = DocumentCommentThreadStatus.Open)
        => new()
        {
            Id = id,
            Status = status,
            Anchor = DocumentCommentAnchor.TextRange(page, [DocumentCommentRect.Create(0.1, 0.2, 0.3, 0.02)], "highlighted"),
            Comments = [new DocumentComment { Id = id + "-c", AuthorId = "u1", AuthorName = "U1", Body = body }]
        };

    // ── Annotation layer ──────────────────────────────────────────────────────

    [Fact]
    public void Layer_RendersMarkerAndHighlightForTextRangeThread()
    {
        var cut = Render<TmPdfAnnotationLayer>(p => p
            .Add(x => x.Threads, new List<DocumentCommentThread> { TextRangeThread("t1") })
            .Add(x => x.PageNumber, 1));

        cut.FindAll("[data-testid='pdf-annotation-marker']").Should().HaveCount(1);
        cut.FindAll(".tm-pdf-annotation-layer__highlight").Should().HaveCount(1);
    }

    [Fact]
    public void Layer_HidesThreadsFromOtherPages()
    {
        var cut = Render<TmPdfAnnotationLayer>(p => p
            .Add(x => x.Threads, new List<DocumentCommentThread> { TextRangeThread("t1", page: 2) })
            .Add(x => x.PageNumber, 1));

        cut.FindAll("[data-testid='pdf-annotation-marker']").Should().BeEmpty();
    }

    [Fact]
    public void Layer_HidesResolvedThreadsUnlessShowResolved()
    {
        var resolved = TextRangeThread("t1", status: DocumentCommentThreadStatus.Resolved);

        var hidden = Render<TmPdfAnnotationLayer>(p => p
            .Add(x => x.Threads, new List<DocumentCommentThread> { resolved })
            .Add(x => x.PageNumber, 1)
            .Add(x => x.ShowResolved, false));
        hidden.FindAll("[data-testid='pdf-annotation-marker']").Should().BeEmpty();

        var shown = Render<TmPdfAnnotationLayer>(p => p
            .Add(x => x.Threads, new List<DocumentCommentThread> { resolved })
            .Add(x => x.PageNumber, 1)
            .Add(x => x.ShowResolved, true));
        shown.FindAll("[data-testid='pdf-annotation-marker']").Should().HaveCount(1);
    }

    [Fact]
    public void Layer_ClickMarker_RaisesOnThreadSelected()
    {
        string? selected = null;
        var cut = Render<TmPdfAnnotationLayer>(p => p
            .Add(x => x.Threads, new List<DocumentCommentThread> { TextRangeThread("t1") })
            .Add(x => x.PageNumber, 1)
            .Add(x => x.OnThreadSelected, EventCallback.Factory.Create<string>(this, id => selected = id)));

        cut.Find("[data-testid='pdf-annotation-marker']").Click();

        selected.Should().Be("t1");
    }

    [Fact]
    public void Layer_RendersDraftHighlightForPendingAnchor()
    {
        var draft = DocumentCommentAnchor.TextRange(1, [DocumentCommentRect.Create(0.2, 0.3, 0.2, 0.02)], "draft");
        var cut = Render<TmPdfAnnotationLayer>(p => p
            .Add(x => x.Threads, new List<DocumentCommentThread>())
            .Add(x => x.PageNumber, 1)
            .Add(x => x.DraftAnchor, draft));

        cut.FindAll("[data-testid='pdf-annotation-draft']").Should().HaveCount(1);
    }

    // ── Thread panel ──────────────────────────────────────────────────────────

    [Fact]
    public void Panel_EmptyThreads_ShowsEmptyState()
    {
        var cut = Render<TmPdfAnnotationThreadPanel>(p => p
            .Add(x => x.Threads, new List<DocumentCommentThread>()));

        cut.Find("[data-testid='pdf-annotation-empty']").Should().NotBeNull();
    }

    [Fact]
    public void Panel_SelectingThread_ShowsCommentDetail()
    {
        var thread = TextRangeThread("t1", body: "the body");
        var cut = Render<TmPdfAnnotationThreadPanel>(p => p
            .Add(x => x.Threads, new List<DocumentCommentThread> { thread })
            .Add(x => x.SelectedThreadId, "t1"));

        cut.Find("[data-testid='pdf-annotation-detail']").Should().NotBeNull();
        cut.Find("[data-testid='pdf-annotation-comment']").TextContent.Should().Contain("the body");
    }

    [Fact]
    public void Panel_PendingSelection_ShowsNewComposerAndCreates()
    {
        string? createdBody = null;
        var selection = new PdfTextSelection("chosen text", 1, [DocumentCommentRect.Create(0.1, 0.2, 0.3, 0.02)]);
        var cut = Render<TmPdfAnnotationThreadPanel>(p => p
            .Add(x => x.Threads, new List<DocumentCommentThread>())
            .Add(x => x.PendingSelection, selection)
            .Add(x => x.OnCreateThreadRequested, EventCallback.Factory.Create<string>(this, b => createdBody = b)));

        cut.Find("[data-testid='pdf-annotation-new']").Should().NotBeNull();
        cut.Find("[data-testid='pdf-annotation-new-input']").Input("My comment");
        cut.Find("[data-testid='pdf-annotation-new-submit']").Click();

        createdBody.Should().Be("My comment");
    }

    [Fact]
    public void Panel_Reply_RaisesReplyRequest()
    {
        DocumentCommentReplyRequest? reply = null;
        var thread = TextRangeThread("t1");
        var cut = Render<TmPdfAnnotationThreadPanel>(p => p
            .Add(x => x.Threads, new List<DocumentCommentThread> { thread })
            .Add(x => x.SelectedThreadId, "t1")
            .Add(x => x.OnReplyRequested, EventCallback.Factory.Create<DocumentCommentReplyRequest>(this, r => reply = r)));

        cut.Find("[data-testid='pdf-annotation-reply-input']").Input("A reply");
        cut.Find("[data-testid='pdf-annotation-reply-submit']").Click();

        reply.Should().NotBeNull();
        reply!.ThreadId.Should().Be("t1");
        reply.Body.Should().Be("A reply");
    }

    [Fact]
    public void Panel_Resolve_RaisesResolveRequest()
    {
        string? resolvedId = null;
        var thread = TextRangeThread("t1");
        var cut = Render<TmPdfAnnotationThreadPanel>(p => p
            .Add(x => x.Threads, new List<DocumentCommentThread> { thread })
            .Add(x => x.SelectedThreadId, "t1")
            .Add(x => x.OnResolveRequested, EventCallback.Factory.Create<string>(this, id => resolvedId = id)));

        cut.Find("[data-testid='pdf-annotation-resolve']").Click();

        resolvedId.Should().Be("t1");
    }

    // ── Wave 0 — a11y ─────────────────────────────────────────────────────────

    [Fact]
    public void ThreadHeader_HasAriaExpandedAndControls()
    {
        var thread = TextRangeThread("t1");
        var cut = Render<TmPdfAnnotationThreadPanel>(p => p
            .Add(x => x.Threads, new List<DocumentCommentThread> { thread })
            .Add(x => x.SelectedThreadId, "t1"));

        var header = cut.Find("[data-testid='pdf-annotation-thread']");
        header.GetAttribute("aria-expanded").Should().Be("true");
        var controls = header.GetAttribute("aria-controls");
        controls.Should().NotBeNullOrEmpty("an expanded thread header must reference its detail via aria-controls");
        cut.Find($"#{controls}").Should().NotBeNull();

        var collapsed = Render<TmPdfAnnotationThreadPanel>(p => p
            .Add(x => x.Threads, new List<DocumentCommentThread> { thread })
            .Add(x => x.SelectedThreadId, (string?)null));
        collapsed.Find("[data-testid='pdf-annotation-thread']")
            .GetAttribute("aria-expanded").Should().Be("false");
        // aria-controls only makes sense while the region is expanded.
        collapsed.Find("[data-testid='pdf-annotation-thread']")
            .GetAttribute("aria-controls").Should().BeNull("a collapsed thread header must not reference a detail region");
    }

    // ── Review fix 9: per-instance detail ids ─────────────────────────────────

    [Fact]
    public void ThreadPanels_TwoInstancesOnOnePage_GetDistinctDetailIds()
    {
        var cut = Render<TwoThreadPanelsHost>(p => p
            .Add(x => x.Threads, new List<DocumentCommentThread> { TextRangeThread("t1") }));

        var headers = cut.FindAll("[data-testid='pdf-annotation-thread']");
        headers.Should().HaveCount(2);
        var details = cut.FindAll("[data-testid='pdf-annotation-detail']");
        details.Should().HaveCount(2);

        var ids = details.Select(d => d.Id).ToList();
        ids[0].Should().NotBeNullOrWhiteSpace();
        ids[1].Should().NotBeNullOrWhiteSpace();
        ids[0].Should().NotBe(ids[1], "two panels on one page must not share detail ids");

        for (var i = 0; i < headers.Count; i++)
        {
            headers[i].GetAttribute("aria-controls").Should().Be(ids[i],
                "each expanded header's aria-controls points at its own detail region");
        }
    }

    /// <summary>Host rendering two thread panels side by side (two viewers on one page).</summary>
    private sealed class TwoThreadPanelsHost : ComponentBase
    {
        [Parameter] public IReadOnlyList<DocumentCommentThread> Threads { get; set; } = [];

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            for (var i = 0; i < 2; i++)
            {
                builder.OpenComponent<TmPdfAnnotationThreadPanel>(i * 10);
                builder.AddAttribute(i * 10 + 1, nameof(TmPdfAnnotationThreadPanel.Threads), Threads);
                builder.AddAttribute(i * 10 + 2, nameof(TmPdfAnnotationThreadPanel.SelectedThreadId), "t1");
                builder.CloseComponent();
            }
        }
    }

    // ── Viewer integration ────────────────────────────────────────────────────

    [Fact]
    public void Viewer_WithEnableAnnotations_RendersOverlayAndPanel()
    {
        var cut = Render<TmPdfViewer>(p => p
            .Add(x => x.Url, "https://example.com/test.pdf")
            .Add(x => x.EnableAnnotations, true));

        cut.InvokeAsync(() => cut.Instance.OnPdfLoaded(3));
        cut.Render();

        cut.Find(".tm-pdf-viewer__annotation-overlay").Should().NotBeNull();
        cut.Find("[data-testid='pdf-annotation-panel']").Should().NotBeNull();
    }

    [Fact]
    public async Task Viewer_OnTextSelectionChanged_RaisesOnTextSelectedAndShowsComposer()
    {
        PdfTextSelection? captured = null;
        var cut = Render<TmPdfViewer>(p => p
            .Add(x => x.Url, "https://example.com/test.pdf")
            .Add(x => x.EnableAnnotations, true)
            .Add(x => x.OnTextSelected, EventCallback.Factory.Create<PdfTextSelection>(this, s => captured = s)));

        await cut.InvokeAsync(() => cut.Instance.OnPdfLoaded(3));
        await cut.InvokeAsync(() => cut.Instance.OnTextSelectionChanged("hello", 1, [0.1, 0.2, 0.3, 0.02]));
        cut.Render();

        captured.Should().NotBeNull();
        captured!.Text.Should().Be("hello");
        cut.Find("[data-testid='pdf-annotation-new']").Should().NotBeNull();
    }

    [Fact]
    public async Task Viewer_CreateThreadFromSelection_PersistsToProvider()
    {
        var provider = new InMemoryPdfAnnotationProvider();
        var cut = Render<TmPdfViewer>(p => p
            .Add(x => x.Url, "https://example.com/test.pdf")
            .Add(x => x.EnableAnnotations, true)
            .Add(x => x.AnnotationProvider, provider)
            .Add(x => x.DocumentId, "doc-1"));

        await cut.InvokeAsync(() => cut.Instance.OnPdfLoaded(3));
        await cut.InvokeAsync(() => cut.Instance.OnTextSelectionChanged("hello", 1, [0.1, 0.2, 0.3, 0.02]));
        cut.Render();

        cut.Find("[data-testid='pdf-annotation-new-input']").Input("Persisted comment");
        cut.Find("[data-testid='pdf-annotation-new-submit']").Click();

        var threads = await provider.GetThreadsAsync("doc-1");
        threads.Should().ContainSingle();
        threads[0].Comments[0].Body.Should().Be("Persisted comment");
        threads[0].Anchor.Kind.Should().Be(DocumentCommentAnchorKind.TextRange);
    }

    [Fact]
    public async Task Viewer_LoadsSeededAnnotationsFromProvider()
    {
        var provider = new InMemoryPdfAnnotationProvider(new Dictionary<string, IReadOnlyList<DocumentCommentThread>>
        {
            ["doc-1"] = [TextRangeThread("seed", body: "seeded comment")]
        });

        var cut = Render<TmPdfViewer>(p => p
            .Add(x => x.Url, "https://example.com/test.pdf")
            .Add(x => x.EnableAnnotations, true)
            .Add(x => x.AnnotationProvider, provider)
            .Add(x => x.DocumentId, "doc-1"));

        await cut.InvokeAsync(() => cut.Instance.OnPdfLoaded(3));
        cut.Render();

        cut.FindAll("[data-testid='pdf-annotation-thread']").Should().HaveCount(1);
    }

    [Fact]
    public async Task Viewer_DeleteNonLastComment_KeepsThreadSelected()
    {
        var thread = new DocumentCommentThread
        {
            Id = "t1",
            Anchor = DocumentCommentAnchor.TextRange(1, [DocumentCommentRect.Create(0.1, 0.2, 0.3, 0.02)], "h"),
            Comments =
            [
                new DocumentComment { Id = "c1", AuthorId = "u1", AuthorName = "U1", Body = "first", CanDelete = true },
                new DocumentComment { Id = "c2", AuthorId = "u1", AuthorName = "U1", Body = "second", CanDelete = true }
            ]
        };
        var provider = new InMemoryPdfAnnotationProvider(new Dictionary<string, IReadOnlyList<DocumentCommentThread>>
        {
            ["doc-1"] = [thread]
        });

        var cut = Render<TmPdfViewer>(p => p
            .Add(x => x.Url, "https://example.com/test.pdf")
            .Add(x => x.EnableAnnotations, true)
            .Add(x => x.AnnotationProvider, provider)
            .Add(x => x.DocumentId, "doc-1"));

        await cut.InvokeAsync(() => cut.Instance.OnPdfLoaded(1));
        cut.Render();

        cut.Find("[data-testid='pdf-annotation-thread']").Click();
        cut.Render();
        cut.Find("[data-testid='pdf-annotation-detail']").Should().NotBeNull();

        // Delete the first (non-last) comment via its ⋯ action menu; the thread survives
        // and stays selected.
        cut.Find(".tm-pdf-annotation-panel .tm-pdf-comment-item__menu .tm-dropdown-trigger").Click();
        cut.FindAll("[data-testid='pdf-annotation-delete']").First().Click();
        cut.WaitForAssertion(() =>
            cut.FindAll("[data-testid='pdf-annotation-comment']").Should().ContainSingle());
        cut.Render();

        cut.Find("[data-testid='pdf-annotation-detail']").Should().NotBeNull();
        cut.FindAll("[data-testid='pdf-annotation-comment']").Should().ContainSingle();
    }

    [Fact]
    public void Viewer_AnnotationsToggle_HidesPanel()
    {
        var cut = Render<TmPdfViewer>(p => p
            .Add(x => x.Url, "https://example.com/test.pdf")
            .Add(x => x.ShowToolbar, true)
            .Add(x => x.EnableAnnotations, true));

        cut.InvokeAsync(() => cut.Instance.OnPdfLoaded(3));
        cut.Render();

        cut.FindAll("[data-testid='pdf-annotation-panel']").Should().ContainSingle();
        cut.Find("[data-testid='pdf-annotations-toggle']").Click();
        cut.Render();
        cut.FindAll("[data-testid='pdf-annotation-panel']").Should().BeEmpty();
    }

    // ── Search UX ─────────────────────────────────────────────────────────────

    [Fact]
    public void Viewer_OnSearchResults_ShowsCountAndNavButtons()
    {
        var cut = Render<TmPdfViewer>(p => p
            .Add(x => x.Url, "https://example.com/test.pdf")
            .Add(x => x.ShowToolbar, true)
            .Add(x => x.ShowSearch, true));

        cut.InvokeAsync(() => cut.Instance.OnPdfLoaded(5));
        cut.InvokeAsync(() => cut.Instance.OnSearchResults(3, [1, 2]));
        cut.Render();

        cut.Find("[data-testid='pdf-search-count']").TextContent.Should().Contain("1");
        cut.Find("[data-testid='pdf-search-count']").TextContent.Should().Contain("3");
        cut.Find("[data-testid='pdf-search-next']").Should().NotBeNull();
        cut.Find("[data-testid='pdf-search-prev']").Should().NotBeNull();
    }

    [Fact]
    public async Task Viewer_OnSearchActiveChanged_UpdatesActiveAndPage()
    {
        int capturedPage = 0;
        var cut = Render<TmPdfViewer>(p => p
            .Add(x => x.Url, "https://example.com/test.pdf")
            .Add(x => x.ShowToolbar, true)
            .Add(x => x.ShowSearch, true)
            .Add(x => x.PageChanged, EventCallback.Factory.Create<int>(this, n => capturedPage = n)));

        await cut.InvokeAsync(() => cut.Instance.OnPdfLoaded(5));
        await cut.InvokeAsync(() => cut.Instance.OnSearchResults(3, [1, 2]));
        await cut.InvokeAsync(() => cut.Instance.OnSearchActiveChanged(2, 3));
        cut.Render();

        cut.Find("[data-testid='pdf-search-count']").TextContent.Should().Contain("2");
        capturedPage.Should().Be(3);
    }

    // ── Phase 4 — shared cards: close button and comment edit ────────────────

    [Fact]
    public void Panel_CloseButton_RaisesCloseRequested_AndViewerHidesPanel()
    {
        // Part 1 — the panel itself raises OnCloseRequested when the header ✕ is clicked.
        var closed = false;
        var panel = Render<TmPdfAnnotationThreadPanel>(p => p
            .Add(x => x.Threads, new List<DocumentCommentThread> { TextRangeThread("t1") })
            .Add(x => x.OnCloseRequested, EventCallback.Factory.Create(this, () => closed = true)));
        panel.Find("[data-testid='pdf-annotation-close']").Click();
        closed.Should().BeTrue("the panel's close button raises OnCloseRequested");

        // Part 2 — the viewer hides the annotations panel entirely after the close request.
        var cut = Render<TmPdfViewer>(p => p
            .Add(x => x.Url, "https://example.com/test.pdf")
            .Add(x => x.EnableAnnotations, true));
        cut.InvokeAsync(() => cut.Instance.OnPdfLoaded(3));
        cut.Render();

        cut.Find("[data-testid='pdf-annotation-panel']").Should().NotBeNull();
        cut.Find("[data-testid='pdf-annotation-close']").Click();
        cut.Render();
        cut.FindAll("[data-testid='pdf-annotation-panel']").Should().BeEmpty(
            "the viewer hides the panel after its close request");
    }

    [Fact]
    public async Task Viewer_EditComment_CallsProviderEditAsync_AndRaisesAnnotationsChanged()
    {
        var thread = TextRangeThread("t1");
        thread.Comments[0].CanEdit = true;
        var provider = new InMemoryPdfAnnotationProvider(new Dictionary<string, IReadOnlyList<DocumentCommentThread>>
        {
            ["doc-1"] = [thread]
        });
        var changes = 0;
        var cut = Render<TmPdfViewer>(p => p
            .Add(x => x.Url, "https://example.com/test.pdf")
            .Add(x => x.EnableAnnotations, true)
            .Add(x => x.AnnotationProvider, provider)
            .Add(x => x.DocumentId, "doc-1")
            .Add(x => x.AnnotationsChanged, EventCallback.Factory.Create<IReadOnlyList<DocumentCommentThread>>(this, _ => changes++)));

        await cut.InvokeAsync(() => cut.Instance.OnPdfLoaded(1));
        cut.Render();
        var changesAfterLoad = changes;

        cut.Find("[data-testid='pdf-annotation-thread']").Click();
        cut.Render();
        cut.Find(".tm-pdf-annotation-panel .tm-pdf-comment-item__menu .tm-dropdown-trigger").Click();
        cut.WaitForElement("[data-testid='pdf-annotation-edit']").Click();
        cut.WaitForElement("[data-testid='pdf-annotation-edit-input']").Input("edited body");
        cut.Find("[data-testid='pdf-annotation-edit-save']").Click();

        var threads = await provider.GetThreadsAsync("doc-1");
        threads[0].Comments[0].Body.Should().Be("edited body");
        threads[0].Comments[0].EditedAt.Should().NotBeNull();
        changes.Should().BeGreaterThan(changesAfterLoad,
            "the viewer re-raises AnnotationsChanged after persisting the edit");
    }
}
