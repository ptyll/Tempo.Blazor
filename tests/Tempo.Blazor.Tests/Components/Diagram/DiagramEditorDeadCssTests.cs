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
/// </summary>
public class DiagramEditorDeadCssTests
{
    private const string DiagramEditorCssPath =
        "src/Tempo.Blazor.DiagramEditor/wwwroot/css/components/_diagram-editor.css";

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
}
