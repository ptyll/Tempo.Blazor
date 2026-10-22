using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using FluentAssertions;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.Abstractions.Models;
using Tempo.Blazor.Components.Files;
using Tempo.Blazor.Components.Toolbar;
using Tempo.Blazor.Tests.Localization;
using Xunit;

namespace Tempo.Blazor.Tests.Components.Files;

/// <summary>
/// FZE-9 (PdfViewer redesign phase 9) touch tests of <see cref="TmPdfAnnotator"/>: one finger or
/// the pen draws while a second finger is the pinch hand-over (the stroke is cancelled, never
/// committed), and the tablet-only sticky "Highlight" CTA surfaces a touch selection's draft.
/// Kept beside <see cref="TmPdfAnnotatorTests"/> so neither file grows past the 1200-line ceiling.
/// </summary>
public class TmPdfAnnotatorTouchTests : LocalizationTestBase
{
    private const string DocUrl = "/sample.pdf";
    private const string DocId = "doc-1";

    public TmPdfAnnotatorTouchTests()
    {
        JSInterop.Setup<bool>("tmPdfViewer.isAvailable").SetResult(true);
        JSInterop.Setup<double[]>("tmPdfAnnotator.measure", _ => true).SetResult([800, 1000]);
    }

    private static DocumentCommentUser Alice => new() { UserId = "alice", DisplayName = "Alice", Role = "lawyer" };

    /// <summary>Renders the annotator with a forced <see cref="TmLayoutMode"/> (tests need no DOM).</summary>
    private IRenderedComponent<TmPdfAnnotator> RenderInLayout(
        TmLayoutMode mode,
        Action<Bunit.ComponentParameterCollectionBuilder<TmPdfAnnotator>>? configure = null)
        => Render<TmPdfAnnotator>(p =>
        {
            p.Add(x => x.Url, DocUrl);
            p.Add(x => x.DocumentId, DocId);
            p.Add(x => x.CurrentUser, Alice);
            p.Add(x => x.LayoutMode, mode);
            configure?.Invoke(p);
        });

    private IRenderedComponent<TmPdfAnnotator> Render(
        IPdfAnnotationProvider? provider = null,
        Action<Bunit.ComponentParameterCollectionBuilder<TmPdfAnnotator>>? configure = null)
        => Render<TmPdfAnnotator>(p =>
        {
            p.Add(x => x.Url, DocUrl);
            p.Add(x => x.DocumentId, DocId);
            p.Add(x => x.CurrentUser, Alice);
            if (provider is not null)
            {
                p.Add(x => x.AnnotationProvider, provider);
            }
            configure?.Invoke(p);
        });

    // ── FZE-9: touch and pen draw; a second finger is the pinch hand-over ────

    [Fact]
    public async Task DrawMode_SingleFinger_CreatesStroke()
    {
        var provider = new InMemoryPdfAnnotationProvider();
        var cut = Render(provider);
        cut.Find("[data-testid='pdf-annotator-mode-draw']").Click();

        var surface = cut.Find("[data-testid='pdf-annotator-surface']");
        await surface.TriggerEventAsync("onpointerdown", new PointerEventArgs { OffsetX = 80, OffsetY = 100, PointerId = 1, PointerType = "touch", IsPrimary = true });
        await surface.TriggerEventAsync("onpointermove", new PointerEventArgs { OffsetX = 160, OffsetY = 200, PointerId = 1, PointerType = "touch", IsPrimary = true });
        await surface.TriggerEventAsync("onpointerup", new PointerEventArgs { OffsetX = 160, OffsetY = 200, PointerId = 1, PointerType = "touch", IsPrimary = true });

        cut.WaitForElement("[data-testid='pdf-annotator-new']");
        cut.Find("[data-testid='pdf-annotator-new-submit']").Click();

        cut.WaitForAssertion(async () =>
        {
            var threads = await provider.GetThreadsAsync(DocId);
            threads.Should().HaveCount(1, "one finger draws in Draw mode");
            threads[0].InkStrokes.Should().HaveCount(1);
        });
    }

    [Fact]
    public async Task DrawMode_PenPointer_CreatesStroke()
    {
        var provider = new InMemoryPdfAnnotationProvider();
        var cut = Render(provider);
        cut.Find("[data-testid='pdf-annotator-mode-draw']").Click();

        var surface = cut.Find("[data-testid='pdf-annotator-surface']");
        await surface.TriggerEventAsync("onpointerdown", new PointerEventArgs { OffsetX = 80, OffsetY = 100, PointerId = 7, PointerType = "pen" });
        await surface.TriggerEventAsync("onpointermove", new PointerEventArgs { OffsetX = 160, OffsetY = 200, PointerId = 7, PointerType = "pen" });
        await surface.TriggerEventAsync("onpointerup", new PointerEventArgs { OffsetX = 160, OffsetY = 200, PointerId = 7, PointerType = "pen" });

        cut.WaitForElement("[data-testid='pdf-annotator-new']");
        cut.Find("[data-testid='pdf-annotator-new-submit']").Click();

        cut.WaitForAssertion(async () =>
        {
            var threads = await provider.GetThreadsAsync(DocId);
            threads.Should().HaveCount(1, "a stylus draws in Draw mode");
            threads[0].InkStrokes.Should().HaveCount(1);
        });
    }

    [Fact]
    public async Task DrawMode_MultiTouch_DoesNotCreateStroke()
    {
        var provider = new InMemoryPdfAnnotationProvider();
        var cut = Render(provider);
        cut.Find("[data-testid='pdf-annotator-mode-draw']").Click();

        // The first finger starts a stroke; the second finger is the pinch hand-over —
        // the stroke is cancelled (never committed) and no new one starts.
        var surface = cut.Find("[data-testid='pdf-annotator-surface']");
        await surface.TriggerEventAsync("onpointerdown", new PointerEventArgs { OffsetX = 80, OffsetY = 100, PointerId = 1, PointerType = "touch", IsPrimary = true });
        await surface.TriggerEventAsync("onpointerdown", new PointerEventArgs { OffsetX = 200, OffsetY = 300, PointerId = 2, PointerType = "touch", IsPrimary = false });
        await surface.TriggerEventAsync("onpointermove", new PointerEventArgs { OffsetX = 160, OffsetY = 200, PointerId = 1, PointerType = "touch", IsPrimary = true });
        await surface.TriggerEventAsync("onpointerup", new PointerEventArgs { OffsetX = 160, OffsetY = 200, PointerId = 1, PointerType = "touch", IsPrimary = true });
        await surface.TriggerEventAsync("onpointerup", new PointerEventArgs { OffsetX = 200, OffsetY = 300, PointerId = 2, PointerType = "touch", IsPrimary = false });

        cut.FindAll("[data-testid='pdf-annotator-new']").Should().BeEmpty(
            "a two-finger gesture is the pan/pinch hand-over: no stroke draft may appear");

        var threads = await provider.GetThreadsAsync(DocId);
        threads.Should().BeEmpty("two fingers pan and zoom, they never draw");
    }

    [Fact]
    public async Task DrawMode_PenStroke_IgnoresTouchContact()
    {
        var provider = new InMemoryPdfAnnotationProvider();
        var cut = Render(provider);
        cut.Find("[data-testid='pdf-annotator-mode-draw']").Click();

        // The pen starts a stroke; a palm touch lands and lifts mid-stroke. The pen has
        // priority: the palm is ignored (it neither cancels nor extends the stroke) and
        // the pen keeps drawing.
        var surface = cut.Find("[data-testid='pdf-annotator-surface']");
        await surface.TriggerEventAsync("onpointerdown", new PointerEventArgs { OffsetX = 80, OffsetY = 100, PointerId = 7, PointerType = "pen" });
        await surface.TriggerEventAsync("onpointerdown", new PointerEventArgs { OffsetX = 200, OffsetY = 300, PointerId = 2, PointerType = "touch", IsPrimary = false });
        await surface.TriggerEventAsync("onpointermove", new PointerEventArgs { OffsetX = 210, OffsetY = 310, PointerId = 2, PointerType = "touch", IsPrimary = false });
        await surface.TriggerEventAsync("onpointerup", new PointerEventArgs { OffsetX = 210, OffsetY = 310, PointerId = 2, PointerType = "touch", IsPrimary = false });
        await surface.TriggerEventAsync("onpointermove", new PointerEventArgs { OffsetX = 160, OffsetY = 200, PointerId = 7, PointerType = "pen" });
        await surface.TriggerEventAsync("onpointerup", new PointerEventArgs { OffsetX = 160, OffsetY = 200, PointerId = 7, PointerType = "pen" });

        cut.WaitForElement("[data-testid='pdf-annotator-new']");
        cut.Find("[data-testid='pdf-annotator-new-submit']").Click();

        cut.WaitForAssertion(async () =>
        {
            var threads = await provider.GetThreadsAsync(DocId);
            threads.Should().HaveCount(1, "the pen stroke survived the palm contact");
            threads[0].InkStrokes.Should().HaveCount(1, "the pen stroke is committed, the palm never drew");
        });
    }

    // ── FZE-9 review (M2): the pinch (and pointercancel) discard the stroke ──

    [Fact]
    public async Task DrawMode_PinchStart_DiscardsStrokeWithoutCommitting()
    {
        var provider = new InMemoryPdfAnnotationProvider();
        var cut = Render(provider);
        cut.Find("[data-testid='pdf-annotator-mode-draw']").Click();

        var surface = cut.Find("[data-testid='pdf-annotator-surface']");
        await surface.TriggerEventAsync("onpointerdown", new PointerEventArgs { OffsetX = 80, OffsetY = 100, PointerId = 1, PointerType = "touch", IsPrimary = true });
        await surface.TriggerEventAsync("onpointermove", new PointerEventArgs { OffsetX = 120, OffsetY = 150, PointerId = 1, PointerType = "touch", IsPrimary = true });

        // The second finger lands → pdf-viewer.js onStart → the viewer raises
        // PinchStarted → the annotator discards the stroke (never commits it).
        var viewer = cut.FindComponent<TmPdfViewer>().Instance;
        await cut.InvokeAsync(() => viewer.OnPinchStarted());

        cut.FindAll("[data-testid='pdf-annotator-new']").Should().BeEmpty(
            "the pinch discards the live stroke: no draft may appear");

        // The stroke's own finger lifting after the pinch must not resurrect anything.
        await surface.TriggerEventAsync("onpointerup", new PointerEventArgs { OffsetX = 120, OffsetY = 150, PointerId = 1, PointerType = "touch", IsPrimary = true });
        cut.FindAll("[data-testid='pdf-annotator-new']").Should().BeEmpty();

        var threads = await provider.GetThreadsAsync(DocId);
        threads.Should().BeEmpty("a discarded stroke is never committed");
    }

    [Fact]
    public async Task DrawMode_PointerCancel_DiscardsStroke()
    {
        var provider = new InMemoryPdfAnnotationProvider();
        var cut = Render(provider);
        cut.Find("[data-testid='pdf-annotator-mode-draw']").Click();

        var surface = cut.Find("[data-testid='pdf-annotator-surface']");
        await surface.TriggerEventAsync("onpointerdown", new PointerEventArgs { OffsetX = 80, OffsetY = 100, PointerId = 1, PointerType = "touch", IsPrimary = true });
        await surface.TriggerEventAsync("onpointercancel", new PointerEventArgs { OffsetX = 120, OffsetY = 150, PointerId = 1, PointerType = "touch", IsPrimary = true });

        cut.FindAll("[data-testid='pdf-annotator-new']").Should().BeEmpty(
            "pointercancel discards the live stroke");

        var threads = await provider.GetThreadsAsync(DocId);
        threads.Should().BeEmpty("a cancelled stroke is never committed");
    }

    // ── FZE-14: continuous mode — the stroke stays on its start page ─────────

    /// <summary>Reports the mounted band (pages 1, 3, 5 at 612×792) on the embedded viewer,
    /// the bUnit equivalent of the JS geometry report.</summary>
    private void MountContinuousPages(IRenderedComponent<TmPdfAnnotator> cut)
    {
        var viewer = cut.FindComponent<TmPdfViewer>().Instance;
        cut.InvokeAsync(() => viewer.OnPdfLoaded(10));
        cut.Render();
        cut.InvokeAsync(() => viewer.OnContinuousPageGeometry(
            new[] { 1, 3, 5 }, new[] { 0.0, 1600.0, 3200.0 }, new[] { 0.0, 0.0, 0.0 },
            new[] { 612.0, 612.0, 612.0 }, new[] { 792.0, 792.0, 792.0 }, 0));
        cut.Render();
    }

    private static string ContinuousSurface(int page)
        => $".tm-pdf-viewer__page-overlay-slot[data-page='{page}'] [data-testid='pdf-annotator-surface']";

    [Fact]
    public async Task Continuous_InkStroke_ConfinedToStartPage()
    {
        var provider = new InMemoryPdfAnnotationProvider();
        var cut = Render(provider, p => p.Add(x => x.ViewMode, PdfViewMode.Continuous));
        MountContinuousPages(cut);
        cut.Find("[data-testid='pdf-annotator-mode-draw']").Click();

        // The stroke starts on page 3 and crosses into page 5's surface: the points of the
        // other page never join the stroke and the anchor stays on the start page (the
        // stroke is clipped at the boundary — pointerleave commits it there in a real DOM).
        var start = cut.Find(ContinuousSurface(3));
        await start.TriggerEventAsync("onpointerdown", new PointerEventArgs { OffsetX = 80, OffsetY = 100, PointerId = 1, PointerType = "touch", IsPrimary = true });
        await start.TriggerEventAsync("onpointermove", new PointerEventArgs { OffsetX = 160, OffsetY = 200, PointerId = 1, PointerType = "touch", IsPrimary = true });
        await cut.Find(ContinuousSurface(5))
            .TriggerEventAsync("onpointermove", new PointerEventArgs { OffsetX = 100, OffsetY = 100, PointerId = 1, PointerType = "touch", IsPrimary = true });
        await start.TriggerEventAsync("onpointerup", new PointerEventArgs { OffsetX = 160, OffsetY = 200, PointerId = 1, PointerType = "touch", IsPrimary = true });

        cut.WaitForElement("[data-testid='pdf-annotator-new']");
        cut.Find("[data-testid='pdf-annotator-new-submit']").Click();

        cut.WaitForAssertion(async () =>
        {
            var threads = await provider.GetThreadsAsync(DocId);
            threads.Should().HaveCount(1);
            threads[0].Anchor.PageNumber.Should().Be(3, "the stroke is anchored to the page it started on");
            threads[0].InkStrokes.Should().HaveCount(1);
            threads[0].InkStrokes[0].Points.Should().HaveCount(2)
                .And.OnlyContain(p => p.Y <= 1.0, "no coordinate from the other page's surface joins the stroke");
            threads[0].InkStrokes[0].Points[0].X.Should().BeApproximately(0.1307, 0.001, "points normalize against the start page's slot box");
        });
    }

    [Fact]
    public async Task Continuous_TwoFingerPinchCancelsStroke()
    {
        var provider = new InMemoryPdfAnnotationProvider();
        var cut = Render(provider, p => p.Add(x => x.ViewMode, PdfViewMode.Continuous));
        MountContinuousPages(cut);
        cut.Find("[data-testid='pdf-annotator-mode-draw']").Click();

        // One finger draws on the page-3 surface; the second finger is the pinch hand-over
        // (pen priority unchanged) — the stroke is discarded, never committed.
        var surface = cut.Find(ContinuousSurface(3));
        await surface.TriggerEventAsync("onpointerdown", new PointerEventArgs { OffsetX = 80, OffsetY = 100, PointerId = 1, PointerType = "touch", IsPrimary = true });
        await surface.TriggerEventAsync("onpointerdown", new PointerEventArgs { OffsetX = 200, OffsetY = 300, PointerId = 2, PointerType = "touch", IsPrimary = false });
        await surface.TriggerEventAsync("onpointerup", new PointerEventArgs { OffsetX = 200, OffsetY = 300, PointerId = 1, PointerType = "touch", IsPrimary = true });

        cut.FindAll("[data-testid='pdf-annotator-new']").Should().BeEmpty(
            "the pinch hand-over cancels the stroke on the per-page surface too");

        var threads = await provider.GetThreadsAsync(DocId);
        threads.Should().BeEmpty("a cancelled stroke is never committed");
    }

    // ── FZE-9: the tablet highlight CTA surfaces the touch selection draft ───

    [Fact]
    public async Task Tablet_HighlightMode_PendingSelection_ShowsHighlightCta_AndOpensComposer()
    {
        var cut = RenderInLayout(TmLayoutMode.Tablet, p =>
        {
            p.Add(x => x.ShowPanel, false);
            p.Add(x => x.Mode, PdfAnnotatorMode.Highlight);
        });

        cut.FindAll("[data-testid='pdf-annotator-highlight-cta']").Should().BeEmpty();

        var viewer = cut.FindComponent<TmPdfViewer>().Instance;
        await cut.InvokeAsync(() => viewer.OnTextSelectionChanged("selected words", 1, [0.1, 0.2, 0.3, 0.02]));

        var cta = cut.Find("[data-testid='pdf-annotator-highlight-cta']");
        cta.TextContent.Should().Contain("Highlight", "the tablet sticky CTA names the highlight action");
        cta.Click();

        cut.WaitForAssertion(() =>
            cut.FindAll("[data-testid='pdf-annotator-new']").Should().NotBeEmpty(
                "the CTA opens the panel with the pending highlight draft"));
        cut.Find("[data-testid='pdf-annotator-new-input']").Input("highlight note");
        cut.Find("[data-testid='pdf-annotator-new-submit']").Click();
    }

    [Fact]
    public async Task Mobile_HighlightMode_ShowsNoHighlightCta_G2()
    {
        var cut = RenderInLayout(TmLayoutMode.Mobile, p => p.Add(x => x.Mode, PdfAnnotatorMode.Highlight));

        var viewer = cut.FindComponent<TmPdfViewer>().Instance;
        await cut.InvokeAsync(() => viewer.OnTextSelectionChanged("selected words", 1, [0.1, 0.2, 0.3, 0.02]));

        cut.FindAll("[data-testid='pdf-annotator-highlight-cta']").Should().BeEmpty(
            "G2: the highlight tools (and their CTA) do not exist on a phone");
    }

}
