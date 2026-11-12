using System.Text.RegularExpressions;
using FluentAssertions;

namespace Tempo.Blazor.Tests.Theme;

/// <summary>
/// TG-006: a MODAL drawer root is a top-layer popover pinned to <c>inset: 0</c>, so it stretches
/// across the whole viewport while the overlay stays <c>position: fixed</c> (out of flow). The panel
/// is then the ONLY flex item of <c>.tm-drawer</c>, and with the default <c>flex-start</c> alignment
/// it stood at the inline start — a <c>Position=Right</c> drawer rendered at the LEFT window edge
/// (LTR, ≥ 641 px). These tests pin the stylesheet contract that keeps a right drawer at the right
/// edge and a left drawer at the left edge in BOTH LTR and RTL.
///
/// Positions are PHYSICAL: the slide keyframes travel physical axes (<c>translateX(±100%)</c>), so
/// the panel must keep standing at the same window edge in RTL — the flex alignment flips back via
/// <c>[dir="rtl"]</c> rules instead of following the inline axis.
/// </summary>
public sealed class DrawerPositionAlignmentTests
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(5);

    private static string Css() =>
        File.ReadAllText(Path.Combine(FindCoreCss(), "components", "_drawer.css"));

    [Fact]
    public void RightDrawer_Panel_IsAlignedToTheEnd_OfTheStretchedPopoverRoot()
    {
        var css = Css();

        Regex.IsMatch(css, @"\.tm-drawer--right\s*\{[^}]*justify-content:\s*flex-end", RegexOptions.None, RegexTimeout)
            .Should().BeTrue(
                "the popover root spans the viewport (inset:0) and the panel is its only flex item — " +
                "without flex-end a right drawer stands at the inline start (left in LTR)");
    }

    [Fact]
    public void LeftDrawer_Panel_IsAlignedToTheStart_OfTheStretchedPopoverRoot()
    {
        var css = Css();

        Regex.IsMatch(css, @"\.tm-drawer--left\s*\{[^}]*justify-content:\s*flex-start", RegexOptions.None, RegexTimeout)
            .Should().BeTrue("a left drawer must keep standing at the left window edge");
    }

    [Fact]
    public void Rtl_FlipsTheAlignmentBack_BecausePositionsArePhysical()
    {
        var css = Css();

        Regex.IsMatch(css, @"\[dir=""rtl""\]\s+\.tm-drawer--right\s*\{[^}]*justify-content:\s*flex-start", RegexOptions.None, RegexTimeout)
            .Should().BeTrue("in RTL flex-end is the physical LEFT edge — a right drawer must stay at the RIGHT edge");
        Regex.IsMatch(css, @"\[dir=""rtl""\]\s+\.tm-drawer--left\s*\{[^}]*justify-content:\s*flex-end", RegexOptions.None, RegexTimeout)
            .Should().BeTrue("in RTL flex-start is the physical RIGHT edge — a left drawer must stay at the LEFT edge");
    }

    [Fact]
    public void SlideAnimations_TravelPhysicalAxes_MatchingThePanelEdge()
    {
        var css = Css();

        // The drawer's Position is physical (right:0 / left:0, translateX keyframes), so the slide
        // must enter from the same physical edge the panel stands at — in every direction mode.
        Regex.IsMatch(css, @"@keyframes\s+tm-drawer-slide-right\s*\{\s*from\s*\{\s*transform:\s*translateX\(100%\)", RegexOptions.None, RegexTimeout)
            .Should().BeTrue("the right drawer's panel enters from the physical right edge");
        Regex.IsMatch(css, @"@keyframes\s+tm-drawer-slide-left\s*\{\s*from\s*\{\s*transform:\s*translateX\(-100%\)", RegexOptions.None, RegexTimeout)
            .Should().BeTrue("the left drawer's panel enters from the physical left edge");
    }

    private static string FindCoreCss()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "TempoBlazor.slnx")))
            dir = dir.Parent;
        return Path.Combine(dir!.FullName, "src", "Tempo.Blazor", "wwwroot", "css");
    }
}
