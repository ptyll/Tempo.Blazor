using System.Text.RegularExpressions;
using FluentAssertions;
using Tempo.Blazor.Tests.Theme;

namespace Tempo.Blazor.Tests.Layout;

/// <summary>
/// The phone chrome's CSS contract (plan 5d396592, phase 4): the frame's mobile rules stay on semantic
/// tokens (no palette literal, dark values come from the token layer), the bottom navigation becomes a
/// layout ROW of the frame instead of viewport-fixed chrome, the safe-area insets are honoured, the
/// coarse-pointer targets reach <c>--tm-touch-target</c>, and the reduced-motion block needs no new
/// entry because the frame declares no animation of its own.
/// </summary>
public class TmAppShellMobileCssTests
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(5);

    /// <summary>A colour literal outside a custom-property definition — the stylesheet rule.</summary>
    private static readonly Regex ColorLiteral =
        new(@"#[0-9a-fA-F]{3,8}\b|rgba?\(|hsla?\(", RegexOptions.Compiled, RegexTimeout);

    private static readonly Regex ManualDarkBlock =
        new(@"\[data-theme\s*=\s*[""']dark[""']\]|\.tm-dark\b", RegexOptions.Compiled, RegexTimeout);

    private static string Sheet()
        => File.ReadAllText(Path.Combine(ThemeCss.RepositoryRoot().FullName,
            "src", "Tempo.Blazor", "wwwroot", "css", "components", "_app-shell.css"));

    private static string Bundle()
        => File.ReadAllText(Path.Combine(ThemeCss.RepositoryRoot().FullName,
            "src", "Tempo.Blazor", "wwwroot", "css", "tempo-blazor.bundled.css"));

    [Fact]
    public void TheFrameSheet_UsesSemanticTokensOnly()
    {
        var css = Sheet();

        ColorLiteral.Matches(css).Should().BeEmpty("the frame paints through --tm-* tokens; the dark theme redefines tokens, never this file");
        ManualDarkBlock.Matches(css).Should().BeEmpty("no component-local dark block: dark values live in tokens-dark.css");
    }

    [Fact]
    public void TheBottomNavigation_IsALayoutRow_NotViewportFixedChrome()
    {
        var css = Sheet();

        css.Should().Contain(".tm-app-shell__bottom-nav", "the bar is a region of the frame");
        css.Should().Contain(".tm-bottom-nav--app-shell");
        css.Should().Contain("position: static",
            "the frame classifies its own width: a viewport-fixed bar would also be pinned by the frame's size containment");
        css.Should().Contain("env(safe-area-inset-bottom", "a phone with a home indicator needs its safe area");
    }

    [Fact]
    public void ThePhoneChrome_HonoursTheSafeAreaInsets()
    {
        var css = Sheet();

        css.Should().Contain("env(safe-area-inset-left", "the drawer sits against the left notch edge");
        css.Should().Contain("env(safe-area-inset-right");
    }

    [Fact]
    public void TheCoarsePointerTargets_ReachTheSharedTouchTargetToken()
    {
        var css = Sheet();

        css.Should().Contain("@media (pointer: coarse)");
        css.Should().Contain("--tm-touch-target",
            "a 44px tap target on a coarse pointer — the same token every other touch surface uses");
    }

    [Fact]
    public void TheNewPhoneClasses_ShipInTheBundle()
    {
        var bundle = Bundle();

        foreach (var cssClass in new[]
        {
            ".tm-app-shell__menu-button",
            ".tm-app-shell__bottom-nav",
            ".tm-bottom-nav--app-shell",
            ".tm-app-shell__overlay--mobile",
            ".tm-app-shell__topbar--menu",
            ".tm-app-shell__topbar-content",
        })
        {
            bundle.Should().Contain(cssClass, $"a host reading only the bundle must see {cssClass} (rebuild regenerates it)");
        }
    }

    [Fact]
    public void TheFrameDeclaresNoNewAnimatedSubject()
    {
        var css = Sheet();

        // The mobile transitions a user sees come from TmDrawer / TmBottomNavigation, whose subjects are
        // already enumerated in animations.css. A new transition here would need a reduced-motion entry.
        Regex.IsMatch(css, @"(^|;)\s*(transition|animation)(-[a-z-]+)?\s*:", RegexOptions.Multiline)
            .Should().BeFalse("the frame animates nothing of its own, so the reduced-motion block has nothing to add");
    }
}
