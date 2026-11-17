using System.Text.RegularExpressions;
using FluentAssertions;

namespace Tempo.Blazor.Tests.Theme;

/// <summary>
/// Fáze 7 (layout): the drawer body padding must come from the single token
/// <c>--tm-drawer-body-padding</c> (with today's 1.25rem kept as the fallback) instead of a
/// hardcoded literal, so hosts can tighten the padding of docked/sheet drawers without
/// <c>::deep</c> overrides and negative-margin workarounds.
/// </summary>
public sealed class DrawerBodyPaddingTokenTests
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(5);

    private static string Css()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "TempoBlazor.slnx")))
            dir = dir.Parent;
        return File.ReadAllText(Path.Combine(dir!.FullName, "src", "Tempo.Blazor", "wwwroot", "css", "components", "_drawer.css"));
    }

    [Fact]
    public void DrawerBody_Padding_UsesTheBodyPaddingToken()
    {
        var css = Css();

        Regex.IsMatch(css, @"\.tm-drawer\s*\{[^}]*--tm-drawer-body-padding:\s*var\(--tm-space-5\)", RegexOptions.None, RegexTimeout)
            .Should().BeTrue("the token is declared on the drawer root (1.25rem = --tm-space-5) as a component-local host knob");
        Regex.IsMatch(css, @"\.tm-drawer__body\s*\{[^}]*padding:\s*var\(--tm-drawer-body-padding\)", RegexOptions.None, RegexTimeout)
            .Should().BeTrue("the body padding must be token-driven (--tm-drawer-body-padding) so hosts restyle drawers with one custom property");
    }

    [Fact]
    public void DrawerBody_DoesNotCarryAHardcodedPaddingLiteral()
    {
        var css = Css();

        Regex.IsMatch(css, @"\.tm-drawer__body\s*\{[^}]*padding:\s*1\.25rem\s*;", RegexOptions.None, RegexTimeout)
            .Should().BeFalse("the literal 1.25rem padding must be replaced by the token");
    }
}
