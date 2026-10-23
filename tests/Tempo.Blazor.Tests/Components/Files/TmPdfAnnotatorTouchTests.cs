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
