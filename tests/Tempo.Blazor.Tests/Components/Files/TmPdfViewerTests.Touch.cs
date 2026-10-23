/// <summary>FZE-9 touch tests of <see cref="TmPdfViewer"/> — text selection, the sticky CTA and the pinch zoom (split of TmPdfViewerTests.cs).</summary>
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.Abstractions.Models;
using Tempo.Blazor.Components.Files;
using Tempo.Blazor.Components.Layout;
using Tempo.Blazor.Components.Toolbar;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.Files;

public partial class TmPdfViewerTests
{
    // ── FZE-9: touch selection raises OnTextSelected ─────────────────────────

    [Fact]
    public async Task TouchSelection_RaisesOnTextSelected()
    {
        PdfTextSelection? received = null;
        var cut = Render<TmPdfViewer>(p => p
            .Add(c => c.Url, "https://example.com/test.pdf")
            .Add(c => c.EnableTextSelectionEvents, true)
            .Add(c => c.OnTextSelected, EventCallback.Factory.Create<PdfTextSelection>(this, s => received = s)));

        await cut.InvokeAsync(() => cut.Instance.OnTextSelectionChanged("selected words", 2, [0.1, 0.2, 0.3, 0.02]));

        received.Should().NotBeNull("the JS text layer reports the touch selection through OnTextSelected");
        received!.Text.Should().Be("selected words");
        received.Page.Should().Be(2);
        received.Rects.Should().HaveCount(1);
    }

    // ── FZE-9: a pending selection shows the sticky comment CTA on touch layouts ──

    [Fact]
    public async Task Mobile_PendingSelection_ShowsStickyCommentCta()
    {
        var cut = RenderInLayout(TmLayoutMode.Mobile, p => p
            .Add(c => c.EnableAnnotations, true)
            .Add(c => c.ShowMobileActionBar, false));

        cut.FindAll("[data-testid='pdf-viewer-selection-cta']").Should().BeEmpty(
            "no CTA before a selection exists");

        await cut.InvokeAsync(() => cut.Instance.OnTextSelectionChanged("quoted text", 1, [0.1, 0.2, 0.3, 0.02]));

        var cta = cut.Find("[data-testid='pdf-viewer-selection-cta']");
        cta.Should().NotBeNull("a pending selection on a touch layout shows the sticky comment CTA");
        cta.TextContent.Should().Contain("Comment on selected text");

        // The CTA opens the comments sheet, which hosts the selection prompt + composer.
        cut.Find("[data-testid='pdf-viewer-selection-cta-comment']").Click();
        cut.Find(".tm-drawer--bottom").Should().NotBeNull("the CTA opens the comments bottom sheet");
        cut.Find(".tm-drawer--bottom [data-testid='pdf-annotation-new']").Should().NotBeNull(
            "the sheet carries the pending selection's composer");

        // The dismiss control clears the pending selection and the bar.
        cut.Find("[data-testid='pdf-viewer-selection-cta-dismiss']").Click();
        cut.FindAll("[data-testid='pdf-viewer-selection-cta']").Should().BeEmpty();
    }

    [Fact]
    public async Task Tablet_PendingSelection_ShowsStickyCommentCta()
    {
        var cut = RenderInLayout(TmLayoutMode.Tablet, p => p.Add(c => c.EnableAnnotations, true));

        cut.FindAll("[data-testid='pdf-viewer-selection-cta']").Should().BeEmpty();

        await cut.InvokeAsync(() => cut.Instance.OnTextSelectionChanged("quoted text", 1, [0.1, 0.2, 0.3, 0.02]));

        cut.Find("[data-testid='pdf-viewer-selection-cta']").Should().NotBeNull(
            "the sticky CTA exists on a tablet layout too (the comments sheet starts closed)");
    }

    [Fact]
    public async Task Desktop_PendingSelection_ShowsNoStickyCta()
    {
        var cut = RenderInLayout(TmLayoutMode.Desktop, p => p.Add(c => c.EnableAnnotations, true));

        await cut.InvokeAsync(() => cut.Instance.OnTextSelectionChanged("quoted text", 1, [0.1, 0.2, 0.3, 0.02]));

        cut.FindAll("[data-testid='pdf-viewer-selection-cta']").Should().BeEmpty(
            "the docked desktop panel already hosts the selection prompt — no sticky CTA");
    }

    // ── FZE-9: pinch zoom commits a clamped scale and the Custom zoom mode ───

    [Fact]
    public async Task OnPinchZoomEnded_SetsScaleWithinBounds_AndZoomModeCustom()
    {
        var scales = new List<double>();
        var modes = new List<PdfZoomMode>();
        var cut = Render<TmPdfViewer>(p => p
            .Add(c => c.Url, "https://example.com/test.pdf")
            .Add(c => c.ZoomMode, PdfZoomMode.FitWidth)
            .Add(c => c.ScaleChanged, EventCallback.Factory.Create<double>(this, scales.Add))
            .Add(c => c.ZoomModeChanged, EventCallback.Factory.Create<PdfZoomMode>(this, modes.Add)));

        cut.InvokeAsync(() => cut.Instance.OnPdfLoaded(10));

        // An out-of-range gesture result is clamped into the documented bounds.
        await cut.InvokeAsync(() => cut.Instance.OnPinchZoomEnded(99.0));
        scales.Should().Contain(5.0, "the pinch scale is clamped to the absolute maximum");
        modes.Should().Contain(PdfZoomMode.Custom, "a pinch hands the zoom mode to the user (Custom)");
        InvocationsOf(this, "tmPdfViewer.clearFit").Should().NotBeEmpty(
            "the abandoned fit observer is detached when the pinch takes over");
        InvocationsOf(this, "tmPdfViewer.setScale").Last().Arguments[1].Should().Be(5.0);

        // And the same on the lower bound.
        await cut.InvokeAsync(() => cut.Instance.OnPinchZoomEnded(0.001));
        scales.Should().Contain(0.1, "the pinch scale is clamped to the absolute minimum");
        InvocationsOf(this, "tmPdfViewer.setScale").Last().Arguments[1].Should().Be(0.1);
    }

    [Fact]
    public async Task OnPinchZoomEnded_IgnoresInvalidAndUnchangedScale()
    {
        var scales = new List<double>();
        var cut = Render<TmPdfViewer>(p => p
            .Add(c => c.Url, "https://example.com/test.pdf")
            .Add(c => c.Scale, 1.0)
            .Add(c => c.ScaleChanged, EventCallback.Factory.Create<double>(this, scales.Add)));

        await cut.InvokeAsync(() => cut.Instance.OnPinchZoomEnded(double.NaN));
        await cut.InvokeAsync(() => cut.Instance.OnPinchZoomEnded(1.0));

        scales.Should().BeEmpty("a NaN or unchanged gesture scale commits nothing");
        InvocationsOf(this, "tmPdfViewer.setScale").Should().BeEmpty();
    }

    // ── FZE-9 review (M2): the pinch start is an internal host notification ──

    [Fact]
    public void OnPinchStarted_RaisesPinchStarted()
    {
        var raised = 0;
        var cut = Render<TmPdfViewer>(p => p.Add(c => c.Url, "https://example.com/test.pdf"));

        cut.Instance.PinchStarted += () => raised++;
        cut.InvokeAsync(() => cut.Instance.OnPinchStarted());

        raised.Should().Be(1, "the JS pinch onStart reports through the internal PinchStarted event");
    }
}
