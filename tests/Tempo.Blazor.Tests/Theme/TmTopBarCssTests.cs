using FluentAssertions;

namespace Tempo.Blazor.Tests.Theme;

/// <summary>
/// Plan 92e96b25, TG-003 — the narrow-width stylesheet contract of
/// <see cref="Tempo.Blazor.Components.Layout.TmTopBar"/>.
/// <para>
/// The bar answers its OWN container (never the viewport): the root declares a named inline-size
/// container, a <c>@container tm-topbar (width &lt; 480px)</c> block hides the Auto user name (the
/// avatar stays) and collapses the command palette trigger to its icon (the localized
/// <c>aria-label</c> stays the accessible name), the brand and the actions never wrap or overflow,
/// and the new rules paint with <c>--tm-*</c> tokens only and introduce no new animated subject.
/// </para>
/// </summary>
public sealed class TmTopBarCssTests
{
    private static string TopBarCss()
    {
        var path = Path.Combine(
            ThemeCss.RepositoryRoot().FullName,
            "src", "Tempo.Blazor", "wwwroot", "css", "components", "_topbar.css");
        File.Exists(path).Should().BeTrue("the partial ships next to the component that reads it: " + path);
        return ThemeCss.StripComments(File.ReadAllText(path));
    }

    [Fact]
    public void TheRoot_Declares_A_Named_Size_Container()
    {
        var css = TopBarCss();

        css.Should().Contain("container-type: inline-size");
        css.Should().Contain("container-name: tm-topbar");
    }

    [Fact]
    public void TheNarrow_Query_Reads_The_Bar_Itself_At_480()
    {
        var css = TopBarCss();

        // 480px = 30rem: component-local density thresholds are declared in rem (the scheduler and
        // chart precedents) — TmBreakpoints guards the four layout widths in px, and
        // TmBreakpointsTests proves no px width outside them appears in any @container condition.
        css.Should().Contain("@container tm-topbar (width < 30rem)",
            "the 480px breakpoint is measured on the bar's own box, so a bar in a wide page still answers a narrow slot");
    }

    [Fact]
    public void TheAuto_User_Name_Hides_The_Always_Name_Survives()
    {
        var css = TopBarCss();

        css.Should().Contain(".tm-topbar[data-user-name-display=\"auto\"] .tm-topbar-user-name",
            "only the Auto name gives way — a host that pinned Always keeps the name at every width");
        css.Should().NotContain("[data-user-name-display=\"always\"] .tm-topbar-user-name",
            "there must be no rule that hides the Always name");

        var hide = css.IndexOf(".tm-topbar-user-name", css.IndexOf("@container tm-topbar", StringComparison.Ordinal), StringComparison.Ordinal);
        hide.Should().BeGreaterThan(-1, "the hiding rule lives inside the narrow container block");
        var block = css.Substring(css.IndexOf("@container tm-topbar", StringComparison.Ordinal));
        block.Should().Contain("display: none");
    }

    [Fact]
    public void TheSearch_Trigger_Collapses_To_Its_Icon_Keeping_The_Accessible_Name()
    {
        var css = TopBarCss();

        var start = css.IndexOf("@container tm-topbar", StringComparison.Ordinal);
        var block = css.Substring(start);
        block.Should().Contain(".tm-topbar-search-hint");
        block.Should().Contain("display: none",
            "the Ctrl+K hint is a wide-bar luxury; the icon button stays with its localized aria-label");
        block.Should().Contain(".tm-topbar-search-trigger",
            "the trigger itself narrows to a square icon button");
    }

    [Fact]
    public void TheActions_Never_Wrap_Or_Shrink()
    {
        var css = TopBarCss();

        css.Should().Contain("flex-shrink: 0", "the actions (and the account button) must stay on screen");
        css.Should().Contain("min-width: 0", "the brand gives way instead of pushing the actions out");
        css.Should().NotContain("flex-wrap: wrap",
            "a wrapped bar doubles its height and pushes the page down — the actions must never wrap");
    }

    [Fact]
    public void TheNarrow_Rules_Paint_With_Tokens_Only_And_Animate_Nothing_New()
    {
        var css = TopBarCss();

        var start = css.IndexOf("@container tm-topbar", StringComparison.Ordinal);
        var block = css.Substring(start);

        System.Text.RegularExpressions.Regex.Matches(block, "#[0-9a-fA-F]{3,8}\\b")
            .Should().BeEmpty("every value in the narrow block resolves a --tm-* token, never a literal colour");
        block.Should().NotContain("animation",
            "narrowing the bar is a layout response, not a motion — no new animated subject");
        block.Should().NotContain("transition",
            "the collapse is instant like the toolbar's label drop — a transition here would be movement the reduced-motion block has to know about");
    }
}
