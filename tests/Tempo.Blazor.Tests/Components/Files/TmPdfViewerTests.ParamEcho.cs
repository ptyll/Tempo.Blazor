using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Components.Files;

namespace Tempo.Blazor.Tests.Components.Files;

// CF1-family "parameter vs. state" echo tests of TmPdfViewer: every two-way shadow parameter
// reacts to VALUE CHANGES against the last PROCESSED parameter — never against the live
// internal state, which a parent re-render's unchanged re-delivery would otherwise clobber
// (the user's choice snaps back on the next parent render).
public partial class TmPdfViewerTests
{
    // ── CF1-family: ViewMode (the demo "All Features Combined" bug) ──────────

    [Fact]
    public void UnboundHost_ParentRerenderAfterContinuousToggle_StaysContinuous()
    {
        // No ViewMode / ViewModeChanged binding — the demo "All Features Combined" setup:
        // the toolbar toggle switches the viewer's OWN state to Continuous, and the next
        // parent re-render re-delivers the unchanged ViewMode=SinglePage parameter. The old
        // `ViewMode != _viewMode` comparison read that delivery as a host-driven switch and
        // snapped the viewer back to SinglePage on every parent render.
        var cut = Render<TmPdfViewer>(p => p.Add(x => x.Url, "https://example.com/test.pdf")
                     .Add(x => x.ShowToolbar, true)
                     .Add(x => x.ShowViewModeToggle, true));

        cut.InvokeAsync(() => cut.Instance.OnPdfLoaded(5));
        cut.Render();

        // The user switches to Continuous through the toolbar segment.
        cut.FindAll(".tm-pdf-viewer__view-mode input[type='radio']").Last().Change(true);
        cut.Find(".tm-pdf-viewer__continuous-wrap").Should().NotBeNull(
            "precondition: the toggle switched the viewer to Continuous");

        // The parent re-renders with UNCHANGED parameters.
        cut.Render();

        cut.Find(".tm-pdf-viewer__continuous-wrap").Should().NotBeNull(
            "an unchanged re-delivered ViewMode parameter must not switch the presentation back");
        cut.FindAll(".tm-pdf-viewer__canvas-wrap").Should().BeEmpty();
    }

    [Fact]
    public void BoundHost_UserToggleEchoThenHostSwitchBack_FollowsTheHost()
    {
        // The two-way binding must keep working: the host echoes the raised value (no second
        // switch — the wrap never rebuilds for it), and a host-driven change is still
        // authoritative.
        PdfViewMode host = PdfViewMode.SinglePage;
        var cut = Render<TmPdfViewer>(p => p.Add(x => x.Url, "https://example.com/test.pdf")
                     .Add(x => x.ShowToolbar, true)
                     .Add(x => x.ShowViewModeToggle, true)
                     .Add(x => x.ViewMode, PdfViewMode.SinglePage)
                     .Add(x => x.ViewModeChanged, EventCallback.Factory.Create<PdfViewMode>(this, m => host = m)));

        cut.InvokeAsync(() => cut.Instance.OnPdfLoaded(5));
        cut.Render();

        cut.FindAll(".tm-pdf-viewer__view-mode input[type='radio']").Last().Change(true);
        host.Should().Be(PdfViewMode.Continuous, "the viewer raises the user's switch");
        cut.Render(parameters => parameters.Add(p => p.ViewMode, host)); // the binding's echo
        cut.Find(".tm-pdf-viewer__continuous-wrap").Should().NotBeNull("the echo keeps Continuous");

        // A delivery the viewer did NOT raise is a host-driven switch and wins.
        cut.Render(parameters => parameters.Add(p => p.ViewMode, PdfViewMode.SinglePage));
        cut.Find(".tm-pdf-viewer__canvas-wrap").Should().NotBeNull(
            "a host-driven ViewMode change must still switch the presentation");
    }

    // ── CF1-family: Scale ────────────────────────────────────────────────────

    [Fact]
    public async Task UnboundHost_ParentRerenderAfterZoom_StaysZoomed()
    {
        // The zoom path (SetScaleAsync, the ± buttons) updates the viewer's own scale and
        // raises ScaleChanged; an unbound host re-delivers the unchanged Scale=1.0 on the
        // next parent render. The old `Scale != _scale` comparison zoomed BACK to 100 %.
        var cut = Render<TmPdfViewer>(p => p.Add(x => x.Url, "https://example.com/test.pdf")
                     .Add(x => x.ShowToolbar, true));

        cut.InvokeAsync(() => cut.Instance.OnPdfLoaded(5));
        cut.Render();

        await cut.InvokeAsync(() => cut.Instance.SetScaleAsync(1.5));
        JSInterop.VerifyInvoke("tmPdfViewer.setScale", 1).Single().Arguments[1].Should().Be(1.5);

        // The parent re-renders with the unchanged Scale parameter.
        cut.Render();

        JSInterop.Invocations.Where(i => i.Identifier == "tmPdfViewer.setScale").Should().ContainSingle(
                "the unchanged re-delivered Scale parameter must not zoom the viewer back")
            .Which.Arguments[1].Should().Be(1.5);

        // A parameter that REALLY changed still applies (the host-driven path is intact).
        cut.Render(parameters => parameters.Add(p => p.Scale, 2.0));
        JSInterop.VerifyInvoke("tmPdfViewer.setScale", 2).Last().Arguments[1].Should().Be(2.0,
            "a genuine host-driven scale change still applies");
    }

    [Fact]
    public async Task HostDeliversInvalidScaleThenCorrection_StillAppliesTheCorrection()
    {
        // N1: the last-seen-parameter tracker advances on EVERY changed delivery — even an
        // out-of-range one that cannot be applied. The old guard (`Scale is > 0 and <= 5 && …`)
        // left the tracker on the stale seed, so 1.0 → user 1.5 → host 6 → host 1.0 matched
        // the seed and the host's correction back to 1.0 never applied.
        var cut = Render<TmPdfViewer>(p => p.Add(x => x.Url, "https://example.com/test.pdf")
                     .Add(x => x.ShowToolbar, true));

        cut.InvokeAsync(() => cut.Instance.OnPdfLoaded(5));
        cut.Render();

        // The user zooms in; the unbound host's Scale parameter still carries 1.0.
        await cut.InvokeAsync(() => cut.Instance.SetScaleAsync(1.5));
        JSInterop.VerifyInvoke("tmPdfViewer.setScale", 1).Single().Arguments[1].Should().Be(1.5);

        // The host delivers an out-of-range scale (ignored — cannot be applied) and then
        // corrects back to 1.0.
        cut.Render(parameters => parameters.Add(p => p.Scale, 6.0));
        cut.Render(parameters => parameters.Add(p => p.Scale, 1.0));

        JSInterop.VerifyInvoke("tmPdfViewer.setScale", 2).Last().Arguments[1].Should().Be(1.0,
            "the invalid delivery must advance the tracker so the later 1.0 is a genuine change");

        // The NaN variant of the same contract: `Math.Abs(NaN − x)` is NaN and every
        // relational comparison with NaN reads false, so the old guard never advanced the
        // tracker on a NaN delivery — the correction back to the previously seen 1.0 was
        // swallowed. The NaN-safe condition tracks the NaN delivery too.
        await cut.InvokeAsync(() => cut.Instance.SetScaleAsync(1.5));
        cut.Render(parameters => parameters.Add(p => p.Scale, double.NaN));
        cut.Render(parameters => parameters.Add(p => p.Scale, 1.0));

        JSInterop.VerifyInvoke("tmPdfViewer.setScale", 4).Last().Arguments[1].Should().Be(1.0,
            "a NaN delivery must advance the tracker so the later 1.0 is a genuine change");
    }

    [Fact]
    public async Task BoundHost_FitScaleEcho_DoesNotReapplyZoom()
    {
        // A fit recompute arrives from JS (a container resize) and raises ScaleChanged; the
        // two-way host's echo re-delivers Scale == the computed value. That echo is a no-op
        // side-effect-wise: exactly ONE setScale for the fit value, never a second zoom from
        // the echo delivery.
        double hostScale = 1.0;
        var cut = Render<TmPdfViewer>(p => p.Add(x => x.Url, "https://example.com/test.pdf")
                     .Add(x => x.ShowToolbar, true)
                     .Add(x => x.ZoomMode, PdfZoomMode.FitWidth)
                     .Add(x => x.Scale, hostScale)
                     .Add(x => x.ScaleChanged, EventCallback.Factory.Create<double>(this, s => hostScale = s)));

        cut.InvokeAsync(() => cut.Instance.OnPdfLoaded(5));
        cut.Render();

        await cut.InvokeAsync(() => cut.Instance.OnFitScaleComputed(1.37));
        hostScale.Should().Be(1.37, "the viewer raises the computed fit scale");

        cut.Render(parameters => parameters.Add(p => p.Scale, hostScale)); // the binding's echo

        JSInterop.Invocations.Where(i => i.Identifier == "tmPdfViewer.setScale")
            .Should().ContainSingle("the echo of the raised fit scale must not zoom the viewer again")
            .Which.Arguments[1].Should().Be(1.37);
    }

    // ── CF1-family: ZoomMode ─────────────────────────────────────────────────

    [Fact]
    public void UnboundHost_ParentRerenderAfterZoomIn_FitModeStaysAbandoned()
    {
        // The host pins ZoomMode=FitWidth and binds nothing. A zoom button abandons the fit
        // mode (ZoomModeChanged reports Custom, the JS observer detaches); the next parent
        // re-render re-delivers the unchanged ZoomMode=FitWidth — the old `ZoomMode !=
        // _zoomMode` comparison re-applied the fit the user had just abandoned.
        var cut = Render<TmPdfViewer>(p => p.Add(x => x.Url, "https://example.com/test.pdf")
                     .Add(x => x.ShowToolbar, true)
                     .Add(x => x.ZoomMode, PdfZoomMode.FitWidth));

        cut.InvokeAsync(() => cut.Instance.OnPdfLoaded(5));
        cut.Render();
        cut.WaitForAssertion(() => InvocationsOf(this, "tmPdfViewer.fitScale").Should().NotBeEmpty());
        var fitCount = InvocationsOf(this, "tmPdfViewer.fitScale").Count;

        // The user zooms in: the fit mode is abandoned (raised — no delegate bound).
        cut.FindAll(".tm-pdf-viewer__btn")
            .First(b => b.GetAttribute("aria-label")?.Contains("Zoom in", StringComparison.Ordinal) == true)
            .Click();

        // The parent re-renders with the unchanged ZoomMode=FitWidth.
        cut.Render();

        InvocationsOf(this, "tmPdfViewer.fitScale").Should().HaveCount(fitCount,
            "the unchanged re-delivered ZoomMode must not re-apply the fit the user abandoned");

        // A parameter that REALLY changed still applies.
        cut.Render(parameters => parameters.Add(p => p.ZoomMode, PdfZoomMode.FitPage));
        cut.WaitForAssertion(() =>
            InvocationsOf(this, "tmPdfViewer.fitScale").Last().Arguments[2].Should().Be("fit-page",
                "a genuine host-driven zoom-mode change still applies"));
    }

    [Fact]
    public async Task UnboundHost_PinchThenParentRerender_KeepsScaleAndCustom()
    {
        // A pinch is a user zoom: ZoomModeChanged reports Custom (nothing bound here) and the
        // JS fit observer detaches. The unbound host re-delivers ZoomMode=FitWidth on the next
        // parent render — the abandoned fit must NOT re-apply and the pinch-committed scale
        // must survive (the old `ZoomMode != _zoomMode` comparison re-applied the fit).
        var cut = Render<TmPdfViewer>(p => p.Add(x => x.Url, "https://example.com/test.pdf")
                     .Add(x => x.ShowToolbar, true)
                     .Add(x => x.ZoomMode, PdfZoomMode.FitWidth));

        cut.InvokeAsync(() => cut.Instance.OnPdfLoaded(5));
        cut.Render();
        cut.WaitForAssertion(() => InvocationsOf(this, "tmPdfViewer.fitScale").Should().NotBeEmpty());
        var fitCount = InvocationsOf(this, "tmPdfViewer.fitScale").Count;

        await cut.InvokeAsync(() => cut.Instance.OnPinchZoomEnded(1.8));

        // The parent re-renders with the unchanged ZoomMode=FitWidth.
        cut.Render();

        InvocationsOf(this, "tmPdfViewer.fitScale").Should().HaveCount(fitCount,
            "the unchanged re-delivered ZoomMode must not re-apply the fit the pinch abandoned");
        InvocationsOf(this, "tmPdfViewer.setScale").Last().Arguments[1].Should().Be(1.8,
            "the pinch-committed scale survives the parent re-render");
    }

    [Fact]
    public async Task SetZoomModeAsync_AppliesFitModeRepeatIsNoOpAndCustomDetachesTheFit()
    {
        // N2: the public zoom-mode path mirrors the toolbar's zoom dropdown. FitWidth computes
        // the scale from the container (one fitScale call) and reports the mode through
        // ZoomModeChanged; repeating the active mode is a no-op; Custom detaches the JS fit
        // observer without touching the current scale.
        PdfZoomMode hostMode = PdfZoomMode.Custom;
        var cut = Render<TmPdfViewer>(p => p.Add(x => x.Url, "https://example.com/test.pdf")
                     .Add(x => x.ShowToolbar, true)
                     .Add(x => x.ZoomMode, PdfZoomMode.Custom)
                     .Add(x => x.ZoomModeChanged, EventCallback.Factory.Create<PdfZoomMode>(this, m => hostMode = m)));

        cut.InvokeAsync(() => cut.Instance.OnPdfLoaded(5));
        cut.Render();

        await cut.InvokeAsync(() => cut.Instance.SetZoomModeAsync(PdfZoomMode.FitWidth));

        hostMode.Should().Be(PdfZoomMode.FitWidth, "the viewer reports the host-driven mode");
        InvocationsOf(this, "tmPdfViewer.fitScale").Should().ContainSingle()
            .Which.Arguments[2].Should().Be("fit-width");

        var fitCount = InvocationsOf(this, "tmPdfViewer.fitScale").Count;
        await cut.InvokeAsync(() => cut.Instance.SetZoomModeAsync(PdfZoomMode.FitWidth));
        InvocationsOf(this, "tmPdfViewer.fitScale").Should().HaveCount(fitCount,
            "repeating the active zoom mode must not recompute the fit");

        await cut.InvokeAsync(() => cut.Instance.SetZoomModeAsync(PdfZoomMode.Custom));
        InvocationsOf(this, "tmPdfViewer.clearFit").Should().ContainSingle(
            "a host-driven switch to Custom must detach the JS fit observer");
        InvocationsOf(this, "tmPdfViewer.setScale").Should().BeEmpty(
            "switching the mode to Custom keeps the current scale");
    }

    // ── CF-next: the EFFECTIVE zoom mode decides — Auto on desktop detaches the fit ──
    //
    // On a desktop/tablet layout PdfZoomMode.Auto resolves to the bound scale (Custom
    // behavior). A FitWidth → Auto transition therefore has NO active fit anymore: the
    // JS ResizeObserver must detach exactly like an explicit Custom. The old
    // `mode is Custom` branches read the raw parameter and kept the observer running —
    // every later container resize recomputed a scale no fit mode owned.

    [Fact]
    public async Task SetZoomModeAsync_AutoOnDesktopAfterFitWidth_ClearsFitExactlyOnce()
    {
        var cut = Render<TmPdfViewer>(p => p.Add(x => x.Url, "https://example.com/test.pdf")
                     .Add(x => x.ShowToolbar, true)
                     .Add(x => x.ZoomMode, PdfZoomMode.FitWidth));

        cut.InvokeAsync(() => cut.Instance.OnPdfLoaded(5));
        cut.Render();
        cut.WaitForAssertion(() => InvocationsOf(this, "tmPdfViewer.fitScale").Should().NotBeEmpty(
            "precondition: the fit mode computed its scale after the document loaded"));

        await cut.InvokeAsync(() => cut.Instance.SetZoomModeAsync(PdfZoomMode.Auto));

        InvocationsOf(this, "tmPdfViewer.clearFit").Should().ContainSingle(
            "Auto on a desktop layout IS the bound scale — the fit observer must detach exactly once");
        InvocationsOf(this, "tmPdfViewer.fitScale").Should().ContainSingle(
            "Auto on a desktop layout must not start (or keep) a fit computation");
    }

    [Fact]
    public async Task ZoomModeParam_AutoOnDesktopAfterFitWidth_ClearsFitExactlyOnce()
    {
        // The same transition delivered through the parameter path (a bound host sets
        // ZoomMode=Auto): the effective-mode branch must detach the fit observer here too.
        var cut = Render<TmPdfViewer>(p => p.Add(x => x.Url, "https://example.com/test.pdf")
                     .Add(x => x.ShowToolbar, true)
                     .Add(x => x.ZoomMode, PdfZoomMode.FitWidth));

        cut.InvokeAsync(() => cut.Instance.OnPdfLoaded(5));
        cut.Render();
        cut.WaitForAssertion(() => InvocationsOf(this, "tmPdfViewer.fitScale").Should().NotBeEmpty(
            "precondition: the fit mode computed its scale after the document loaded"));

        cut.Render(parameters => parameters.Add(p => p.ZoomMode, PdfZoomMode.Auto));

        InvocationsOf(this, "tmPdfViewer.clearFit").Should().ContainSingle(
            "a host-driven ZoomMode=Auto on a desktop layout must detach the fit observer exactly once");
        InvocationsOf(this, "tmPdfViewer.fitScale").Should().ContainSingle(
            "Auto on a desktop layout must not re-apply the fit the host replaced");
    }

    // ── CF-next: the public zoom API refreshes the toolbar chrome ────────────

    [Fact]
    public async Task SetZoomModeAsync_FitWidth_UpdatesZoomTriggerTextWithoutExplicitRender()
    {
        // The zoom trigger renders from the shadow state the API call just moved — the
        // call must refresh the render tree itself. Without the trailing StateHasChanged
        // the trigger kept the stale percentage until the HOST happened to re-render.
        var cut = Render<TmPdfViewer>(p => p.Add(x => x.Url, "https://example.com/test.pdf")
                     .Add(x => x.ShowToolbar, true)
                     .Add(x => x.ZoomMode, PdfZoomMode.Custom));

        cut.InvokeAsync(() => cut.Instance.OnPdfLoaded(5));
        cut.Render();
        cut.Find(".tm-pdf-viewer__zoom .tm-dropdown-trigger").TextContent.Should().Contain(
            "100%", "precondition: the trigger shows the percentage in a Custom mode");

        await cut.InvokeAsync(() => cut.Instance.SetZoomModeAsync(PdfZoomMode.FitWidth));

        cut.Find(".tm-pdf-viewer__zoom .tm-dropdown-trigger").TextContent.Should().Contain(
            "Fit width", "the trigger follows the activated fit mode without an explicit cut.Render()");
    }

    [Fact]
    public async Task SetZoomModeAsync_FitWidth_DefersTheFitUntilTheDocumentIsLoaded()
    {
        // Nothing to fit before the document exists — the request is recorded and the
        // computation runs once OnPdfLoaded reports the page count.
        var cut = Render<TmPdfViewer>(p => p.Add(x => x.Url, "https://example.com/test.pdf")
                     .Add(x => x.ShowToolbar, true)
                     .Add(x => x.ZoomMode, PdfZoomMode.Custom));

        await cut.InvokeAsync(() => cut.Instance.SetZoomModeAsync(PdfZoomMode.FitWidth));

        InvocationsOf(this, "tmPdfViewer.fitScale").Should().BeEmpty(
            "no fit computation may run before the document has loaded");

        cut.InvokeAsync(() => cut.Instance.OnPdfLoaded(5));
        cut.WaitForAssertion(() => InvocationsOf(this, "tmPdfViewer.fitScale")
            .Should().ContainSingle("the deferred fit computes once the page count is known")
            .Which.Arguments[2].Should().Be("fit-width"));
    }

    [Fact]
    public async Task SetZoomModeAsync_CustomWhileLoading_DoesNotThrow()
    {
        // Hold the JS init promise open so the call really lands while the document is
        // still loading (the detached-fit branch of the core path runs in this state too).
        JSInterop.SetupVoid("tmPdfViewer.init", _ => true);
        var cut = Render<TmPdfViewer>(p => p.Add(x => x.Url, "https://example.com/test.pdf")
                     .Add(x => x.ShowToolbar, true)
                     .Add(x => x.ZoomMode, PdfZoomMode.FitWidth));

        Func<Task> act = () => cut.InvokeAsync(() => cut.Instance.SetZoomModeAsync(PdfZoomMode.Custom));

        await act.Should().NotThrowAsync("a zoom-mode call during loading is legal and must be a safe no-op");
    }

    // ── CF1: a fresh document re-seeds the trackers ──────────────────────────

    [Fact]
    public void NewUrl_ReseedsTrackers_UnchangedRerenderIsNoOp()
    {
        // CF1: a fresh document (a Url change) re-seeds every shadow-parameter tracker from
        // the delivered parameters and starts the echo FIFO EMPTY — nothing from the OLD
        // document may echo into the new one, and the first unchanged re-render after the
        // switch must be a no-op (no navigation, no zoom, no view-mode switch).
        int hostPage = 1;
        var cut = Render<TmPdfViewer>(p => p.Add(x => x.Url, "https://example.com/a.pdf")
                     .Add(x => x.ShowToolbar, true)
                     .Add(x => x.ViewMode, PdfViewMode.Continuous)
                     .Add(x => x.Page, hostPage)
                     .Add(x => x.PageChanged, EventCallback.Factory.Create<int>(this, page => hostPage = page)));

        cut.InvokeAsync(() => cut.Instance.OnPdfLoaded(5));

        // The OLD document's tracker reports page 4; the two-way host picks it up.
        cut.InvokeAsync(() => cut.Instance.OnVisiblePageChanged(4));
        cut.WaitForAssertion(() => hostPage.Should().Be(4));

        // The document CHANGES: the trackers re-seed from the delivered parameters and the
        // old echo queue is dropped.
        cut.Render(parameters => parameters.Add(p => p.Url, "https://example.com/b.pdf"));
        cut.InvokeAsync(() => cut.Instance.OnPdfLoaded(5));

        // The fresh document's tracker reports page 3; the host's echo of 3 is consumed
        // without scrolling, and the UNCHANGED re-render (Page=3, everything the same)
        // is a no-op.
        cut.InvokeAsync(() => cut.Instance.OnVisiblePageChanged(3));
        cut.WaitForAssertion(() => hostPage.Should().Be(3));
        cut.Render(parameters => parameters.Add(p => p.Page, hostPage));
        cut.Render();

        InvocationsOf(this, "tmPdfViewer.scrollToPage").Should().BeEmpty(
            "tracker reports and their binding echoes never scroll — only a host navigation does");

        // A genuine host navigation on the fresh document still drives the wrap.
        cut.Render(parameters => parameters.Add(p => p.Page, 2));
        InvocationsOf(this, "tmPdfViewer.scrollToPage").Should().ContainSingle()
            .Which.Arguments[1].Should().Be(2,
                "a delivery the viewer never raised is a genuine host navigation and scrolls the wrap");
    }
}
