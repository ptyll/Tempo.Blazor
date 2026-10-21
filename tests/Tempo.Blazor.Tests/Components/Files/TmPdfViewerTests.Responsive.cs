/// <summary>Responsive-layout, zoom-mode and panel tests of <see cref="TmPdfViewer"/> (FZE-7 phase, split of TmPdfViewerTests.cs).</summary>
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
    // ── PDF-56: body má flex layout s thumbnails ────────────────────────────

    [Fact]
    public void Render_WithThumbnails_BodyHasFlexLayout()
    {
        var cut = Render<TmPdfViewer>(parameters =>
            parameters.Add(p => p.Url, "https://example.com/test.pdf")
                      .Add(p => p.ShowThumbnails, true));

        var body = cut.Find(".tm-pdf-viewer__body");
        body.Should().NotBeNull();
    }

    // ── Zoom preset sets the scale and switches the mode to Custom ──────────

    [Fact]
    public async Task ZoomPreset_Select_SetsScaleAndZoomModeCustom()
    {
        double capturedScale = 1.0;
        PdfZoomMode capturedMode = PdfZoomMode.FitWidth;
        var cut = Render<TmPdfViewer>(parameters =>
            parameters.Add(p => p.Url, "https://example.com/test.pdf")
                      .Add(p => p.ShowToolbar, true)
                      .Add(p => p.ZoomMode, PdfZoomMode.FitWidth)
                      .Add(p => p.ScaleChanged, EventCallback.Factory.Create<double>(this, s => capturedScale = s))
                      .Add(p => p.ZoomModeChanged, EventCallback.Factory.Create<PdfZoomMode>(this, m => capturedMode = m)));

        cut.InvokeAsync(() => cut.Instance.OnPdfLoaded(5));
        cut.Render();

        // The dropdown closes and yields before invoking OnSelect, so the handler
        // completes a few dispatcher hops after the click.
        await cut.InvokeAsync(async () =>
        {
            cut.Find(".tm-pdf-viewer__zoom .tm-dropdown-trigger").Click();
            var item = cut.FindAll(".tm-pdf-viewer__zoom .tm-dropdown-item")
                          .First(i => i.TextContent.Contains("125%"));
            item.Click();
        });

        cut.WaitForAssertion(() => capturedScale.Should().Be(1.25));
        capturedMode.Should().Be(PdfZoomMode.Custom);
    }

    // ── ZoomMode=FitWidth invokes the JS fit and raises ScaleChanged ────────

    [Fact]
    public void ZoomMode_FitWidth_InvokesJsFitAndRaisesScaleChanged()
    {
        JSInterop.Setup<double>("tmPdfViewer.fitScale", _ => true).SetResult(1.5);
        double capturedScale = 1.0;
        var cut = Render<TmPdfViewer>(parameters =>
            parameters.Add(p => p.Url, "https://example.com/test.pdf")
                      .Add(p => p.ShowToolbar, true)
                      .Add(p => p.ScaleChanged, EventCallback.Factory.Create<double>(this, s => capturedScale = s)));

        cut.InvokeAsync(() => cut.Instance.OnPdfLoaded(5));
        cut.Render();
        cut.Render(parameters => parameters.Add(p => p.ZoomMode, PdfZoomMode.FitWidth));

        JSInterop.Invocations["tmPdfViewer.fitScale"].Should().NotBeEmpty();
        // The JS fit answer arrives asynchronously and must raise ScaleChanged.
        cut.WaitForAssertion(() => capturedScale.Should().Be(1.5));
    }

    // ── ZoomMode=FitPage invokes the JS fit ─────────────────────────────────

    [Fact]
    public void ZoomMode_FitPage_InvokesJsFit()
    {
        JSInterop.Setup<double>("tmPdfViewer.fitScale", _ => true).SetResult(0.8);
        var cut = Render<TmPdfViewer>(parameters =>
            parameters.Add(p => p.Url, "https://example.com/test.pdf")
                      .Add(p => p.ShowToolbar, true));

        cut.InvokeAsync(() => cut.Instance.OnPdfLoaded(5));
        cut.Render();
        cut.Render(parameters => parameters.Add(p => p.ZoomMode, PdfZoomMode.FitPage));

        cut.WaitForAssertion(() =>
            JSInterop.Invocations["tmPdfViewer.fitScale"].Should().NotBeEmpty());
        var invocation = JSInterop.Invocations["tmPdfViewer.fitScale"].Single();
        invocation.Arguments.Should().Contain("fit-page");
    }

    // ── ± zoom buttons switch a fit mode back to Custom ─────────────────────

    [Fact]
    public void ZoomIn_FromFitMode_SwitchesToCustom()
    {
        PdfZoomMode capturedMode = PdfZoomMode.FitWidth;
        var cut = Render<TmPdfViewer>(parameters =>
            parameters.Add(p => p.Url, "https://example.com/test.pdf")
                      .Add(p => p.ShowToolbar, true)
                      .Add(p => p.ZoomMode, PdfZoomMode.FitWidth)
                      .Add(p => p.ZoomModeChanged, EventCallback.Factory.Create<PdfZoomMode>(this, m => capturedMode = m)));

        cut.InvokeAsync(() => cut.Instance.OnPdfLoaded(5));
        cut.Render();

        var zoomInBtn = cut.FindAll(".tm-pdf-viewer__btn")
                           .First(b => b.GetAttribute("aria-label")?.Contains("Zoom in") == true);
        zoomInBtn.Click();

        capturedMode.Should().Be(PdfZoomMode.Custom);
    }

    // ── Fit mode: a view-mode switch refits on the fresh continuous container ──

    [Fact]
    public void ZoomMode_FitWidth_ViewModeSwitch_RefitsOnContinuousContainer()
    {
        JSInterop.Setup<double>("tmPdfViewer.fitScale", _ => true).SetResult(1.5);
        var cut = Render<TmPdfViewer>(parameters =>
            parameters.Add(p => p.Url, "https://example.com/test.pdf")
                      .Add(p => p.ShowToolbar, true)
                      .Add(p => p.ShowViewModeToggle, true)
                      .Add(p => p.ZoomMode, PdfZoomMode.FitWidth));

        cut.InvokeAsync(() => cut.Instance.OnPdfLoaded(5));
        cut.Render();

        // The initial fit observed the single-page canvas wrap.
        cut.WaitForAssertion(() =>
            JSInterop.Invocations["tmPdfViewer.fitScale"].Should().HaveCount(1));
        var beforeSwitch = JSInterop.Invocations["tmPdfViewer.fitScale"].Single();

        var continuousInput = cut.FindAll(".tm-pdf-viewer__view-mode input[type='radio']").Last();
        continuousInput.Change(true);

        // The continuous wrap is a different element that exists only after the switch
        // rendered — the recompute must run from OnAfterRenderAsync (_fitPending) with
        // the fresh container.
        cut.WaitForAssertion(() =>
            JSInterop.Invocations["tmPdfViewer.fitScale"].Should().HaveCount(2,
                "the fit mode must recompute once the continuous container exists"));
        var afterSwitch = JSInterop.Invocations["tmPdfViewer.fitScale"].Last();
        afterSwitch.Arguments.Should().Contain("fit-width");
        afterSwitch.Arguments[1].Should().NotBe(beforeSwitch.Arguments[1],
            "the recompute must observe the swapped (continuous) container");
    }

    // ── Fit mode: rotating recomputes (the page aspect ratio changed) ────────

    [Fact]
    public async Task Rotate_InFitMode_Refits()
    {
        JSInterop.Setup<double>("tmPdfViewer.fitScale", _ => true).SetResult(1.5);
        var cut = Render<TmPdfViewer>(parameters =>
            parameters.Add(p => p.Url, "https://example.com/test.pdf")
                      .Add(p => p.AllowRotation, true)
                      .Add(p => p.ZoomMode, PdfZoomMode.FitWidth));

        await cut.InvokeAsync(() => cut.Instance.OnPdfLoaded(5));
        cut.WaitForAssertion(() =>
            JSInterop.Invocations["tmPdfViewer.fitScale"].Should().HaveCount(1));

        await cut.Instance.RotateAsync();

        cut.WaitForAssertion(() =>
            JSInterop.Invocations["tmPdfViewer.fitScale"].Should().HaveCount(2,
                "the rotation changes the page aspect ratio, so the fit must recompute"));
        JSInterop.Invocations["tmPdfViewer.fitScale"].Last().Arguments.Should().Contain("fit-width");
    }

    // ── Fit mode: page changes recompute (prev/next, page input, thumbnails) ──

    [Fact]
    public async Task PageChange_InFitMode_Refits()
    {
        JSInterop.Setup<double>("tmPdfViewer.fitScale", _ => true).SetResult(1.5);
        var cut = Render<TmPdfViewer>(parameters =>
            parameters.Add(p => p.Url, "https://example.com/test.pdf")
                      .Add(p => p.ShowToolbar, true)
                      .Add(p => p.ZoomMode, PdfZoomMode.FitWidth));

        await cut.InvokeAsync(() => cut.Instance.OnPdfLoaded(5));
        cut.WaitForAssertion(() =>
            JSInterop.Invocations["tmPdfViewer.fitScale"].Should().HaveCount(1));

        await cut.Instance.GoToPageAsync(3);

        cut.WaitForAssertion(() =>
            JSInterop.Invocations["tmPdfViewer.fitScale"].Should().HaveCount(2,
                "a different page size changes the container ratio"));
    }

    // ── Host-driven ZoomMode=Custom detaches the JS fit observer ─────────────

    [Fact]
    public void ZoomMode_HostSetsCustom_DetachesJsFitObserver()
    {
        var cut = Render<TmPdfViewer>(parameters =>
            parameters.Add(p => p.Url, "https://example.com/test.pdf")
                      .Add(p => p.ShowToolbar, true)
                      .Add(p => p.ZoomMode, PdfZoomMode.FitWidth));

        cut.InvokeAsync(() => cut.Instance.OnPdfLoaded(5));
        cut.Render();

        cut.Render(parameters => parameters.Add(p => p.ZoomMode, PdfZoomMode.Custom));

        cut.WaitForAssertion(() =>
            JSInterop.Invocations["tmPdfViewer.clearFit"].Should().NotBeEmpty(
                "a host-driven switch to Custom must detach the JS ResizeObserver like the user-driven path"));
    }

    // ── OnFitScaleComputed rejects scales outside the documented clamp range ──

    [Fact]
    public async Task OnFitScaleComputed_RejectsScaleBelowMinScale()
    {
        double? capturedScale = null;
        var cut = Render<TmPdfViewer>(parameters =>
            parameters.Add(p => p.Url, "https://example.com/test.pdf")
                      .Add(p => p.ShowToolbar, true)
                      .Add(p => p.ZoomMode, PdfZoomMode.FitWidth)
                      .Add(p => p.ScaleChanged, EventCallback.Factory.Create<double>(this, s => capturedScale = s)));

        await cut.InvokeAsync(() => cut.Instance.OnPdfLoaded(5));

        await cut.Instance.OnFitScaleComputed(0.05);
        capturedScale.Should().BeNull("a scale below MinScale is outside the documented clamp range");

        await cut.Instance.OnFitScaleComputed(1.5);
        capturedScale.Should().Be(1.5);
    }

    // ── ThumbnailsCollapsed toggled purely via parameters re-renders thumbnails ──

    [Fact]
    public void ThumbnailsCollapsed_ParamToggle_ReRendersThumbnails()
    {
        var cut = Render<TmPdfViewer>(parameters =>
            parameters.Add(p => p.Url, "https://example.com/test.pdf")
                      .Add(p => p.ShowToolbar, true)
                      .Add(p => p.ShowThumbnails, true));

        cut.InvokeAsync(() => cut.Instance.OnPdfLoaded(5));
        cut.WaitForAssertion(() =>
            JSInterop.Invocations["tmPdfViewer.renderThumbnails"].Should().HaveCount(1));

        cut.Render(parameters => parameters.Add(p => p.ThumbnailsCollapsed, true));
        cut.Render(parameters => parameters.Add(p => p.ThumbnailsCollapsed, false));

        // The JS-rendered thumbnails died with the hidden panel: the expanding
        // parameter change must rebuild them in the fresh container.
        cut.WaitForAssertion(() =>
            JSInterop.Invocations["tmPdfViewer.renderThumbnails"].Should().HaveCount(2,
                "expanding via parameters must re-render the thumbnails"));
    }

    // ── Thumbnails collapse toggle raises ThumbnailsCollapsedChanged ────────

    [Fact]
    public void Thumbnails_Collapse_TogglesAndRaisesThumbnailsCollapsedChanged()
    {
        bool? collapsed = null;
        var cut = Render<TmPdfViewer>(parameters =>
            parameters.Add(p => p.Url, "https://example.com/test.pdf")
                      .Add(p => p.ShowToolbar, true)
                      .Add(p => p.ShowThumbnails, true)
                      .Add(p => p.ThumbnailsCollapsedChanged, EventCallback.Factory.Create<bool>(this, c => collapsed = c)));

        cut.InvokeAsync(() => cut.Instance.OnPdfLoaded(5));
        cut.Render();

        cut.Find(".tm-pdf-viewer__thumbnails").Should().NotBeNull();
        cut.Find("[data-testid='pdf-thumbnails-collapse']").Click();

        collapsed.Should().BeTrue();

        // The host rebinds the parameter (what @bind- does); like Page/ViewMode the
        // parameter is authoritative on the next render.
        cut.Render(parameters => parameters.Add(p => p.ThumbnailsCollapsed, true));
        cut.FindAll(".tm-pdf-viewer__thumbnails").Should().BeEmpty("the collapsed sidebar is hidden");
        cut.Find("[data-testid='pdf-thumbnails-expand']").Should().NotBeNull("the collapsed rail keeps one reopen affordance");

        // The rail's expand button (and the toolbar Pages toggle) raise the change again.
        collapsed = null;
        cut.Find("[data-testid='pdf-thumbnails-expand']").Click();
        collapsed.Should().BeFalse();
        cut.Render(parameters => parameters.Add(p => p.ThumbnailsCollapsed, false));
        cut.Find(".tm-pdf-viewer__thumbnails").Should().NotBeNull();
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Phase FZE-7 — toolbar labels and the responsive viewer (tablet & mobile)
    // ══════════════════════════════════════════════════════════════════════════

    /// <summary>Renders a loaded viewer with a forced <see cref="TmLayoutMode"/> (tests need no DOM).</summary>
    private IRenderedComponent<TmPdfViewer> RenderInLayout(
        TmLayoutMode mode,
        Action<ComponentParameterCollectionBuilder<TmPdfViewer>>? extra = null)
    {
        var cut = Render<TmPdfViewer>((ComponentParameterCollectionBuilder<TmPdfViewer> p) =>
        {
            p.Add(c => c.Url, "https://example.com/test.pdf")
             .Add(c => c.ShowToolbar, true)
             .Add(c => c.LayoutMode, mode);
            extra?.Invoke(p);
        });
        cut.InvokeAsync(() => cut.Instance.OnPdfLoaded(10));
        cut.Render();
        return cut;
    }

    // ── G14: the default Icons labels keep the previous phase's icon-only toolbar ──

    [Fact]
    public void ToolbarLabels_DefaultIcons_IsMarkupIdenticalToPreviousPhase()
    {
        var cut = RenderInLayout(TmLayoutMode.Desktop);

        // The converted toolbar keeps the previous phase's role/label contract.
        var toolbar = cut.Find(".tm-pdf-viewer__toolbar");
        toolbar.GetAttribute("role").Should().Be("toolbar");
        toolbar.GetAttribute("aria-label").Should().Be("PDF controls");

        // Icons (the default) renders icon-only buttons — no visible label span anywhere.
        cut.FindAll(".tm-toolbar-btn-text").Should().BeEmpty(
            "the default Icons mode must look exactly like the previous phase's icon-only toolbar");
        // The previous-phase button class and the nav aria-labels survive the conversion.
        cut.FindAll("button.tm-pdf-viewer__btn").Should().NotBeEmpty();
        cut.FindAll("button.tm-pdf-viewer__btn")
           .Should().Contain(b => b.GetAttribute("aria-label") == "Previous page");
    }

    // ── G14: IconsWithText renders the labels next to the icons ──────────────

    [Fact]
    public void ToolbarLabels_IconsWithText_RendersVisibleLabels()
    {
        var cut = Render<TmPdfViewer>(p => p
            .Add(c => c.Url, "https://example.com/test.pdf")
            .Add(c => c.ShowToolbar, true)
            .Add(c => c.LayoutMode, TmLayoutMode.Desktop)
            .Add(c => c.ToolbarLabels, ToolbarLabels.IconsWithText));
        cut.InvokeAsync(() => cut.Instance.OnPdfLoaded(10));
        cut.Render();

        var labels = cut.FindAll(".tm-toolbar-btn-text").Select(l => l.TextContent.Trim()).ToList();
        labels.Should().Contain("Previous page");
        labels.Should().Contain("Next page");
        labels.Should().Contain("Zoom in");
    }

    // ── Mobile: the in-flow action bar exposes Pages | Search | Comments ─────

    [Fact]
    public void LayoutMobile_RendersActionBar_PagesSearchComments()
    {
        var cut = RenderInLayout(TmLayoutMode.Mobile, p => p
            .Add(c => c.ShowThumbnails, true)
            .Add(c => c.ShowSearch, true)
            .Add(c => c.EnableAnnotations, true));

        var bar = cut.Find(".tm-mobile-action-bar__bar");
        bar.Should().NotBeNull("mobile renders the compact action bar");
        var ids = bar.QuerySelectorAll("[data-action-id]")
                     .Select(el => el.GetAttribute("data-action-id")).ToList();
        ids.Should().Contain("pages");
        ids.Should().Contain("search");
        ids.Should().Contain("comments");
    }

    // ── Mobile: Rotate / Open / Download live only in the overflow menu ──────

    [Fact]
    public void LayoutMobile_OverflowMenuContainsRotateOpenDownload()
    {
        var cut = RenderInLayout(TmLayoutMode.Mobile, p => p
            .Add(c => c.AllowRotation, true)
            .Add(c => c.AllowDownload, true));

        cut.WaitForAssertion(() => cut.FindAll("button.tm-toolbar-more").Should().HaveCount(1));
        cut.Find("button.tm-toolbar-more").Click();

        var labels = cut.FindAll("[role='menu'] [role='menuitem']")
                        .Select(i => i.TextContent.Trim()).ToList();
        labels.Should().Contain(l => l.Contains("Rotate"));
        labels.Should().Contain(l => l.Contains("Open"));
        labels.Should().Contain(l => l.Contains("Download"));
    }

    // ── PD2: ZoomMode=Auto on mobile requests the fit-width computation ──────

    [Fact]
    public void LayoutMobile_ZoomModeAuto_RequestsFitWidth()
    {
        var cut = Render<TmPdfViewer>(p => p
            .Add(c => c.Url, "https://example.com/test.pdf")
            .Add(c => c.ShowToolbar, true)
            .Add(c => c.LayoutMode, TmLayoutMode.Mobile)
            .Add(c => c.ZoomMode, PdfZoomMode.Auto));

        cut.InvokeAsync(() => cut.Instance.OnPdfLoaded(10));

        cut.WaitForAssertion(() => InvocationsOf(this, "tmPdfViewer.fitScale").Should().NotBeEmpty());
        InvocationsOf(this, "tmPdfViewer.fitScale").Last().Arguments[2].Should().Be("fit-width");
    }

    // ── PD2: ZoomMode=Auto on desktop keeps the bound Scale ──────────────────

    [Fact]
    public void LayoutDesktop_ZoomModeAuto_KeepsScale()
    {
        double? captured = null;
        var cut = Render<TmPdfViewer>(p => p
            .Add(c => c.Url, "https://example.com/test.pdf")
            .Add(c => c.ShowToolbar, true)
            .Add(c => c.LayoutMode, TmLayoutMode.Desktop)
            .Add(c => c.ZoomMode, PdfZoomMode.Auto)
            .Add(c => c.Scale, 1.25)
            .Add(c => c.ScaleChanged, EventCallback.Factory.Create<double>(this, s => captured = s)));

        cut.InvokeAsync(() => cut.Instance.OnPdfLoaded(10));
        cut.Render();

        cut.WaitForAssertion(() => InvocationsOf(this, "tmPdfViewer.fitScale").Should().BeEmpty(
            "Auto resolves to the bound Scale off mobile — no fit computation is requested"));
        captured.Should().BeNull();
        cut.Find(".tm-pdf-viewer__zoom .tm-dropdown-trigger").TextContent.Trim().Should().Contain("125%");
        cut.FindAll(".tm-mobile-action-bar").Should().BeEmpty("desktop never renders the mobile bar");
    }

    // ── Tablet: the comments panel is a side sheet, closed by default ────────

    [Fact]
    public void LayoutTablet_CommentsPanelIsSideSheet_ClosedByDefault()
    {
        var cut = RenderInLayout(TmLayoutMode.Tablet, p => p
            .Add(c => c.EnableAnnotations, true)
            .Add(c => c.Annotations, new List<DocumentCommentThread> { TextRangeThread("t1", 1) }));

        cut.FindAll(".tm-pdf-annotation-panel").Should().BeEmpty(
            "the docked panel is not rendered on a tablet layout");
        cut.FindAll(".tm-drawer--right").Should().BeEmpty("the side sheet is closed by default");

        var toggle = cut.Find("[data-testid='pdf-annotations-toggle']");
        toggle.GetAttribute("aria-pressed").Should().Be("false");
        toggle.Click();

        cut.Find(".tm-drawer--right").Should().NotBeNull("the comments panel presents as a side sheet");
        cut.Find(".tm-drawer--right .tm-pdf-annotation-panel").Should().NotBeNull();
    }

    // ── Mobile: the comments panel presents as a bottom sheet ────────────────

    [Fact]
    public void LayoutMobile_CommentsPanelIsBottomSheet()
    {
        var cut = RenderInLayout(TmLayoutMode.Mobile, p => p
            .Add(c => c.EnableAnnotations, true)
            .Add(c => c.Annotations, new List<DocumentCommentThread> { TextRangeThread("t1", 1) }));

        cut.FindAll(".tm-pdf-annotation-panel").Should().BeEmpty(
            "the docked panel is not rendered on a mobile layout");

        cut.Find("[data-action-id='comments']").Click();

        var sheet = cut.Find(".tm-drawer--bottom");
        sheet.Should().NotBeNull("the comments panel presents as a bottom sheet on mobile");
        sheet.QuerySelector(".tm-pdf-annotation-panel").Should().NotBeNull();
    }

    // ── Closing the comments sheet restores focus to the toolbar toggle ──────

    [Fact]
    public void CommentsSheet_Close_RestoresFocusToToggle()
    {
        var cut = RenderInLayout(TmLayoutMode.Tablet, p => p
            .Add(c => c.EnableAnnotations, true)
            .Add(c => c.Annotations, new List<DocumentCommentThread> { TextRangeThread("t1", 1) }));

        cut.Find("[data-testid='pdf-annotations-toggle']").Click();

        // The toggle's id is a per-instance GUID (two viewers on one page must not share an
        // id), so the restore target is compared against the rendered id, not a literal.
        var toggleId = cut.Find("[data-testid='pdf-annotations-toggle']").GetAttribute("id");
        toggleId.Should().NotBeNullOrEmpty().And.StartWith("tm-pdf-viewer-comments-");

        var sheet = cut.Find(".tm-drawer--right");
        sheet.GetAttribute("data-restore-target").Should().Be(toggleId,
            "the sheet records the toolbar toggle as its focus-restore target");

        cut.Find(".tm-drawer__close").Click();

        cut.FindAll(".tm-drawer--right").Should().BeEmpty();
        cut.Find("[data-testid='pdf-annotations-toggle']").GetAttribute("aria-pressed").Should().Be("false");
    }

    // ── The comments toggle id is per-instance, never a shared constant ──────

    [Fact]
    public void CommentsToggleIds_ArePerInstance()
    {
        var first = RenderInLayout(TmLayoutMode.Desktop, p => p.Add(c => c.EnableAnnotations, true));
        var second = RenderInLayout(TmLayoutMode.Desktop, p => p.Add(c => c.EnableAnnotations, true));

        var firstId = first.Find("[data-testid='pdf-annotations-toggle']").GetAttribute("id");
        var secondId = second.Find("[data-testid='pdf-annotations-toggle']").GetAttribute("id");
        firstId.Should().NotBeNullOrEmpty();
        secondId.Should().NotBeNullOrEmpty();
        firstId.Should().NotBe(secondId, "two viewers on one page must not share the toggle id");
    }

    // ── Selecting a page in the thumbnails sheet closes it and navigates ─────

    [Fact]
    public async Task ThumbnailsSheet_SelectPage_ClosesAndNavigates()
    {
        int capturedPage = 1;
        var cut = Render<TmPdfViewer>(p => p
            .Add(c => c.Url, "https://example.com/test.pdf")
            .Add(c => c.ShowToolbar, true)
            .Add(c => c.ShowThumbnails, true)
            .Add(c => c.LayoutMode, TmLayoutMode.Mobile)
            .Add(c => c.PageChanged, EventCallback.Factory.Create<int>(this, page => capturedPage = page)));
        cut.InvokeAsync(() => cut.Instance.OnPdfLoaded(10));
        cut.Render();

        cut.Find("[data-action-id='pages']").Click();
        cut.Find(".tm-drawer--bottom .tm-pdf-viewer__thumbnails-list").Should().NotBeNull();

        await cut.InvokeAsync(() => cut.Instance.OnThumbnailClicked(3));

        cut.FindAll(".tm-drawer--bottom .tm-pdf-viewer__thumbnails-list")
           .Should().BeEmpty("selecting a page closes the sheet");
        capturedPage.Should().Be(3, "the sheet selection navigates to the page");
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Review fixes — Auto panel defaults and the forced cascaded ancestor
    // ══════════════════════════════════════════════════════════════════════════

    /// <summary>Allows the wrapping TmLayoutObserver to attach (the module import is mocked),
    /// so its <see cref="TmLayoutObserver.OnLayoutModeChanged"/> can be driven from the test.</summary>
    private void SetupLayoutObserverModule()
    {
        var module = JSInterop.SetupModule("./_content/Tempo.Blazor/js/layout-observer.js");
        module.SetupVoid("observe", _ => true);
        module.SetupVoid("disconnect", _ => true);
    }

    // ── Auto: a measured flip re-applies the panel defaults (until the user touched) ──

    [Fact]
    public async Task LayoutAuto_ObserverReportsTablet_AppliesPanelDefaults()
    {
        SetupLayoutObserverModule();

        var cut = Render<TmPdfViewer>(p => p
            .Add(c => c.Url, "https://example.com/test.pdf")
            .Add(c => c.ShowToolbar, true)
            .Add(c => c.ShowThumbnails, true)
            .Add(c => c.EnableAnnotations, true)
            .Add(c => c.Annotations, new List<DocumentCommentThread> { TextRangeThread("t1", 1) }));
        cut.InvokeAsync(() => cut.Instance.OnPdfLoaded(10));
        cut.Render();

        // First frame: Desktop — the docked thumbnails are open.
        cut.Find("[data-testid='pdf-thumbnails-panel']").Should().NotBeNull();

        await cut.InvokeAsync(() =>
            cut.FindComponents<TmLayoutObserver>()[0].Instance.OnLayoutModeChanged("tablet"));

        // The tablet defaults re-apply under Auto: the docked thumbnails start collapsed and
        // the comments panel is not docked (the side sheet is closed by default).
        cut.FindAll("[data-testid='pdf-thumbnails-panel']").Should().BeEmpty(
            "the tablet default collapses the thumbnails panel");
        cut.Find("[data-testid='pdf-thumbnails-expand']").Should().NotBeNull(
            "the collapsed rail keeps the reopen affordance");
        cut.FindAll("[data-testid='pdf-annotation-panel']").Should().BeEmpty(
            "the comments panel only docks on a desktop layout");

        // Back to desktop: the defaults re-apply again (the user has not touched anything).
        await cut.InvokeAsync(() =>
            cut.FindComponents<TmLayoutObserver>()[0].Instance.OnLayoutModeChanged("desktop"));
        cut.Find("[data-testid='pdf-thumbnails-panel']").Should().NotBeNull(
            "desktop re-opens the docked thumbnails when the user has not touched them");
    }

    [Fact]
    public async Task LayoutAuto_UserTouchedPanels_DefaultsDoNotOverwrite()
    {
        SetupLayoutObserverModule();

        var cut = Render<TmPdfViewer>(p => p
            .Add(c => c.Url, "https://example.com/test.pdf")
            .Add(c => c.ShowToolbar, true)
            .Add(c => c.ShowThumbnails, true));
        cut.InvokeAsync(() => cut.Instance.OnPdfLoaded(10));
        cut.Render();

        // The user collapses the thumbnails through the toolbar Pages toggle: a user touch.
        cut.Find("[data-testid='pdf-pages-toggle']").Click();
        cut.FindAll("[data-testid='pdf-thumbnails-panel']").Should().BeEmpty();

        var observer = () => cut.FindComponents<TmLayoutObserver>()[0].Instance;
        await cut.InvokeAsync(() => observer().OnLayoutModeChanged("mobile"));
        await cut.InvokeAsync(() => observer().OnLayoutModeChanged("desktop"));

        cut.FindAll("[data-testid='pdf-thumbnails-panel']").Should().BeEmpty(
            "the user's own panel choice is never overwritten by a layout flip");
    }

    // ── A forced cascaded ancestor decides the layout like the shell resolution ──

    [Fact]
    public void CascadedMobileParent_ResolvesMobileLayout()
    {
        var cut = Render(builder =>
        {
            builder.OpenComponent<CascadingValue<TmLayoutContext>>(0);
            builder.AddAttribute(1, "Value", new TmLayoutContext(TmLayoutMode.Mobile, TmLayoutMode.Mobile));
            builder.AddAttribute(2, "IsFixed", true);
            builder.AddAttribute(3, "ChildContent", (RenderFragment)(b =>
            {
                b.OpenComponent<TmPdfViewer>(0);
                b.AddAttribute(1, "Url", "https://example.com/test.pdf");
                b.AddAttribute(2, "ShowToolbar", true);
                b.AddAttribute(3, "ShowThumbnails", true);
                b.CloseComponent();
            }));
            builder.CloseComponent();
        });
        cut.InvokeAsync(() => cut.FindComponent<TmPdfViewer>().Instance.OnPdfLoaded(120));
        cut.Render();

        // Mobile chrome: the compact row (no zoom ± buttons) and the in-flow action bar.
        cut.FindAll("button[aria-label='Zoom in']").Should().BeEmpty(
            "the mobile compact row drops the zoom ± buttons");
        cut.Find("[data-action-id='pages']").Should().NotBeNull(
            "the mobile action bar exposes the Pages sheet");
        cut.FindAll("[data-testid='pdf-thumbnails-panel']").Should().BeEmpty(
            "the docked thumbnails are a desktop/tablet presentation");
        cut.Find(".tm-pdf-viewer__zoom .tm-dropdown-trigger").TextContent.Trim().Should().Be("100%",
            "the mobile zoom trigger is the bare percentage");
    }

    // ── A cascaded-context flip re-applies the defaults and the Auto zoom ─────

    [Fact]
    public async Task CascadedLayoutFlip_DesktopToMobile_ClosesDockedPanelAndStartsFit()
    {
        var cut = Render<LayoutCascadeHost>(p => p
            .Add(h => h.Mode, TmLayoutMode.Desktop)
            .Add(h => h.ChildContent, nav =>
            {
                nav.OpenComponent<TmPdfViewer>(0);
                nav.AddAttribute(1, "Url", "https://example.com/test.pdf");
                nav.AddAttribute(2, "ShowToolbar", true);
                nav.AddAttribute(3, "ShowThumbnails", true);
                nav.AddAttribute(4, "EnableAnnotations", true);
                nav.AddAttribute(5, "Annotations", new List<DocumentCommentThread> { TextRangeThread("t1", 1) });
                nav.CloseComponent();
            }));

        var viewer = cut.FindComponent<TmPdfViewer>();
        await cut.InvokeAsync(() => viewer.Instance.OnPdfLoaded(120));
        cut.Render();

        // Desktop first: the docked comments panel is open and nothing has computed a fit.
        cut.Find("[data-testid='pdf-annotation-panel']").Should().NotBeNull(
            "the desktop layout docks the comments panel");
        InvocationsOf(this, "tmPdfViewer.fitScale").Should().BeEmpty(
            "Auto resolves to the bound scale on desktop — nothing to compute yet");

        // The parent flips its cascaded context Desktop → Mobile: the viewer must treat the
        // flip exactly like an observer report — the guarded panel defaults and the Auto zoom.
        cut.Render(p => p.Add(h => h.Mode, TmLayoutMode.Mobile));

        cut.FindAll("[data-testid='pdf-annotation-panel']").Should().BeEmpty(
            "the docked panel closes on the mobile layout (the bottom sheet owns the panel there)");
        cut.FindAll("[data-action-id='pages']").Should().ContainSingle(
            "the flip rendered the mobile chrome");
        cut.WaitForAssertion(() => InvocationsOf(this, "tmPdfViewer.fitScale").Should().NotBeEmpty());
        InvocationsOf(this, "tmPdfViewer.fitScale").Last().Arguments[2].Should().Be("fit-width",
            "Auto starts the fit-width computation on the mobile layout");
    }

    /// <summary>
    /// Test host: bUnit cannot re-render a rendered component with new cascading values, so the
    /// cascade-flip test drives the cascaded <see cref="TmLayoutContext"/> through this wrapper's
    /// parameter (the TmSidePanelCascadeHost pattern).
    /// </summary>
    private sealed class LayoutCascadeHost : ComponentBase
    {
        [Parameter] public TmLayoutMode Mode { get; set; }

        [Parameter] public RenderFragment? ChildContent { get; set; }

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenComponent<CascadingValue<TmLayoutContext>>(0);
            builder.AddAttribute(1, "Value", new TmLayoutContext(Mode, Mode));
            builder.AddAttribute(2, "IsFixed", false);
            builder.AddAttribute(3, "ChildContent", ChildContent);
            builder.CloseComponent();
        }
    }
}
