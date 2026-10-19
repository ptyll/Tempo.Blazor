using FluentAssertions;
using Tempo.Blazor.Tests.Theme;

namespace Tempo.Blazor.Tests.Components.Diagram;

/// <summary>
/// Guards U12: rules for classes no component emits anymore must not resurrect in the package
/// stylesheet — <c>tm-toolbar-btn--active</c> (the pressed state is painted by the core
/// <c>.tm-toolbar-btn[aria-pressed="true"]</c>), <c>tm-diagram-editor__body</c> (the editor shell
/// owns the body region since the redesign) and <c>tm-diagram-template-card__tags</c> (tags were
/// deduplicated into chips in D15). Also no <c>max-width: 480px</c> literal, which a later
/// <c>width &lt; 640px</c> container rule would silently override.
/// Carry-forward sweep (1ddab088): legacy pre-redesign orphans removed from
/// <c>_diagram-editor.css</c> — the toolbox/props/editor-sheet duplicates of classes the
/// redesign moved to <c>tm-diagram-toolbox__*</c>/<c>tm-editor-shell__sheet</c>, the dead
/// collapsible-section / toggles / align / mixed property-panel rules, the
/// <c>tm-diagram-node__table-cell--context-target</c> marker and the arrow-select option hover.
/// </summary>
public class DiagramEditorDeadCssTests
{
    private const string DiagramEditorCssPath =
        "src/Tempo.Blazor.DiagramEditor/wwwroot/css/components/_diagram-editor.css";

    private const string ArrowSelectCssPath =
        "src/Tempo.Blazor.DiagramEditor/Components/Diagram/TmDiagramArrowSelect.razor.css";

    private static string Css() => ThemeCss.StripComments(File.ReadAllText(
        Path.Combine(ThemeCss.RepositoryRoot().FullName, DiagramEditorCssPath)));

    [Fact]
    public void DiagramEditorCss_HasNoRulesForRemovedClasses()
    {
        var css = Css();

        css.Should().NotContain("tm-toolbar-btn--active");
        css.Should().NotContain("tm-diagram-editor__body");
        css.Should().NotContain("tm-diagram-template-card__tags");
        css.Should().NotContain("max-width: 480px",
            "a 480px query always loses to the later width<640 container rule — class-based bands only (D2)");
    }

    [Fact]
    public void DiagramEditorCss_HasNoRulesForRedesignOrphans()
    {
        var css = Css();

        // Legacy toolbox chrome: TmDiagramToolbox paints tm-diagram-toolbox__*; only __toolbox-wrap
        // (the dock slot) and __toolbox (padding) remain editor-owned.
        css.Should().NotContain("tm-diagram-editor__toolbox-category");
        css.Should().NotContain("tm-diagram-editor__toolbox-item");
        css.Should().NotContain("tm-diagram-editor__toolbox-icon");
        css.Should().NotContain("tm-diagram-editor__toolbox-label");

        // Legacy properties chrome: only __props-wrap/__props-input are still emitted.
        css.Should().NotContain("tm-diagram-editor__props-title");
        css.Should().NotContain("tm-diagram-editor__props-group");
        css.Should().NotContain("tm-diagram-editor__props-label");
        css.Should().NotContain("tm-diagram-editor__props-textarea");

        // The mobile panels sheet is tm-editor-shell__sheet (TmEditorShell), never an
        // editor-owned __sheet.
        css.Should().NotContain("tm-diagram-editor__sheet");

        // Dead property-panel subjects (no producer in razor/cs/js).
        css.Should().NotContain("tm-diagram-properties__action-btn");
        css.Should().NotContain("tm-diagram-properties__actions");
        css.Should().NotContain("tm-diagram-properties__align-grid");
        css.Should().NotContain("tm-diagram-properties__toggles");
        css.Should().NotContain("tm-diagram-properties__toggle-btn");
        css.Should().NotContain("tm-diagram-properties__mixed");
        css.Should().NotContain("tm-diagram-properties__section--collapsible");
        css.Should().NotContain("tm-diagram-properties__section-body");
        // NB: bare tm-diagram-properties__color cannot be guarded by NotContain — it is a
        // substring of the still-live __color-wrap/__color-swatch/__color-value.

        // The context-menu table-cell marker is no longer emitted.
        css.Should().NotContain("tm-diagram-node__table-cell--context-target");
    }

    [Fact]
    public void ArrowSelectCss_HasNoRulesForRemovedOptionClass()
    {
        var css = ThemeCss.StripComments(File.ReadAllText(
            Path.Combine(ThemeCss.RepositoryRoot().FullName, ArrowSelectCssPath)));

        // Options paint their state through inline styles (GetOptionStyle) — the __option
        // class has no producer.
        css.Should().NotContain("tm-diagram-arrow-select__option");
    }
}
