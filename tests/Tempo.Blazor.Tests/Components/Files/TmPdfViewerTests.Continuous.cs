/// <summary>View-mode and continuous-scroll tests of <see cref="TmPdfViewer"/> (split of TmPdfViewerTests.cs).</summary>
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
    // ── PDF-49: ViewMode=Continuous zobrazí continuous wrap místo canvas wrap

    [Fact]
    public void Render_WithViewModeContinuous_DisplaysContinuousWrap()
    {
        var cut = Render<TmPdfViewer>(parameters =>
            parameters.Add(p => p.Url, "https://example.com/test.pdf")
                      .Add(p => p.ViewMode, PdfViewMode.Continuous));

        cut.Find(".tm-pdf-viewer__continuous-wrap").Should().NotBeNull();
    }

    // ── A11y: the continuous wrap is a labelled, keyboard-reachable region ──

    [Fact]
    public void Render_Continuous_WrapCarriesRegionSemantics()
    {
        var cut = Render<TmPdfViewer>(parameters =>
            parameters.Add(p => p.Url, "https://example.com/test.pdf")
                      .Add(p => p.ViewMode, PdfViewMode.Continuous));

        var wrap = cut.Find(".tm-pdf-viewer__continuous-wrap");
        wrap.GetAttribute("role").Should().Be("region");
        wrap.GetAttribute("tabindex").Should().Be("0", "keyboard users must be able to scroll the pages");
        wrap.GetAttribute("aria-label").Should().Be("Pages");
    }

    // ── PDF-50: ViewMode=SinglePage zobrazí canvas wrap ─────────────────────

    [Fact]
    public void Render_WithViewModeSinglePage_DisplaysCanvasWrap()
    {
        var cut = Render<TmPdfViewer>(parameters =>
            parameters.Add(p => p.Url, "https://example.com/test.pdf")
                      .Add(p => p.ViewMode, PdfViewMode.SinglePage));

        cut.Find(".tm-pdf-viewer__canvas-wrap").Should().NotBeNull();
    }

    // ── PDF-51: klik na view mode segment změní ViewMode ───────────────────

    [Fact]
    public void ClickViewModeToggle_ChangesViewMode()
    {
        PdfViewMode capturedMode = PdfViewMode.SinglePage;
        var cut = Render<TmPdfViewer>(parameters =>
            parameters.Add(p => p.Url, "https://example.com/test.pdf")
                      .Add(p => p.ShowToolbar, true)
                      .Add(p => p.ShowViewModeToggle, true)
                      .Add(p => p.ViewMode, PdfViewMode.SinglePage)
                      .Add(p => p.ViewModeChanged, EventCallback.Factory.Create<PdfViewMode>(this, m => capturedMode = m)));

        cut.InvokeAsync(() => cut.Instance.OnPdfLoaded(5));
        cut.Render();

        var continuousInput = cut.FindAll(".tm-pdf-viewer__view-mode input[type='radio']")
            .Last();
        continuousInput.Change(true);

        capturedMode.Should().Be(PdfViewMode.Continuous);
    }

    // ── W0: view-mode segment má nativní radio sémantiku (šipky, aria-checked) ──

    [Fact]
    public void Render_ViewModeButtons_HaveRadioSemantics()
    {
        var cut = Render<TmPdfViewer>(parameters =>
            parameters.Add(p => p.Url, "https://example.com/test.pdf")
                      .Add(p => p.ShowToolbar, true)
                      .Add(p => p.ShowViewModeToggle, true));

        cut.InvokeAsync(() => cut.Instance.OnPdfLoaded(5));
        cut.Render();

        var group = cut.Find(".tm-pdf-viewer__view-mode");
        group.GetAttribute("role").Should().Be("radiogroup");

        var inputs = cut.FindAll(".tm-pdf-viewer__view-mode input[type='radio']");
        inputs.Should().HaveCount(2);
        inputs[0].HasAttribute("checked").Should().BeTrue("Single page is the default view mode");
        inputs[1].HasAttribute("checked").Should().BeFalse();
    }

    // ── View mode segment: radiogroup semantics with native radios ──────────

    [Fact]
    public void ViewModeSegment_HasRadioGroupSemantics_AndArrowKeys()
    {
        PdfViewMode capturedMode = PdfViewMode.SinglePage;
        var cut = Render<TmPdfViewer>(parameters =>
            parameters.Add(p => p.Url, "https://example.com/test.pdf")
                      .Add(p => p.ShowToolbar, true)
                      .Add(p => p.ShowViewModeToggle, true)
                      .Add(p => p.ViewModeChanged, EventCallback.Factory.Create<PdfViewMode>(this, m => capturedMode = m)));

        cut.InvokeAsync(() => cut.Instance.OnPdfLoaded(5));
        cut.Render();

        var group = cut.Find(".tm-pdf-viewer__view-mode");
        group.GetAttribute("role").Should().Be("radiogroup");
        group.GetAttribute("aria-label").Should().NotBeNullOrEmpty();

        // Native radio inputs deliver arrow-key selection per the HTML spec (the
        // keyboard-activation convention: never emulate what the browser owns).
        var inputs = cut.FindAll(".tm-pdf-viewer__view-mode input[type='radio']");
        inputs.Should().HaveCount(2);
        inputs[1].Change(true);

        capturedMode.Should().Be(PdfViewMode.Continuous);
    }

    // ── Next page in continuous scrolls the wrap, never an invisible canvas ──

    [Fact]
    public void Continuous_NextPage_InvokesScrollToPage()
    {
        int capturedPage = 1;
        var cut = Render<TmPdfViewer>(p => ContinuousDefaults(p).Add(
            p => p.PageChanged, EventCallback.Factory.Create<int>(this, page => capturedPage = page)));

        cut.InvokeAsync(() => cut.Instance.OnPdfLoaded(10));
        cut.Render();

        var nextBtn = cut.FindAll(".tm-pdf-viewer__btn")
                         .First(b => b.GetAttribute("aria-label")?.Contains("Next") == true);
        nextBtn.Click();

        capturedPage.Should().Be(2);
        var scroll = JSInterop.VerifyInvoke("tmPdfViewer.scrollToPage", 1).Single();
        scroll.Arguments[1].Should().Be(2, "the continuous wrap must scroll to the requested page");
        InvocationsOf(this, "tmPdfViewer.renderPage").Should().BeEmpty(
            "continuous navigation must not render into an invisible single-page canvas");
    }

    // ── The page input in continuous scrolls the wrap ───────────────────────

    [Fact]
    public void Continuous_PageInput_InvokesScrollToPage()
    {
        int capturedPage = 1;
        var cut = Render<TmPdfViewer>(p => ContinuousDefaults(p).Add(
            p => p.PageChanged, EventCallback.Factory.Create<int>(this, page => capturedPage = page)));

        cut.InvokeAsync(() => cut.Instance.OnPdfLoaded(10));
        cut.Render();

        var input = cut.Find(".tm-pdf-viewer__page-input");
        input.Input("6");
        input.KeyDown("Enter");

        capturedPage.Should().Be(6);
        var scroll = JSInterop.VerifyInvoke("tmPdfViewer.scrollToPage", 1).Single();
        scroll.Arguments[1].Should().Be(6);
        InvocationsOf(this, "tmPdfViewer.renderPage").Should().BeEmpty();
    }

    // ── A thumbnail click in continuous scrolls the wrap ────────────────────

    [Fact]
    public async Task Continuous_ThumbnailClick_ScrollsToPage()
    {
        int capturedPage = 1;
        var cut = Render<TmPdfViewer>(p => ContinuousDefaults(p).Add(
            p => p.PageChanged, EventCallback.Factory.Create<int>(this, page => capturedPage = page)));

        cut.InvokeAsync(() => cut.Instance.OnPdfLoaded(10));
        cut.Render();

        await cut.InvokeAsync(() => cut.Instance.OnThumbnailClicked(5));

        capturedPage.Should().Be(5);
        var scroll = JSInterop.VerifyInvoke("tmPdfViewer.scrollToPage", 1).Single();
        scroll.Arguments[1].Should().Be(5);
        InvocationsOf(this, "tmPdfViewer.renderPage").Should().BeEmpty();
    }

    // ── Scrolling reports the visible page through the binding ──────────────

    [Fact]
    public void Continuous_OnVisiblePageChanged_UpdatesPageAndRaisesPageChanged()
    {
        int capturedPage = 1;
        var cut = Render<TmPdfViewer>(p => ContinuousDefaults(p).Add(p => p.ShowThumbnails, true)
                                         .Add(p => p.PageChanged, EventCallback.Factory.Create<int>(this, page => capturedPage = page)));

        cut.InvokeAsync(() => cut.Instance.OnPdfLoaded(10));
        cut.Render();

        cut.InvokeAsync(() => cut.Instance.OnVisiblePageChanged(7));

        capturedPage.Should().Be(7);

        // The host rebinds Page (what @bind- does) — the parameter is authoritative
        // on the next render.
        cut.Render(parameters => parameters.Add(p => p.Page, 7));
        cut.Find(".tm-pdf-viewer__page-input").GetAttribute("value").Should().Be("7");
        // The initial render highlights page 1; the visible-page report re-highlights page 7.
        JSInterop.VerifyInvoke("tmPdfViewer.highlightThumbnail", 2).Last()
            .Arguments[1].Should().Be(7, "the active thumbnail must follow the visible page");
    }

    // ── A search match activates on its own page (scrolled there by JS) ─────

    [Fact]
    public async Task Continuous_Search_ActivatesMatchOnItsPage()
    {
        int capturedPage = 1;
        var cut = Render<TmPdfViewer>(p => ContinuousDefaults(p).Add(p => p.ShowSearch, true)
                                         .Add(p => p.PageChanged, EventCallback.Factory.Create<int>(this, page => capturedPage = page)));

        cut.InvokeAsync(() => cut.Instance.OnPdfLoaded(10));
        cut.Render();

        // FZE-11: the floating card starts closed — open it via the toolbar toggle.
        cut.Find("[data-testid='pdf-search-toggle']").Click();
        var input = cut.Find("[data-testid='pdf-search-input']");
        input.Input("needle");
        input.KeyUp("Enter");
        JSInterop.VerifyInvoke("tmPdfViewer.search", 1);

        // The JS search reports the active match's page; the viewer syncs state
        // and lets the (already scrolled) lazy renderer paint the page.
        await cut.InvokeAsync(() => cut.Instance.OnSearchActiveChanged(2, 4));

        capturedPage.Should().Be(4);

        // The host rebinds Page (what @bind- does) — the parameter is authoritative
        // on the next render.
        cut.Render(parameters => parameters.Add(p => p.Page, 4));
        cut.Find(".tm-pdf-viewer__page-input").GetAttribute("value").Should().Be("4");
        InvocationsOf(this, "tmPdfViewer.renderPage").Should().BeEmpty(
            "the match's page is painted by the lazy continuous renderer, not the single-page canvas");
    }

    // ── FZE-13: annotations render on every mounted page of the continuous column ──

    [Fact]
    public void Continuous_EnableAnnotations_RendersLayerPerMountedPage()
    {
        var cut = Render<TmPdfViewer>(p => ContinuousDefaults(p).Add(p => p.EnableAnnotations, true)
                                         .Add(p => p.Annotations, new List<DocumentCommentThread>
                                         {
                                             TextRangeThread("t3", 3),
                                             TextRangeThread("t7", 7),
                                         }));

        cut.InvokeAsync(() => cut.Instance.OnPdfLoaded(10));
        cut.Render();
        // JS reports the mounted band: pages 3 and 7 (geometry in content coordinates).
        cut.InvokeAsync(() => cut.Instance.OnContinuousPageGeometry(
            [3, 7], [1600.0, 4800.0], [0.0, 0.0], [612.0, 612.0], [792.0, 792.0], 0));
        cut.Render();

        var slots = cut.FindAll(".tm-pdf-viewer__continuous-overlays .tm-pdf-viewer__page-overlay-slot");
        slots.Should().HaveCount(2, "one overlay slot per mounted page");
        var slot3 = slots.First(s => s.GetAttribute("data-page") == "3");
        var slot7 = slots.First(s => s.GetAttribute("data-page") == "7");
        slot3.QuerySelectorAll("[data-testid='pdf-annotation-marker']")
            .Should().ContainSingle("the page-3 thread renders its marker in its page's slot");
        slot7.QuerySelectorAll("[data-testid='pdf-annotation-marker']")
            .Should().ContainSingle("the page-7 thread renders its marker in its page's slot");
        // Same cards contract as the single-page layer: named groups per page.
        slot3.GetAttribute("aria-label").Should().Be("Annotations on page 3");
    }

    [Fact]
    public void Continuous_UnmountedPages_RenderNoMarkers()
    {
        var cut = Render<TmPdfViewer>(p => ContinuousDefaults(p).Add(p => p.EnableAnnotations, true)
                                         .Add(p => p.Annotations, new List<DocumentCommentThread> { TextRangeThread("t7", 7) }));

        cut.InvokeAsync(() => cut.Instance.OnPdfLoaded(10));
        cut.Render();
        // Only pages 1 and 2 sit inside the reported band.
        cut.InvokeAsync(() => cut.Instance.OnContinuousPageGeometry(
            [1, 2], [0.0, 800.0], [0.0, 0.0], [612.0, 612.0], [792.0, 792.0], 0));
        cut.Render();

        cut.FindAll("[data-testid='pdf-annotation-marker']").Should().BeEmpty(
            "a thread on an unmounted (far from the viewport) page renders nothing");
        cut.FindAll(".tm-pdf-viewer__page-overlay-slot").Should().HaveCount(2);
    }

    [Fact]
    public void Continuous_IgnoresAnnotationsOnPagesOutsideTheDocument()
    {
        var cut = Render<TmPdfViewer>(p => ContinuousDefaults(p).Add(p => p.EnableAnnotations, true)
                                         .Add(p => p.Annotations, new List<DocumentCommentThread> { TextRangeThread("t99", 99) }));

        cut.InvokeAsync(() => cut.Instance.OnPdfLoaded(10));
        cut.Render();
        cut.InvokeAsync(() => cut.Instance.OnContinuousPageGeometry(
            [99], [0.0], [0.0], [612.0], [792.0], 0));
        cut.Render();

        cut.FindAll(".tm-pdf-viewer__page-overlay-slot").Should().BeEmpty(
            "a geometry page beyond the loaded document is ignored");
    }

    // ── FZE-13: the PD1 hint is gone — continuous is a first-class annotation mode ──

    [Fact]
    public void Continuous_HintNoLongerRendered()
    {
        var cut = Render<TmPdfViewer>(p => ContinuousDefaults(p).Add(p => p.EnableAnnotations, true));

        cut.InvokeAsync(() => cut.Instance.OnPdfLoaded(10));
        cut.Render();

        cut.FindAll("[data-testid='pdf-annotations-single-page-hint']").Should().BeEmpty(
            "PD1 is superseded: continuous annotations need no explanatory hint");
        cut.FindAll(".tm-pdf-viewer__annotations-hint").Should().BeEmpty();
    }

    // ── FZE-13: selecting a thread scrolls, it does not switch the mode ─────

    [Fact]
    public async Task Continuous_SelectThread_ScrollsToAnchor_StaysContinuous()
    {
        PdfViewMode? capturedMode = null;
        var cut = Render<TmPdfViewer>(p => ContinuousDefaults(p).Add(p => p.EnableAnnotations, true)
                                         .Add(p => p.Annotations, new List<DocumentCommentThread> { TextRangeThread("t1", 3) })
                                         .Add(p => p.ViewModeChanged, EventCallback.Factory.Create<PdfViewMode>(this, m => capturedMode = m)));

        cut.InvokeAsync(() => cut.Instance.OnPdfLoaded(10));
        cut.Render();

        await cut.InvokeAsync(() => cut.Instance.OnThumbnailClicked(6));

        cut.FindAll("[data-testid='pdf-annotation-thread']")
           .First(b => b.GetAttribute("data-thread-id") == "t1")
           .Click();

        capturedMode.Should().BeNull("selecting a thread must stay in Continuous mode (PD1 superseded)");
        // The thumbnail pre-navigation scrolled to page 6 first; the thread select
        // scrolls again, to the thread's page.
        var scroll = JSInterop.VerifyInvoke("tmPdfViewer.scrollToPage", 2).Last();
        scroll.Arguments[1].Should().Be(3, "the wrap scrolls to the thread's page");
        var anchor = JSInterop.VerifyInvoke("tmPdfViewer.scrollToAnchor", 1).Single();
        anchor.Arguments[1].Should().Be(3);
        anchor.Arguments[2].Should().Be(0.2, "the anchor point (the thread's Y) centers in the viewport");
        InvocationsOf(this, "tmPdfViewer.renderPage").Should().BeEmpty(
            "the thread's page is painted by the lazy renderer, not the single-page canvas");
    }

    // ── FZE-13: a text selection on a mounted page drafts on that page ──────

    [Fact]
    public async Task Continuous_TextSelection_RaisesDraftOnCorrectPage()
    {
        PdfTextSelection? captured = null;
        var cut = Render<TmPdfViewer>(p => ContinuousDefaults(p).Add(p => p.EnableAnnotations, true)
                                         .Add(p => p.OnTextSelected, EventCallback.Factory.Create<PdfTextSelection>(this, s => captured = s)));

        cut.InvokeAsync(() => cut.Instance.OnPdfLoaded(10));
        cut.Render();
        cut.InvokeAsync(() => cut.Instance.OnContinuousPageGeometry(
            [3, 7], [1600.0, 4800.0], [0.0, 0.0], [612.0, 612.0], [792.0, 792.0], 0));
        cut.Render();

        await cut.InvokeAsync(() => cut.Instance.OnTextSelectionChanged(
            "quoted words", 7, new[] { 0.1, 0.2, 0.3, 0.02 }));

        captured.Should().NotBeNull();
        captured!.Page.Should().Be(7, "the JS selection report carries the anchor page");
        cut.FindAll(".tm-pdf-viewer__page-overlay-slot [data-testid='pdf-annotation-draft']")
            .Should().ContainSingle("the draft renders in the selection's page slot");
        cut.Find(".tm-pdf-viewer__page-overlay-slot[data-page='7'] [data-testid='pdf-annotation-draft']")
            .Should().NotBeNull();
        cut.FindAll(".tm-pdf-viewer__page-overlay-slot[data-page='3'] [data-testid='pdf-annotation-draft']")
            .Should().BeEmpty("the draft never leaks into another page's slot");
    }

    // ── FZE-13: the sticky CTA flow creates the thread on the selected page ──

    [Fact]
    public async Task Continuous_SelectionCta_CreatesThreadOnSelectedPage()
    {
        var cut = Render<TmPdfViewer>(p => ContinuousDefaults(p).Add(p => p.EnableAnnotations, true));

        cut.InvokeAsync(() => cut.Instance.OnPdfLoaded(10));
        cut.Render();
        cut.InvokeAsync(() => cut.Instance.OnContinuousPageGeometry(
            [3, 7], [1600.0, 4800.0], [0.0, 0.0], [612.0, 612.0], [792.0, 792.0], 0));
        await cut.InvokeAsync(() => cut.Instance.OnTextSelectionChanged(
            "quoted words", 7, new[] { 0.1, 0.2, 0.3, 0.02 }));
        cut.Render();

        // The docked desktop panel hosts the selection prompt + composer; typing and
        // submitting creates the thread anchored on page 7 — its marker lands in the
        // page-7 slot without any mode switch.
        var input = cut.Find("[data-testid='pdf-annotation-new-input']");
        input.Input("A note on page 7");
        cut.Find("[data-testid='pdf-annotation-new-submit']").Click();
        cut.Render();

        var slot7 = cut.Find(".tm-pdf-viewer__page-overlay-slot[data-page='7']");
        slot7.QuerySelectorAll("[data-testid='pdf-annotation-marker']")
            .Should().ContainSingle("the new page-7 thread renders in its page's slot");
    }

    // ── FZE-13: the templated per-page overlay slot ─────────────────────────

    [Fact]
    public void PageOverlayContent_RendersPerPage_WithContext()
    {
        var contexts = new List<PdfPageOverlayContext>();
        var cut = Render<TmPdfViewer>(p => ContinuousDefaults(p)
            .Add(p => p.PageOverlayContent, (RenderFragment<PdfPageOverlayContext>)(context => builder =>
            {
                contexts.Add(context);
                builder.OpenElement(0, "div");
                builder.AddAttribute(1, "data-testid", $"page-overlay-{context.PageNumber}");
                builder.CloseElement();
            })));

        cut.InvokeAsync(() => cut.Instance.OnPdfLoaded(10));
        cut.Render();
        cut.InvokeAsync(() => cut.Instance.OnContinuousPageGeometry(
            [3, 7], [1600.0, 4800.0], [0.0, 0.0], [612.0, 612.0], [792.0, 792.0], 90));
        cut.Render();

        cut.Find("[data-testid='page-overlay-3']").Should().NotBeNull();
        cut.Find("[data-testid='page-overlay-7']").Should().NotBeNull();
        // The fragment runs once per slot per render (the geometry report renders once,
        // the explicit Render() again) — dedupe before asserting the context set.
        var distinct = contexts.Distinct().ToList();
        distinct.Should().HaveCount(2);
        distinct.Should().Contain(c => c.PageNumber == 3 && c.Width == 612 && c.Height == 792);
        distinct.Should().Contain(c => c.PageNumber == 7 && c.Rotation == 90);
        distinct.Should().OnlyContain(c => !c.IsCurrentPage, "no page is current before the scroll tracker reports one");

        // The scroll tracker flips the current page — the slots re-render with the
        // fresh context (the fragment may run again per render, hence the clear).
        contexts.Clear();
        cut.InvokeAsync(() => cut.Instance.OnVisiblePageChanged(7));
        cut.Render();
        contexts.Should().NotBeEmpty();
        contexts.Should().Contain(c => c.PageNumber == 7 && c.IsCurrentPage,
            "the context flags the page the viewer currently shows");
    }

    [Fact]
    public void PageOverlayContent_SinglePage_RendersForCurrentPageOnly()
    {
        var contexts = new List<PdfPageOverlayContext>();
        var cut = Render<TmPdfViewer>(parameters => parameters
            .Add(p => p.Url, "https://example.com/test.pdf")
            .Add(p => p.PageOverlayContent, (RenderFragment<PdfPageOverlayContext>)(context => builder =>
            {
                contexts.Add(context);
                builder.OpenElement(0, "div");
                builder.AddAttribute(1, "data-testid", "page-overlay-single");
                builder.CloseElement();
            })));

        cut.InvokeAsync(() => cut.Instance.OnPdfLoaded(10));
        cut.Render();

        cut.Find(".tm-pdf-viewer__canvas-wrap [data-testid='page-overlay-single']").Should().NotBeNull(
            "the templated overlay rides the canvas wrap in single-page mode");
        contexts.Should().NotBeEmpty();
        contexts.Should().OnlyContain(c => c.PageNumber == 1,
            "single-page renders exactly one slot: the current page");
        contexts.Should().OnlyContain(c => c.IsCurrentPage, "single-page always shows the current page");

        contexts.Clear();
        cut.InvokeAsync(() => cut.Instance.OnVisiblePageChanged(4));
        cut.Render(parameters => parameters.Add(p => p.Page, 4));
        contexts.Should().NotBeEmpty();
        contexts.Should().OnlyContain(c => c.PageNumber == 4, "the context follows the current page");
    }

    // ── FZE-13: the non-templated OverlayContent stays single-page (user decision) ──

    [Fact]
    public void OverlayContent_StaysSinglePageOnly_Documented()
    {
        var cut = Render<TmPdfViewer>(p => ContinuousDefaults(p)
            .Add(p => p.OverlayContent, builder =>
                builder.AddMarkupContent(0, "<div data-testid='custom-overlay-content'></div>")));

        cut.InvokeAsync(() => cut.Instance.OnPdfLoaded(10));
        cut.Render();
        cut.InvokeAsync(() => cut.Instance.OnContinuousPageGeometry(
            [3, 7], [1600.0, 4800.0], [0.0, 0.0], [612.0, 612.0], [792.0, 792.0], 0));
        cut.Render();

        cut.FindAll("[data-testid='custom-overlay-content']").Should().BeEmpty(
            "OverlayContent stays aligned to THE canvas — it is a single-page-only slot (PDF-VIEWER-CONTINUOUS-ANNOTATIONS)");
    }

    // ── FZE-13: single-page markup is unchanged when the new slots are unused ──

    [Fact]
    public void SinglePage_MarkupUnchanged()
    {
        var cut = Render<TmPdfViewer>(parameters => parameters
            .Add(p => p.Url, "https://example.com/test.pdf")
            .Add(p => p.EnableAnnotations, true)
            .Add(p => p.Annotations, new List<DocumentCommentThread> { TextRangeThread("t1", 1) }));

        cut.InvokeAsync(() => cut.Instance.OnPdfLoaded(10));
        cut.Render();

        cut.Find(".tm-pdf-viewer__canvas-wrap .tm-pdf-annotation-layer").Should().NotBeNull(
            "the annotation layer keeps its single-page home inside the canvas wrap");
        cut.FindAll(".tm-pdf-viewer__continuous-overlays").Should().BeEmpty();
        cut.FindAll(".tm-pdf-viewer__page-overlay-slot").Should().BeEmpty();
        cut.FindAll("[data-testid='pdf-page-overlay']").Should().BeEmpty(
            "no templated overlay slot renders when PageOverlayContent is not supplied");
    }

    // ── Zoom in continuous re-renders lazily and keeps the current page ─────

    [Fact]
    public async Task Continuous_ZoomChange_PreservesCurrentPage()
    {
        var pages = new List<int>();
        var cut = Render<TmPdfViewer>(p => ContinuousDefaults(p).Add(
            p => p.PageChanged, EventCallback.Factory.Create<int>(this, page => pages.Add(page))));

        cut.InvokeAsync(() => cut.Instance.OnPdfLoaded(10));
        cut.InvokeAsync(() => cut.Instance.OnVisiblePageChanged(4));
        cut.InvokeAsync(() => cut.Instance.SetScaleAsync(2.0));

        pages.Should().OnlyContain(page => page == 4, "zooming must not navigate");
        // A scale-only change updates the placeholders in place — the DOM (wraps,
        // layers, observers, scroll bindings) survives; only Url/rotation rebuild it.
        var updates = JSInterop.VerifyInvoke("tmPdfViewer.updateContinuousScale", 1);
        updates.Last().Arguments[2].Should().Be(2.0, "the in-place update carries the new scale");
        JSInterop.VerifyInvoke("tmPdfViewer.renderAllPages", 1); // the initial render only
        InvocationsOf(this, "tmPdfViewer.renderPage").Should().BeEmpty();
        InvocationsOf(this, "tmPdfViewer.scrollToPage").Should().BeEmpty(
            "the scroll anchor is preserved by the JS in-place update, no navigation needed");
    }

    // ── Switching to Continuous on page N passes the page for the scroll restore ──

    [Fact]
    public async Task SwitchToContinuous_OnPageN_PassesCurrentPageToRenderAllPages()
    {
        var cut = Render<TmPdfViewer>(parameters => parameters
            .Add(p => p.Url, "https://example.com/test.pdf")
            .Add(p => p.ShowToolbar, true)
            .Add(p => p.Page, 3));

        cut.InvokeAsync(() => cut.Instance.OnPdfLoaded(10));
        cut.Render();

        await cut.InvokeAsync(async () => await cut.Instance.SetParametersAsync(
            ParameterView.FromDictionary(new Dictionary<string, object?> { [nameof(TmPdfViewer.ViewMode)] = PdfViewMode.Continuous })));

        cut.WaitForAssertion(() =>
            JSInterop.VerifyInvoke("tmPdfViewer.renderAllPages", 1));
        var invocation = JSInterop.VerifyInvoke("tmPdfViewer.renderAllPages", 1).Last();
        var options = invocation.Arguments[4] switch
        {
            System.Text.Json.JsonElement json => json,
            var raw => System.Text.Json.JsonSerializer.SerializeToElement(raw),
        };
        options.GetProperty("currentPage").GetInt32().Should().Be(3,
            "the JS side restores the scroll to page N when the fresh build has no anchor");
    }

    private static DocumentCommentThread ResolvedThread(string id, int page)
    {
        var thread = TextRangeThread(id, page);
        thread.Status = DocumentCommentThreadStatus.Resolved;
        return thread;
    }
}
