using System.Text.RegularExpressions;
using FluentAssertions;
using Tempo.Blazor.Tests.Theme;

namespace Tempo.Blazor.Tests.Files;

/// <summary>
/// CSS contract of <c>TmPdfViewer.razor.css</c>. A merge (<c>bb606ca8</c>) once reverted the file to an older copy and
/// took the FZE-11 floating search card and the FZE-13 continuous overlay-slot rules with it; only 11 slow E2E specs
/// noticed. These rules are what those specs depend on, so a regression now fails here in milliseconds.
/// </summary>
public sealed class TmPdfViewerCssTests
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(5);

    private static string Css() =>
        ThemeCss.StripComments(File.ReadAllText(Path.Combine(
            ThemeCss.RepositoryRoot().FullName, "src", "Tempo.Blazor.PdfViewer", "Components", "Files", "TmPdfViewer.razor.css")));

    // All declaration blocks whose rule is exactly <selector> (the stylesheet may declare one in several places).
    private static string RuleBody(string css, string selector)
    {
        var matches = Regex.Matches(
            css,
            @"(?:^|[}\s])" + Regex.Escape(selector) + @"\s*\{(?<body>[^{}]*)\}",
            RegexOptions.None,
            RegexTimeout);
        matches.Should().NotBeEmpty("the stylesheet must keep a rule for {0}", selector);
        return string.Join("\n", matches.Select(match => match.Groups["body"].Value));
    }

    [Fact]
    public void SearchCard_Rule_Exists()
    {
        var css = Css();

        css.Should().Contain(".tm-pdf-viewer__search-card {");
        RuleBody(css, ".tm-pdf-viewer__search-card").Should().Contain("position: absolute",
            "the FZE-11 search card floats over the viewer body");
    }

    [Fact]
    public void SearchCard_DockedPanelOffset_UsesTheCommentsPanelWidthKnob()
    {
        var css = Css();

        css.Should().Contain("--tm-pdf-comments-panel-width");
        Regex.IsMatch(css, @"--tm-pdf-comments-panel-width:\s*\d+px\s*;", RegexOptions.None, RegexTimeout)
            .Should().BeTrue("the viewer root declares the docked comments panel width knob");

        RuleBody(css, ".tm-pdf-viewer__search-card--docked-panel").Should().Contain("var(--tm-pdf-comments-panel-width)",
            "the card must not overlap the docked comments panel");
    }

    [Fact]
    public void ContinuousWrap_IsAPositioningContext_ForTheAnnotationSlots()
    {
        // FZE-13: the Blazor-owned overlay slots position against the scroll content; without position: relative
        // they drift by the wrap's offset.
        Regex.IsMatch(RuleBody(Css(), ".tm-pdf-viewer__continuous-wrap"), @"position:\s*relative\s*;", RegexOptions.None, RegexTimeout)
            .Should().BeTrue("the continuous wrap is the containing block of the annotation slots");
    }

    [Fact]
    public void ContinuousTextLayer_SpanRule_ReachesTheJsOwnedPages_ThroughDeep()
    {
        // The pages are created by pdf-viewer.js, so a scoped selector never matches them: the rule needs ::deep.
        Css().Should().Contain(".tm-pdf-viewer__continuous-wrap ::deep .tm-pdf-viewer__text-layer span");
    }

    [Fact]
    public void ViewModeSegment_Rules_AreAnchoredOnTheViewerRoot_ThroughDeep()
    {
        var css = Css();

        // The segmented control is a child component, so its markup carries no scope attribute of this stylesheet.
        css.Should().Contain(".tm-pdf-viewer ::deep .tm-pdf-viewer__view-mode {");
        css.Should().Contain(".tm-pdf-viewer ::deep .tm-pdf-viewer__view-mode .tm-radio-group-options");
        css.Should().Contain(".tm-pdf-viewer ::deep .tm-pdf-viewer__view-mode .tm-radio-segment");
    }

    [Fact]
    public void SearchCount_EmptyModifier_Exists_AndTheUnusedAnnotationsHintIsGone()
    {
        var css = Css();

        css.Should().Contain(".tm-pdf-viewer__search-count--empty");
        css.Should().NotContain(".tm-pdf-viewer__annotations-hint", "nothing renders that class any more");
    }
}
