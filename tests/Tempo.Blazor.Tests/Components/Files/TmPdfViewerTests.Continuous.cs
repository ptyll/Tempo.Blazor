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

    // ── A11y: the PD1 hint note hides its decorative icon from AT ───────────

    [Fact]
    public void Render_Continuous_AnnotationsHint_IconIsAriaHidden()
    {
        var cut = Render<TmPdfViewer>(parameters =>
            parameters.Add(p => p.Url, "https://example.com/test.pdf")
                      .Add(p => p.ViewMode, PdfViewMode.Continuous)
                      .Add(p => p.EnableAnnotations, true));

        cut.InvokeAsync(() => cut.Instance.OnPdfLoaded(5));
        cut.Render();

        var hint = cut.Find("[data-testid='pdf-annotations-single-page-hint']");
        hint.Should().NotBeNull();
        hint.QuerySelector("svg").Should().NotBeNull();
        hint.QuerySelector("svg").GetAttribute("aria-hidden").Should().Be("true",
            "the decorative icon must not be announced next to the note's text");
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

    // ── PD1: annotations and OverlayContent stay out of continuous ──────────

    [Fact]
    public void Continuous_EnableAnnotations_DoesNotRenderOverlay_AndShowsSinglePageHint()
    {
        var cut = Render<TmPdfViewer>(p => ContinuousDefaults(p).Add(p => p.EnableAnnotations, true)
                                         .Add(p => p.Annotations, new List<DocumentCommentThread> { TextRangeThread("t1", 3) })
                                         .Add(p => p.OverlayContent, builder =>
                                             builder.AddMarkupContent(0, "<div data-testid='custom-overlay-content'></div>")));

        cut.InvokeAsync(() => cut.Instance.OnPdfLoaded(10));
        cut.Render();

        cut.FindAll(".tm-pdf-viewer__annotation-overlay").Should().BeEmpty(
            "PD1: the annotation overlay is not rendered in continuous mode");
        cut.FindAll("[data-testid='custom-overlay-content']").Should().BeEmpty(
            "PD1: OverlayContent is not rendered in continuous mode");
        cut.Find("[data-testid='pdf-annotations-single-page-hint']").Should().NotBeNull(
            "the panel stays readable and explains the single-page limitation");
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

    // ── Selecting a thread from the panel returns to SinglePage (PD1) ───────

    [Fact]
    public async Task Continuous_SelectThread_SwitchesToSinglePageOnThreadPage()
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

        capturedMode.Should().Be(PdfViewMode.SinglePage,
            "the overlay only exists in single-page mode, so the thread pulls the viewer back");
        JSInterop.VerifyInvoke("tmPdfViewer.renderPage", 1).Last()
            .Arguments[1].Should().Be(3, "the viewer lands on the thread's page in single-page mode");
    }

    private static DocumentCommentThread ResolvedThread(string id, int page)
    {
        var thread = TextRangeThread(id, page);
        thread.Status = DocumentCommentThreadStatus.Resolved;
        return thread;
    }
}
