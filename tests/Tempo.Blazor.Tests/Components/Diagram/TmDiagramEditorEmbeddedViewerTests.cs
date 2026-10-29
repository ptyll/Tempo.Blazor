using Bunit;
using FluentAssertions;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.Components.Diagram;
using Tempo.Blazor.Components.Diagram.Models;
using Tempo.Blazor.Tests.Diagram;
using Xunit;

namespace Tempo.Blazor.Tests.Components.Diagram;

/// <summary>
/// Fáze 9 (plan 7480f23e): the additive embedded-viewer API of <see cref="TmDiagramEditor"/> —
/// the knobs a nested host (the Modeling preview and its "Open in editor" dialog) uses instead of
/// CSS on the editor's internal DOM: <c>ShowPageTabs</c>, the public
/// <c>FitToViewAsync(DiagramFitMode)</c> (+ <c>FitOnDocumentLoad</c>), the explicit
/// <c>ShowMobileZoomOverlay</c> rule and <c>MinimapInitiallyCollapsed</c>. Every knob is additive:
/// its default is today's behavior.
/// </summary>
public class TmDiagramEditorEmbeddedViewerTests : DiagramTestBase
{
    private static DiagramDocument CreateDocument(int pages = 1)
    {
        var doc = new DiagramDocument();
        doc.Nodes.Add(new DiagramNode { Id = "n1", StencilId = "general.rectangle", X = 100, Y = 100, W = 120, H = 60 });
        for (var i = 1; i < pages; i++)
        {
            doc.Pages.Add(new DiagramPage { Name = $"Page {i + 1}" });
        }
        return doc;
    }

    private IRenderedComponent<TmDiagramEditor> RenderEditor(
        TmLayoutMode mode,
        Action<ComponentParameterCollectionBuilder<TmDiagramEditor>>? extra = null)
    {
        return Render<TmDiagramEditor>(p =>
        {
            p.Add(e => e.Document, CreateDocument());
            p.Add(e => e.LayoutMode, mode);
            extra?.Invoke(p);
        });
    }

    // ── ShowPageTabs ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData(TmLayoutMode.Desktop)]
    [InlineData(TmLayoutMode.Tablet)]
    [InlineData(TmLayoutMode.Mobile)]
    public void ShowPageTabs_false_does_not_render_page_tabs_row_in_any_layout(TmLayoutMode mode)
    {
        using var cut = RenderEditor(mode, p => p.Add(e => e.ShowPageTabs, false));

        cut.FindAll(".tm-diagram-editor__page-tabs").Should().BeEmpty(
            "ShowPageTabs=false drops the tab row in EVERY layout (public API, never CSS)");
    }

    [Fact]
    public void ShowPageTabs_default_renders_page_tabs()
    {
        using var cut = RenderEditor(TmLayoutMode.Desktop);

        cut.Find(".tm-diagram-editor__page-tabs").Should().NotBeNull(
            "the default (true) keeps today's page-tabs row");
    }

    // ── FitToViewAsync / DiagramFitMode ──────────────────────────────────────

    [Fact]
    public async Task FitToViewAsync_invokes_canvas_fit_and_updates_zoom_label()
    {
        JSInterop.Setup<double>("tmDiagramEditor.fitToView", _ => true).SetResult(0.5);
        using var cut = RenderEditor(TmLayoutMode.Desktop);

        await cut.InvokeAsync(() => cut.Instance.FitToViewAsync());

        JSInterop.Invocations["tmDiagramEditor.fitToView"].Should().NotBeEmpty(
            "the fit runs the canvas' zoom-to-fit path");
        cut.WaitForAssertion(() =>
            cut.Find(".tm-diagram-editor__zoom-label").TextContent.Should().Be("50%",
                "the fitted scale re-renders the zoom label"));
    }

    [Fact]
    public async Task FitToViewAsync_width_mode_passes_width_flag_to_js()
    {
        JSInterop.Setup<double>("tmDiagramEditor.fitToView", _ => true).SetResult(0.5);
        using var cut = RenderEditor(TmLayoutMode.Desktop);

        await cut.InvokeAsync(() => cut.Instance.FitToViewAsync(DiagramFitMode.Width));

        var invocation = JSInterop.Invocations["tmDiagramEditor.fitToView"].Single();
        invocation.Arguments.Should().Contain("width",
            "width mode asks the JS engine to scale by the container width only");
    }

    [Fact]
    public async Task FitToViewAsync_page_mode_keeps_default_js_contract()
    {
        JSInterop.Setup<double>("tmDiagramEditor.fitToView", _ => true).SetResult(0.5);
        using var cut = RenderEditor(TmLayoutMode.Desktop);

        await cut.InvokeAsync(() => cut.Instance.FitToViewAsync());

        var invocation = JSInterop.Invocations["tmDiagramEditor.fitToView"].Single();
        invocation.Arguments.Should().NotContain("width",
            "the page fit keeps today's two-argument contract (no width flag)");
    }

    [Fact]
    public async Task FitOnDocumentLoad_fits_once_per_document_identity()
    {
        JSInterop.Setup<double>("tmDiagramEditor.fitToView", _ => true).SetResult(0.5);
        var doc = CreateDocument();

        using var cut = Render<TmDiagramEditor>(p => p
            .Add(e => e.Document, doc)
            .Add(e => e.FitOnDocumentLoad, DiagramFitMode.Width));

        cut.WaitForAssertion(() => JSInterop.Invocations["tmDiagramEditor.fitToView"]
            .Should().HaveCount(1, "the first render of a new document fits once"));

        // The same document identity re-rendering must not fit again.
        cut.Render(p => p.Add(e => e.Document, doc));
        cut.WaitForAssertion(() => JSInterop.Invocations["tmDiagramEditor.fitToView"]
            .Should().HaveCount(1, "a re-render of the SAME document does not fit again"));

        // A NEW document instance fits again (the Modeling dialog hands a fresh document over).
        cut.Render(p => p.Add(e => e.Document, CreateDocument()));
        cut.WaitForAssertion(() => JSInterop.Invocations["tmDiagramEditor.fitToView"]
            .Should().HaveCount(2, "a new document identity fits again"));
    }

    [Fact]
    public async Task FitOnDocumentLoad_default_null_never_fits()
    {
        using var cut = RenderEditor(TmLayoutMode.Desktop);

        cut.Render(p => p.Add(e => e.Document, CreateDocument()));
        cut.WaitForAssertion(() => JSInterop.Invocations
            .Where(i => i.Identifier == "tmDiagramEditor.fitToView")
            .Should().BeEmpty("the default (null) keeps today's no-auto-fit behavior"));
        await Task.CompletedTask;
    }

    // ── ShowMobileZoomOverlay ────────────────────────────────────────────────

    [Fact]
    public void ShowMobileZoomOverlay_true_renders_pill_without_toolbar_on_mobile()
    {
        using var cut = RenderEditor(TmLayoutMode.Mobile, p => p
            .Add(e => e.ShowToolbar, false)
            .Add(e => e.ShowMobileZoomOverlay, true));

        cut.Find(".tm-diagram-editor__zoom-overlay").Should().NotBeNull(
            "an explicit true shows the floating pill even when the toolbar is hidden");
    }

    [Fact]
    public void ShowMobileZoomOverlay_false_hides_pill()
    {
        using var cut = RenderEditor(TmLayoutMode.Mobile, p => p
            .Add(e => e.ShowMobileZoomOverlay, false));

        cut.FindAll(".tm-diagram-editor__zoom-overlay").Should().BeEmpty(
            "an explicit false hides the pill even where today's rule would show it");
    }

    [Fact]
    public void ShowMobileZoomOverlay_null_keeps_todays_rule()
    {
        // Default null + hidden toolbar: no pill (today's rule needs ShowToolbar).
        using var hidden = RenderEditor(TmLayoutMode.Mobile, p => p.Add(e => e.ShowToolbar, false));
        hidden.FindAll(".tm-diagram-editor__zoom-overlay").Should().BeEmpty(
            "the default keeps today's rule: no toolbar → no pill");

        // Default null + toolbar with the Zoom group: the pill shows on mobile.
        using var shown = RenderEditor(TmLayoutMode.Mobile);
        shown.Find(".tm-diagram-editor__zoom-overlay").Should().NotBeNull(
            "the default keeps today's rule: mobile + toolbar + Zoom group → pill");
    }

    // ── MinimapInitiallyCollapsed ────────────────────────────────────────────

    [Fact]
    public void MinimapInitiallyCollapsed_true_starts_tablet_overlay_collapsed_and_reopen_button_works()
    {
        using var cut = RenderEditor(TmLayoutMode.Tablet, p => p.Add(e => e.MinimapInitiallyCollapsed, true));

        cut.FindAll(".tm-diagram-editor__minimap-overlay").Should().BeEmpty(
            "the tablet overlay starts collapsed");
        var reopen = cut.Find("[data-testid='diagram-editor-minimap-reopen']");
        reopen.Should().NotBeNull("the collapsed overlay keeps a reopen path");
        reopen.Click();
        cut.Find(".tm-diagram-editor__minimap-overlay").Should().NotBeNull(
            "the reopen button expands the overlay (the user's choice wins after start)");
    }

    [Fact]
    public void MinimapInitiallyCollapsed_does_not_override_mobile_X1_snapshot()
    {
        using var cut = RenderEditor(TmLayoutMode.Mobile, p => p.Add(e => e.MinimapInitiallyCollapsed, true));

        cut.FindAll(".tm-diagram-editor__minimap-overlay").Should().BeEmpty(
            "the mobile X1 rule (minimap hidden, toggled from the ⋯ menu) is unchanged");
    }

    [Fact]
    public void MinimapInitiallyCollapsed_default_starts_expanded()
    {
        using var cut = RenderEditor(TmLayoutMode.Tablet);

        cut.Find(".tm-diagram-editor__minimap-overlay").Should().NotBeNull(
            "the default (false) keeps today's expanded overlay");
    }
}
