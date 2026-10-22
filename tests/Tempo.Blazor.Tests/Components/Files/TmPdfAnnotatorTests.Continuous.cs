using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.Abstractions.Models;
using Tempo.Blazor.Components.Files;
using Tempo.Blazor.Components.Toolbar;
using Xunit;

namespace Tempo.Blazor.Tests.Components.Files;

/// <summary>
/// FZE-14: the annotator in Continuous mode. The SinglePage pin is lifted — the tools
/// (stamp, draw, highlight, comment) work per page of the continuous column through the
/// viewer's templated <c>PageOverlayContent</c> slots (FZE-13), the ink stroke stays
/// confined to the page where it started, thread selection scrolls to the anchor (the
/// shared FZE-13 viewer path) and export covers every page. The SinglePage default is
/// additive: hosts that never set <c>ViewMode</c> keep the pre-FZE-14 behavior.
/// </summary>
public partial class TmPdfAnnotatorTests
{
    /// <summary>The default geometry report of the continuous tests: pages 1, 3 and 5
    /// mounted inside the ±50% band, each 612×792 CSS px (a portrait US-Letter box).</summary>
    private static readonly int[] MountedPages = [1, 3, 5];
    private static readonly double[] MountedTops = [0.0, 1600.0, 3200.0];

    /// <summary>A stamp thread anchored on an arbitrary page (the shared <see cref="Stamp"/>
    /// helper is pinned to page 1).</summary>
    private static DocumentCommentThread StampOnPage(string id, int page, string text = "APPROVED")
        => new()
        {
            Id = id,
            Kind = DocumentAnnotationKind.Stamp,
            StampText = text,
            Anchor = DocumentCommentAnchor.Area(page, 0.6, 0.1, 0.2, 0.06),
            Comments = [new DocumentComment { Id = id + "-c", AuthorId = "alice", AuthorName = "Alice", Body = text }]
        };

    /// <summary>Reports the mounted band on the embedded viewer and re-renders, so the
    /// annotator's per-page overlay slots exist (the bUnit equivalent of the JS geometry
    /// report — the same <see cref="TmPdfViewer.OnContinuousPageGeometry"/> contract).</summary>
    private void MountContinuousPages(IRenderedComponent<TmPdfAnnotator> cut, int[]? pages = null)
    {
        var viewer = cut.FindComponent<TmPdfViewer>().Instance;
        cut.InvokeAsync(() => viewer.OnPdfLoaded(10));
        cut.Render();
        cut.InvokeAsync(() => viewer.OnContinuousPageGeometry(
            pages ?? MountedPages, MountedTops, [0.0, 0.0, 0.0], [612.0, 612.0, 612.0], [792.0, 792.0, 792.0], 0));
        cut.Render();
    }

    private IRenderedComponent<TmPdfAnnotator> RenderContinuous(
        IPdfAnnotationProvider? provider = null,
        Action<Bunit.ComponentParameterCollectionBuilder<TmPdfAnnotator>>? configure = null)
        => Render(provider, p => p.Add(x => x.ViewMode, PdfViewMode.Continuous));

    /// <summary>The annotator's interaction surface inside one page slot.</summary>
    private static string SurfaceSelector(int page)
        => $".tm-pdf-viewer__page-overlay-slot[data-page='{page}'] [data-testid='pdf-annotator-surface']";

    // ── Pass-through parameters ──────────────────────────────────────────────

    [Fact]
    public void ViewMode_Continuous_PassesThroughToViewer_AndRaisesViewModeChanged()
    {
        var cut = RenderContinuous();
        cut.FindComponent<TmPdfViewer>().Instance.ViewMode
            .Should().Be(PdfViewMode.Continuous, "the annotator forwards the host's ViewMode to the embedded viewer");

        // The viewer's own toggle raises the annotator's two-way callback (the annotator
        // never blocks it — PD1 is lifted).
        PdfViewMode? raised = null;
        var bound = Render(configure: p => p
            .Add(x => x.ViewMode, PdfViewMode.SinglePage)
            .Add(x => x.ShowViewModeToggle, true)
            .Add(x => x.ViewModeChanged, EventCallback.Factory.Create<PdfViewMode>(this, m => raised = m)));
        var viewer = bound.FindComponent<TmPdfViewer>().Instance;
        bound.InvokeAsync(() => viewer.OnPdfLoaded(5));
        bound.Render();

        var continuousInput = bound.FindAll(".tm-pdf-viewer__view-mode input[type='radio']").Last();
        continuousInput.Change(true);

        raised.Should().Be(PdfViewMode.Continuous, "the annotator forwards the viewer's ViewModeChanged");
        bound.FindComponent<TmPdfViewer>().Instance.ViewMode.Should().Be(PdfViewMode.Continuous);
    }

    [Fact]
    public void ShowViewModeToggle_RendersSegmentInMergedToolbar()
    {
        var cut = Render(configure: p => p
            .Add(x => x.ViewMode, PdfViewMode.Continuous)
            .Add(x => x.ShowViewModeToggle, true));
        var viewer = cut.FindComponent<TmPdfViewer>().Instance;
        cut.InvokeAsync(() => viewer.OnPdfLoaded(5));
        cut.Render();

        cut.FindComponent<TmPdfViewer>().Instance.ShowViewModeToggle.Should().BeTrue();
        cut.Find(".tm-pdf-viewer__toolbar").QuerySelector(".tm-pdf-viewer__view-mode")
            .Should().NotBeNull("the view-mode segment rides the merged toolbar");
        cut.FindAll(".tm-pdf-viewer__view-mode input[type='radio']").Should().HaveCount(2);

        // Default off: the annotator keeps the pre-FZE-14 toolbar without the segment.
        var plain = Render(configure: p => p.Add(x => x.ViewMode, PdfViewMode.Continuous));
        plain.FindComponent<TmPdfViewer>().Instance.ShowViewModeToggle.Should().BeFalse();
    }

    // ── Per-page layer and surface ───────────────────────────────────────────

    [Fact]
    public void Continuous_LayerAndSurfacePerMountedPage()
    {
        var cut = RenderContinuous(Seeded(Highlight("h1", page: 3), StampOnPage("s1", 5)));
        MountContinuousPages(cut);

        // One annotator layer + surface per mounted page, carrying data-page.
        foreach (var page in MountedPages)
        {
            cut.Find(SurfaceSelector(page)).Should().NotBeNull($"page {page} gets its own interaction surface");
        }

        cut.FindAll(".tm-pdf-viewer__page-overlay-slot .tm-pdf-annotator-layer").Should().HaveCount(MountedPages.Length);
        cut.Find(".tm-pdf-viewer__page-overlay-slot[data-page='3'] [data-testid='pdf-annotator-highlight']")
            .Should().NotBeNull("the page-3 thread renders in its own slot");
        cut.Find(".tm-pdf-viewer__page-overlay-slot[data-page='5'] [data-testid='pdf-annotator-stamp']")
            .Should().NotBeNull("the page-5 thread renders in its own slot");
        cut.FindAll(".tm-pdf-viewer__page-overlay-slot[data-page='1'] [data-testid='pdf-annotator-marker']")
            .Should().BeEmpty("a page without threads renders no markers");

        // The single-page overlay must NOT double-render: only the per-page slots exist.
        cut.FindAll(".tm-pdf-viewer__custom-overlay").Should().BeEmpty();
    }

    [Fact]
    public async Task Continuous_StampClick_OnPage5_CreatesAnchorOnPage5()
    {
        var provider = new InMemoryPdfAnnotationProvider();
        var cut = RenderContinuous(provider);
        MountContinuousPages(cut);
        cut.Find("[data-testid='pdf-annotator-mode-stamp']").Click();

        // A click at the middle of the page-5 surface (612×792 slot box) must anchor on
        // page 5 with normalized 0.5/0.5 coordinates — never on the annotator's current page.
        await cut.Find(SurfaceSelector(5))
            .TriggerEventAsync("onclick", new MouseEventArgs { OffsetX = 306, OffsetY = 396 });

        cut.WaitForAssertion(async () =>
        {
            var threads = await provider.GetThreadsAsync(DocId);
            threads.Should().HaveCount(1);
            threads[0].Kind.Should().Be(DocumentAnnotationKind.Stamp);
            threads[0].Anchor.PageNumber.Should().Be(5, "the slot's page decides the anchor page");
            threads[0].Anchor.X.Should().BeApproximately(0.5, 0.01);
            threads[0].Anchor.Y.Should().BeApproximately(0.5, 0.01);
        });
    }

    [Fact]
    public async Task Continuous_CommentClick_UsesPageNormalizedCoordinates()
    {
        var provider = new InMemoryPdfAnnotationProvider();
        var cut = RenderContinuous(provider);
        MountContinuousPages(cut);
        cut.Find("[data-testid='pdf-annotator-mode-comment']").Click();

        await cut.Find(SurfaceSelector(1))
            .TriggerEventAsync("onclick", new MouseEventArgs { OffsetX = 153, OffsetY = 198 });

        cut.WaitForAssertion(async () =>
        {
            var threads = await provider.GetThreadsAsync(DocId);
            threads.Should().HaveCount(1);
            threads[0].Kind.Should().Be(DocumentAnnotationKind.Comment);
            threads[0].Anchor.PageNumber.Should().Be(1);
            threads[0].Anchor.X.Should().BeApproximately(0.25, 0.01, "the click normalizes against the page slot box");
            threads[0].Anchor.Y.Should().BeApproximately(0.25, 0.01);
        });
    }

    // ── Draft lifecycle across the continuous column ─────────────────────────

    [Fact]
    public async Task Continuous_DraftOnPage3_SurvivesScrollAway_AndComposerStaysBound()
    {
        var cut = RenderContinuous();
        MountContinuousPages(cut);
        cut.Find("[data-testid='pdf-annotator-mode-comment']").Click();

        await cut.Find(SurfaceSelector(3))
            .TriggerEventAsync("onclick", new MouseEventArgs { OffsetX = 100, OffsetY = 100 });
        cut.WaitForElement("[data-testid='pdf-annotator-new']");
        cut.Find(".tm-pdf-viewer__page-overlay-slot[data-page='3'] [data-testid='pdf-annotator-draft']")
            .Should().NotBeNull("the draft marker paints in its page's slot");

        // The user scrolls away: the mounted band no longer contains page 3 — the draft
        // (component state) survives and the panel composer stays bound to it.
        MountContinuousPages(cut, [8, 9]);
        cut.FindAll("[data-testid='pdf-annotator-new']").Should().HaveCount(1,
            "the draft and its composer are bound to the draft page, not to the mounted band");

        // Scrolling back remounts page 3 and the draft marker paints again.
        MountContinuousPages(cut);
        cut.Find(".tm-pdf-viewer__page-overlay-slot[data-page='3'] [data-testid='pdf-annotator-draft']")
            .Should().NotBeNull("the draft reappears with its page's slot");
    }

    [Fact]
    public async Task Continuous_SecondStrokeOnOtherPage_DoesNotJoinPage3Draft()
    {
        var provider = new InMemoryPdfAnnotationProvider();
        var cut = RenderContinuous(provider);
        MountContinuousPages(cut);
        cut.Find("[data-testid='pdf-annotator-mode-draw']").Click();

        // Stroke one: committed on page 3 — it opens the drawing draft anchored there.
        var page3 = cut.Find(SurfaceSelector(3));
        await page3.TriggerEventAsync("onpointerdown", new PointerEventArgs { OffsetX = 100, OffsetY = 100, PointerId = 1, PointerType = "touch", IsPrimary = true });
        await page3.TriggerEventAsync("onpointermove", new PointerEventArgs { OffsetX = 200, OffsetY = 200, PointerId = 1, PointerType = "touch", IsPrimary = true });
        await page3.TriggerEventAsync("onpointerup", new PointerEventArgs { OffsetX = 200, OffsetY = 200, PointerId = 1, PointerType = "touch", IsPrimary = true });
        cut.WaitForElement("[data-testid='pdf-annotator-new']");
        cut.Find(".tm-pdf-viewer__page-overlay-slot[data-page='3'] [data-testid='pdf-annotator-draft']")
            .Should().NotBeNull("the first stroke opens a draft anchored on page 3");

        // Stroke two: a contact inside page 5's slot must not join the page-3 draft —
        // the stroke is refused and the page-3 draft stays page-confined.
        var page5 = cut.Find(SurfaceSelector(5));
        await page5.TriggerEventAsync("onpointerdown", new PointerEventArgs { OffsetX = 100, OffsetY = 100, PointerId = 2, PointerType = "touch", IsPrimary = true });
        await page5.TriggerEventAsync("onpointermove", new PointerEventArgs { OffsetX = 300, OffsetY = 300, PointerId = 2, PointerType = "touch", IsPrimary = true });
        await page5.TriggerEventAsync("onpointerup", new PointerEventArgs { OffsetX = 300, OffsetY = 300, PointerId = 2, PointerType = "touch", IsPrimary = true });

        cut.FindAll(".tm-pdf-viewer__page-overlay-slot[data-page='5'] [data-testid='pdf-annotator-draft']")
            .Should().BeEmpty("the refused stroke never opens a page-5 draft");

        cut.Find("[data-testid='pdf-annotator-new-submit']").Click();
        cut.WaitForAssertion(async () =>
        {
            var threads = await provider.GetThreadsAsync(DocId);
            threads.Should().HaveCount(1);
            threads[0].Anchor.PageNumber.Should().Be(3, "the draft keeps its page-3 anchor");
            threads[0].InkStrokes.Should().HaveCount(1, "the page-5 contact drew nothing and joined nothing");
        });
    }

    [Fact]
    public async Task Continuous_SelectThread_ScrollsToAnchor()
    {
        var thread = new DocumentCommentThread
        {
            Id = "t5",
            Kind = DocumentAnnotationKind.Comment,
            Anchor = DocumentCommentAnchor.Point(5, 0.5, 0.4),
            Comments = [new DocumentComment { Id = "t5-c", AuthorId = "alice", AuthorName = "Alice", Body = "note" }]
        };
        var cut = RenderContinuous(Seeded(thread));
        MountContinuousPages(cut);

        cut.WaitForElement("[data-testid='pdf-annotator-thread'][data-thread-id='t5']").Click();

        // The shared FZE-13 path: the viewer scrolls the wrap to the page and centers the
        // anchor's Y — the mode stays Continuous.
        cut.WaitForAssertion(() =>
        {
            var scroll = JSInterop.Invocations.Single(i => i.Identifier == "tmPdfViewer.scrollToAnchor");
            scroll.Arguments[1].Should().Be(5, "the thread's page is the scroll target");
            scroll.Arguments[2].Should().Be(0.4, "the anchor's normalized Y centers the scroll");
        });
    }

    [Fact]
    public async Task Continuous_HighlightFromSelection_OnCorrectPage()
    {
        var provider = new InMemoryPdfAnnotationProvider();
        var cut = RenderContinuous(provider);
        MountContinuousPages(cut);
        cut.Find("[data-testid='pdf-annotator-mode-highlight']").Click();

        var viewer = cut.FindComponent<TmPdfViewer>().Instance;
        await cut.InvokeAsync(() => viewer.OnTextSelectionChanged("quoted words", 5, [0.1, 0.2, 0.3, 0.02]));

        cut.WaitForElement("[data-testid='pdf-annotator-new']");
        cut.Find("[data-testid='pdf-annotator-new-input']").Input("highlight note");
        cut.Find("[data-testid='pdf-annotator-new-submit']").Click();

        cut.WaitForAssertion(async () =>
        {
            var threads = await provider.GetThreadsAsync(DocId);
            threads.Should().HaveCount(1);
            threads[0].Kind.Should().Be(DocumentAnnotationKind.Highlight);
            threads[0].Anchor.PageNumber.Should().Be(5, "the text selection's page decides the anchor page");
            threads[0].Anchor.HighlightedText.Should().Be("quoted words");
        });
    }

    // ── G2 and export ────────────────────────────────────────────────────────

    [Fact]
    public void Continuous_Mobile_G2_OnlyBrowseAndComment()
    {
        var cut = Render(configure: p =>
        {
            p.Add(x => x.LayoutMode, TmLayoutMode.Mobile);
            p.Add(x => x.ViewMode, PdfViewMode.Continuous);
        });
        MountContinuousPages(cut);

        // G2 is a layout rule and holds in Continuous too: the editing tools stay hidden.
        cut.FindAll("[data-testid='pdf-annotator-mode-highlight']").Should().BeEmpty();
        cut.FindAll("[data-testid='pdf-annotator-mode-stamp']").Should().BeEmpty();
        cut.FindAll("[data-testid='pdf-annotator-mode-draw']").Should().BeEmpty();
        cut.Find("[data-testid='pdf-annotator-mode-browse']").Should().NotBeNull();
        cut.Find("[data-testid='pdf-annotator-mode-comment']").Should().NotBeNull();
    }

    [Fact]
    public async Task Export_Continuous_ContainsAnnotationsOfAllPages()
    {
        var handler = JSInterop.SetupVoid("tmPdfAnnotator.exportPdf", _ => true).SetVoidResult();
        var cut = RenderContinuous(Seeded(Highlight("h2", page: 2), Highlight("h5", page: 5)));
        MountContinuousPages(cut);

        await cut.InvokeAsync(() => cut.Find("[data-testid='pdf-annotator-export']").Click());

        cut.WaitForAssertion(() =>
        {
            handler.Invocations.Should().NotBeEmpty();
            var payload = handler.Invocations.Last().Arguments[1] as string;
            payload.Should().Contain("\"page\":2", "the export payload carries every page's annotations");
            payload.Should().Contain("\"page\":5", "the export is view-mode independent — all pages included");
        });
    }
}
