using FluentAssertions;
using Tempo.Blazor.Tests.Theme;

namespace Tempo.Blazor.Tests.Components.Inputs;

/// <summary>
/// The narrow-viewport CSS contract of TmOtpInput (fáze 6 review): a cell never shrinks below
/// the 24px spacing-token floor, and under a narrow container the row wraps per group (each
/// group is one flex item, so a wrap never orphans a separator or overflows a 320px phone).
/// The root carries inline-size containment so the cells' intrinsic min-content can never force
/// a flex/grid host wider than its track (an &lt;input&gt;'s intrinsic minimum is ~20ch — without
/// the containment a 320px page would land in the shell's overflow-x clip instead of shrinking).
/// </summary>
public sealed class TmOtpInputCssTests
{
    private static string Sheet()
        => File.ReadAllText(Path.Combine(ThemeCss.RepositoryRoot().FullName,
            "src", "Tempo.Blazor", "wwwroot", "css", "components", "_otp-input.css"));

    private static string Block(string css, string selector)
    {
        var start = css.IndexOf(selector, StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0, $"{selector} must exist");
        var open = css.IndexOf('{', start);
        var depth = 0;
        for (var i = open; i < css.Length; i++)
        {
            if (css[i] == '{') depth++;
            else if (css[i] == '}' && --depth == 0) return css[start..(i + 1)];
        }

        return css[start..];
    }

    [Fact]
    public void Cells_KeepThe24pxFloor_ThroughTheSpacingToken()
    {
        var cell = Block(Sheet(), ".tm-otp-input__cell {");

        cell.Should().Contain("min-inline-size: var(--tm-space-6",
            "a cell must never shrink below 24px (--tm-space-6), even in a 320px viewport");
        cell.Should().NotContain("min-width: 0",
            "min-width: 0 would let flexbox crush the cell under the tappable floor");
    }

    [Fact]
    public void TheRoot_DoesNotPropagateItsContentIntrinsicSize_ToFlexHosts()
    {
        var root = Block(Sheet(), ".tm-otp-input {");

        root.Should().Contain("contain: inline-size",
            "an <input>'s intrinsic minimum is ~20ch per cell; without inline-size containment a flex or grid host "
            + "of TmOtpInput would be forced wider than its track and the page would land in the overflow-x clip");
    }

    [Fact]
    public void TheRow_WrapsLazily_PerGroup()
    {
        var css = Sheet();

        // No @container query: the repo lints container widths to the TmBreakpoints constants
        // (TmBreakpointsTests.ContainerQueries_UseRangeSyntax_AndOnlyTheFourConstants) — wrapping
        // is instead lazy, because a group is one flex item and flex only wraps when the groups do
        // not fit.
        css.Should().NotContain("@container tmotp");
        var row = Block(css, ".tm-otp-input__row {");
        row.Should().Contain("display: flex");
        row.Should().Contain("flex-wrap: wrap",
            "a grouped 8-digit backup code or a 6-digit code must stack its groups on a 320px phone");
        Block(css, ".tm-otp-input__group {").Should().Contain("display: flex",
            "each group (cells + separator) is one flex item, so a wrap never orphans a separator");
    }

    [Fact]
    public void TheNarrowRules_ShipInTheCommittedBundle()
    {
        var bundle = File.ReadAllText(Path.Combine(ThemeCss.RepositoryRoot().FullName,
            "src", "Tempo.Blazor", "wwwroot", "css", "tempo-blazor.bundled.css"));

        bundle.Should().Contain(".tm-otp-input{max-width:100%;contain:inline-size");
        bundle.Should().Contain(".tm-otp-input__row{display:flex");
        bundle.Should().Contain("flex-wrap:wrap");
        bundle.Should().Contain("min-inline-size:var(--tm-space-6");
    }
}
