using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.Abstractions.Models;
using Tempo.Blazor.Components.Files;
using Tempo.Blazor.Components.Layout;
using Tempo.Blazor.Components.Toolbar;
using Xunit;

namespace Tempo.Blazor.Tests.Components.Files;

/// <summary>
/// The annotator laid out on <c>TmEditorShell</c> (FZE-8) and its review fixes — split out of
/// <c>TmPdfAnnotatorTests.cs</c> to keep that file under the 1200-line source ceiling
/// (<c>SourceFileLengthTests</c>); the class is the same partial, so the helpers are shared.
/// </summary>
public partial class TmPdfAnnotatorTests
{    // ── Phase 8 (FZE-8): the annotator laid out on TmEditorShell ─────────────

    /// <summary>Renders the annotator with a forced <see cref="TmLayoutMode"/> (tests need no DOM).</summary>
    private IRenderedComponent<TmPdfAnnotator> RenderInLayout(
        TmLayoutMode mode,
        IPdfAnnotationProvider? provider = null,
        Action<Bunit.ComponentParameterCollectionBuilder<TmPdfAnnotator>>? configure = null)
        => Render(provider, p =>
        {
            p.Add(x => x.LayoutMode, mode);
            configure?.Invoke(p);
        });

    [Fact]
    public void UsesEditorShell_CanvasAndRightRegions()
    {
        var cut = RenderInLayout(TmLayoutMode.Desktop, Seeded(Highlight("h1")));

        // The annotator renders through the core shell: the viewer lives in the canvas region,
        // the thread panel in the docked right region.
        cut.FindComponents<TmEditorShell>().Should().HaveCount(1);
        cut.Find("[data-region='canvas'] .tm-pdf-viewer").Should().NotBeNull(
            "the viewer (with its thumbnails) is the shell's canvas");
        cut.Find("[data-region='right'] [data-testid='pdf-annotator-panel']").Should().NotBeNull(
            "the annotator panel is the shell's right dock");

        // ShowPanel=false clears the right region entirely (no docked panel, no sheet).
        var hidden = RenderInLayout(TmLayoutMode.Desktop, Seeded(Highlight("h1")),
            p => p.Add(x => x.ShowPanel, false));
        hidden.FindAll("[data-region='right']").Should().BeEmpty();
    }

    [Fact]
    public void LayoutTablet_ToolsHaveLabelsBelow_PanelIsSideSheet()
    {
        var cut = RenderInLayout(TmLayoutMode.Tablet, Seeded(Highlight("h1")),
            p => p.Add(x => x.ToolbarLabels, ToolbarLabels.IconsWithText));

        // The annotator's tools show their labels UNDER the icon (the ribbon style of the
        // tablet reference) when ToolbarLabels shows labels at all.
        cut.Find("[data-testid='pdf-annotator-mode-browse']").ClassList
            .Should().Contain("tm-toolbar-btn--label-below");
        cut.Find("[data-testid='pdf-annotator-mode-draw']").ClassList
            .Should().Contain("tm-toolbar-btn--label-below");

        // The panel presents as the side sheet region of the shell (docked right, never inline
        // in the viewer column).
        cut.Find("[data-region='right'] [data-testid='pdf-annotator-panel']").Should().NotBeNull();
    }

    [Fact]
    public void LayoutMobile_HidesHighlightStampDrawTools_G2()
    {
        var cut = RenderInLayout(TmLayoutMode.Mobile, Seeded(Highlight("h1")));

        // G2: mobile is viewing + light edits — highlight, stamp and draw are not offered.
        cut.FindAll("[data-testid='pdf-annotator-mode-highlight']").Should().BeEmpty();
        cut.FindAll("[data-testid='pdf-annotator-mode-stamp']").Should().BeEmpty();
        cut.FindAll("[data-testid='pdf-annotator-mode-draw']").Should().BeEmpty();
        cut.Find("[data-testid='pdf-annotator-mode-browse']").Should().NotBeNull();
        cut.Find("[data-testid='pdf-annotator-mode-comment']").Should().NotBeNull();
    }

    [Fact]
    public void LayoutMobile_ToolPaletteIsHorizontallyScrollable()
    {
        var cut = RenderInLayout(TmLayoutMode.Mobile);

        // The palette is a horizontally scrollable strip (the G2 tile row), holding exactly
        // the two mobile tiles.
        var track = cut.Find(".tm-pdf-annotator__palette-track");
        track.Should().NotBeNull();
        track.QuerySelectorAll("[data-testid='pdf-annotator-mode-browse'], [data-testid='pdf-annotator-mode-comment']")
            .Should().HaveCount(2);
    }

    [Fact]
    public void LayoutMobile_ActionBar_ExportFlattenPrintMore()
    {
        var cut = RenderInLayout(TmLayoutMode.Mobile);

        // The F5 action bar: Export PDF | Export flattened | Print on the bar, everything
        // else (the threads entry) in the shared "More" menu.
        var bar = cut.Find(".tm-mobile-action-bar__bar");
        bar.QuerySelector("[data-action-id='export']").Should().NotBeNull();
        bar.QuerySelector("[data-action-id='export-flat']").Should().NotBeNull();
        bar.QuerySelector("[data-action-id='print']").Should().NotBeNull();

        cut.Find(".tm-mobile-action-bar__more").Click();
        cut.FindAll(".tm-overlay-panel-sheet [data-action-id='threads']")
            .Should().NotBeEmpty("the threads sheet opens from the action bar's More menu");
    }

    [Fact]
    public void LayoutMobile_ThreadMarkerOpensPanelSheetOnThread()
    {
        var cut = RenderInLayout(TmLayoutMode.Mobile, Seeded(Highlight("h1")),
            p => p.Add(x => x.ShowPanel, false));

        cut.FindAll(".tm-editor-shell__sheet").Should().BeEmpty("the sheet starts closed");

        // A highlight thread renders the highlight hit area; tapping it selects the thread.
        cut.WaitForElement("[data-testid='pdf-annotator-highlight']").Click();

        cut.WaitForAssertion(() =>
            cut.FindAll(".tm-editor-shell__sheet").Should().HaveCount(1,
                "tapping a thread marker opens the panel bottom sheet"));
        var sheet = cut.Find(".tm-editor-shell__sheet");
        sheet.QuerySelector("[data-testid='pdf-annotator-panel']").Should().NotBeNull();
        sheet.QuerySelector("[data-testid='pdf-annotator-thread']")
            .GetAttribute("aria-expanded").Should().Be("true", "the sheet opens on the tapped thread");
    }

    [Fact]
    public void LayoutMobile_SwitchFromDrawMode_FallsBackToBrowse()
    {
        // A host that pinned Draw (or restored it from state) on a phone gets Browse: the
        // editing tools do not exist on a mobile layout (G2).
        var cut = RenderInLayout(TmLayoutMode.Mobile, Seeded(Highlight("h1")),
            p => p.Add(x => x.Mode, PdfAnnotatorMode.Draw));

        cut.Find("[data-testid='pdf-annotator-mode-browse']").ClassList
            .Should().Contain("tm-pdf-annotator__tile--active",
                "the active edit mode falls back to Browse on a mobile layout");
    }

    // ── Phase 8 review fixes ─────────────────────────────────────────────────

    [Fact]
    public void MobileSheet_StartsClosed_ByDefault_DesktopKeepsPanelOpen()
    {
        // Review 3: the first resolution to mobile closes the sheet unless the host supplied
        // ShowPanel; the desktop default stays open.
        var mobile = RenderInLayout(TmLayoutMode.Mobile, Seeded(Highlight("h1")));
        mobile.FindAll(".tm-editor-shell__sheet").Should().BeEmpty();

        var desktop = RenderInLayout(TmLayoutMode.Desktop, Seeded(Highlight("h1")));
        desktop.Find("[data-region='right'] [data-testid='pdf-annotator-panel']").Should().NotBeNull();
    }

    [Fact]
    public async Task MobileSheet_SwitchToMobile_ClosesSheet_UnlessHostSuppliedShowPanel()
    {
        var states = new List<bool>();
        var cut = RenderInLayout(TmLayoutMode.Desktop, Seeded(Highlight("h1")), p => p
            .Add(x => x.ShowPanelChanged, EventCallback.Factory.Create<bool>(this, states.Add)));
        cut.Find("[data-region='right'] [data-testid='pdf-annotator-panel']").Should().NotBeNull();

        await cut.InvokeAsync(() => cut.Instance.SetParametersAsync(ParameterView.FromDictionary(
            new Dictionary<string, object?> { [nameof(TmPdfAnnotator.LayoutMode)] = TmLayoutMode.Mobile })));
        cut.Render();

        cut.FindAll(".tm-editor-shell__sheet").Should().BeEmpty(
            "a desktop→mobile switch closes the sheet (review 3)");
        states.Should().Contain(false, "the auto-close is announced through ShowPanelChanged");

        // A host that explicitly supplies ShowPanel keeps the control.
        var supplied = RenderInLayout(TmLayoutMode.Mobile, Seeded(Highlight("h1")),
            p => p.Add(x => x.ShowPanel, true));
        supplied.Find(".tm-editor-shell__sheet").Should().NotBeNull();
    }

    [Fact]
    public async Task MobileSheet_AutoTransientLayoutFlap_DoesNotCloseThePanel()
    {
        // Phase 12 review: under Auto the shell's container measurement can flap while the page
        // settles (a full-page screenshot, a transient reflow). The mobile sheet auto-close is
        // debounced, so an intermediate blip must NOT hide the open panel.
        // The debounce runs on a FakeTimeProvider: "the window has passed" means Advance crossed
        // it, so a loaded machine can never let the 150 ms timer fire between the two re-resolves.
        var clock = new Microsoft.Extensions.Time.Testing.FakeTimeProvider();
        Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions
            .AddSingleton<TimeProvider>(Services, clock);
        var cut = Render(Seeded(Highlight("h1")));
        var observer = cut.FindComponent<TmEditorShell>().FindComponent<TmLayoutObserver>();

        // A transient re-resolve to mobile leaves the panel visible (the close is pending) …
        await cut.InvokeAsync(() => observer.Instance.OnLayoutModeChanged("mobile"));
        cut.Find("[data-testid='pdf-annotator-panel']").Should().NotBeNull("a blip must not close the panel");

        // … and resolving back cancels the pending close for good, even past the window.
        await cut.InvokeAsync(() => observer.Instance.OnLayoutModeChanged("desktop"));
        clock.Advance(TimeSpan.FromMilliseconds(500));
        cut.Find("[data-testid='pdf-annotator-panel']")
            .Should().NotBeNull("the cancelled auto-close must stay cancelled");

        // A STABLE resolution to mobile still starts the sheet closed (after the debounce).
        await cut.InvokeAsync(() => observer.Instance.OnLayoutModeChanged("mobile"));
        cut.Find("[data-testid='pdf-annotator-panel']")
            .Should().NotBeNull("the close waits for the debounce window");
        clock.Advance(TimeSpan.FromMilliseconds(200));
        cut.WaitForState(() => cut.FindAll(".tm-editor-shell__sheet").Count == 0,
            TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void MobileSheetClose_RestoresFocusToTheOpener_ViaJsHelper()
    {
        var cut = RenderInLayout(TmLayoutMode.Mobile, Seeded(Highlight("h1")));

        // Opening from a marker remembers the opener (review 1) ...
        cut.WaitForElement("[data-testid='pdf-annotator-highlight']").Click();
        cut.WaitForAssertion(() =>
            JSInterop.Invocations.Count(i => i.Identifier == "tmPdfAnnotator.rememberFocus").Should().Be(1));

        // ... and closing the sheet returns focus through the JS helper (the opener, or the
        // first action-bar tile when the opener is gone — the helper decides).
        cut.Find(".tm-editor-shell__sheet-close").Click();
        cut.WaitForAssertion(() =>
            JSInterop.Invocations.Count(i => i.Identifier == "tmPdfAnnotator.focusOpener").Should().Be(1));
    }

    [Fact]
    public void MobileMarkerOpensSheetAtHalfSnap_ScrollsCardAndFocusesComposer()
    {
        var cut = RenderInLayout(TmLayoutMode.Mobile, Seeded(Highlight("h1")));

        cut.WaitForElement("[data-testid='pdf-annotator-highlight']").Click();

        cut.WaitForAssertion(() =>
            cut.FindComponent<TmEditorShell>().Instance.MobileSheetSnapIndex
                .Should().Be(1, "the sheet opens from a marker at the HALF snap, 0.7 (review 12)"));

        // The selected card is scrolled into view and the reply composer focused (review 12).
        cut.WaitForAssertion(() =>
            JSInterop.Invocations.Count(i => i.Identifier == "tmPdfAnnotator.scrollSelectedCardIntoView").Should().Be(1));
        var focus = JSInterop.Invocations.Single(i => i.Identifier == "tmPdfAnnotator.focusDescendant");
        focus.Arguments[^1].Should().Be("[data-testid='pdf-annotator-reply-input']");
    }

    [Fact]
    public void MobileDraft_ComposerInputIsFocusedViaJsHelper()
    {
        var cut = RenderInLayout(TmLayoutMode.Mobile);

        cut.Find("[data-testid='pdf-annotator-mode-comment']").Click();
        cut.InvokeAsync(async () => await cut.Find("[data-testid='pdf-annotator-surface']")
            .TriggerEventAsync("onclick", new MouseEventArgs { OffsetX = 400, OffsetY = 500 }));

        cut.WaitForElement("[data-testid='pdf-annotator-new']");
        var focus = JSInterop.Invocations.Single(i => i.Identifier == "tmPdfAnnotator.focusDescendant");
        focus.Arguments[^1].Should().Be("[data-testid='pdf-annotator-new-input']",
            "opening the sheet from a new draft focuses the draft textarea (review 12)");
    }

    [Fact]
    public void MobileBar_FoldsViewerPagesAndSearch_IntoSingleActionBar()
    {
        var cut = RenderInLayout(TmLayoutMode.Mobile, configure: p => p
            .Add(x => x.ShowThumbnails, true)
            .Add(x => x.ShowSearch, true));

        // Exactly ONE action bar: the viewer renders none of its own (review 4).
        cut.FindAll(".tm-mobile-action-bar__bar").Should().HaveCount(1);
        cut.FindComponent<TmPdfViewer>().Instance.ShowMobileActionBar.Should().BeFalse();

        var bar = cut.Find(".tm-mobile-action-bar__bar");
        bar.QuerySelector("[data-action-id='pages']").Should().NotBeNull();
        bar.QuerySelector("[data-action-id='search']").Should().NotBeNull();

        // The tiles drive the viewer through its public API: Pages opens the thumbnails sheet…
        bar.QuerySelector("[data-action-id='pages']")!.Click();
        cut.WaitForAssertion(() =>
            cut.Find(".tm-pdf-viewer__thumbnails-list--sheet").Should().NotBeNull());

        // …and Search opens the viewer's search input.
        cut.Find(".tm-mobile-action-bar__bar").QuerySelector("[data-action-id='search']")!.Click();
        cut.WaitForAssertion(() =>
            cut.Find("[data-testid='pdf-search-input']").Should().NotBeNull());
    }

    [Fact]
    public void DesktopToolbar_IconsWithText_ShowsInlineLabels()
    {
        var cut = RenderInLayout(TmLayoutMode.Desktop, Seeded(Highlight("h1")),
            p => p.Add(x => x.ToolbarLabels, ToolbarLabels.IconsWithText));

        // Review 5: on a desktop layout the labels sit INLINE (the viewer's rule), so the
        // merged toolbar's annotator pills show their text like the viewer's own buttons.
        var browse = cut.Find("[data-testid='pdf-annotator-mode-browse']");
        browse.QuerySelector(".tm-toolbar-btn-text").Should().NotBeNull("the label renders on desktop");
        browse.ClassList.Should().NotContain("tm-toolbar-btn--label-below");
        browse.ClassList.Should().NotContain("tm-toolbar-btn--icon-only");
        cut.Find(".tm-pdf-viewer__toolbar").TextContent.Should().Contain("Browse annotations");
    }

    [Fact]
    public void Tablet_ToolsRenderInTheirOwnFullWidthRow_NotInTheViewerToolbar()
    {
        var cut = RenderInLayout(TmLayoutMode.Tablet, Seeded(Highlight("h1")),
            p => p.Add(x => x.ToolbarLabels, ToolbarLabels.IconsWithText));

        // Review 7: the tools + export group are the annotator's own full-width labelled row
        // above the canvas — not squeezed into the viewer toolbar's More overflow.
        var row = cut.Find("[data-testid='pdf-annotator-toolbar']");
        row.QuerySelector("[data-testid='pdf-annotator-mode-browse']").Should().NotBeNull();
        row.QuerySelector("[data-testid='pdf-annotator-export']").Should().NotBeNull();

        var labelled = row.QuerySelectorAll("[data-testid='pdf-annotator-tools'] .tm-toolbar-btn--label-below")
            .ToList();
        labelled.Should().HaveCountGreaterThanOrEqualTo(5, "the five tools carry visible labels");
        labelled.Select(b => b.QuerySelector(".tm-toolbar-btn-text")?.TextContent.Trim())
            .Should().OnlyContain(t => !string.IsNullOrEmpty(t));

        cut.Find(".tm-pdf-viewer__toolbar").QuerySelector("[data-testid='pdf-annotator-mode-browse']")
            .Should().BeNull("the annotator injects nothing into the viewer toolbar on tablet");
    }

    [Fact]
    public void Tablet_ThumbnailsDefaultCollapsed_AndForwardedToTheHost()
    {
        bool? raised = null;
        var cut = RenderInLayout(TmLayoutMode.Tablet, configure: p => p
            .Add(x => x.ShowThumbnails, true)
            .Add(x => x.ThumbnailsCollapsedChanged, EventCallback.Factory.Create<bool>(this, v => raised = v)));

        // Review 8: the viewer's guarded tablet default reaches the annotator's two-way shadow,
        // so the next parameter pass carries true back instead of overriding the default.
        cut.WaitForAssertion(() =>
            cut.FindComponent<TmPdfViewer>().Instance.ThumbnailsCollapsed.Should().BeTrue());
        raised.Should().BeTrue();

        cut.Render();
        cut.FindComponent<TmPdfViewer>().Instance.ThumbnailsCollapsed.Should().BeTrue(
            "an unbound host re-render must not expand the tablet default");
    }

    [Fact]
    public void ShellCollapseChevron_MapsToPanelClosed_AndReopenToggleRestores()
    {
        bool? raised = null;
        var cut = RenderInLayout(TmLayoutMode.Desktop, Seeded(Highlight("h1")), p => p
            .Add(x => x.ShowPanelChanged, EventCallback.Factory.Create<bool>(this, v => raised = v)));

        // Review 2: the shell's collapse chevron is wired two-way — collapsing the right panel
        // closes it and raises ShowPanelChanged(false).
        cut.Find(".tm-editor-shell__panel-toggle--right").Click();
        raised.Should().BeFalse();
        cut.FindAll("[data-region='right']").Should().BeEmpty();

        // The slim reopen toggle (the "rail" affordance) brings the panel back.
        cut.Find(".tm-editor-shell__panel-toggle--right").Click();
        raised.Should().BeTrue();
        cut.WaitForAssertion(() =>
            cut.Find("[data-region='right'] [data-testid='pdf-annotator-panel']").Should().NotBeNull());
    }
}
