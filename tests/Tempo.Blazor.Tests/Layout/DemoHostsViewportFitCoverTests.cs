using System.Text.RegularExpressions;
using FluentAssertions;

namespace Tempo.Blazor.Tests.Layout;

/// <summary>
/// C12: the demo hosts opt into <c>viewport-fit=cover</c> so the library's safe-area helpers
/// (<c>env(safe-area-inset-*)</c>, the <c>.tm-safe-area-*</c> classes, the mobile action bar's
/// bottom inset) report real insets on notched devices instead of a constant zero. Chromium
/// cannot emulate a notch, so the contract is pinned on the sources — no runtime E2E.
/// </summary>
public class DemoHostsViewportFitCoverTests
{
    public static TheoryData<string> HostFiles => new()
    {
        Path.Combine("src", "Tempo.Blazor.Demo", "wwwroot", "index.html"),
        Path.Combine("src", "Tempo.Blazor.Demo.Server", "Pages", "_Host.cshtml"),
        Path.Combine("src", "Tempo.Blazor.Demo.InteractiveAuto", "Tempo.Blazor.Demo.InteractiveAuto", "Components", "App.razor"),
    };

    [Theory]
    [MemberData(nameof(HostFiles))]
    public void DemoHosts_OptIntoViewportFitCover(string relativePath)
    {
        var html = File.ReadAllText(Path.Combine(FindRepoRoot(), relativePath));

        var meta = Regex.Match(html, "<meta\\s+name=\"viewport\"[^>]*>", RegexOptions.IgnoreCase);
        meta.Success.Should().BeTrue($"{relativePath} must declare the viewport meta");
        meta.Value.Should().Contain("viewport-fit=cover",
            $"{relativePath} must opt into viewport-fit=cover so the safe-area insets are real");
    }

    [Fact]
    public void DemoMobileHeader_PadsTheTopSafeArea_SoTheCoverMetaDoesNotPushItUnderTheNotch()
    {
        // With viewport-fit=cover the page paints under the notch, and the demo's fixed mobile
        // header sits at the very top edge. The shared .tm-safe-area-top helper pads it by
        // env(safe-area-inset-top) on top of its own base (zero on every non-notched screen).
        var razor = File.ReadAllText(Path.Combine(
            FindRepoRoot(), "src", "Tempo.Blazor.Demo.SharedUI", "Layout", "MainLayout.razor"));
        razor.Should().Contain("tm-safe-area-top",
            "the fixed mobile header must pad the top safe-area inset once the demo opts into cover");
    }

    [Fact]
    public void DemoContentWrapper_OffsetsTheFixedHeader_ByFourRemPlusTheTopSafeArea()
    {
        // The header is 4rem tall; the content wrapper offset it with a plain pt-16, which under
        // viewport-fit=cover leaves the first content row the top inset short — it hides under
        // the notch exactly like the header itself did before .tm-safe-area-top.
        var razor = File.ReadAllText(Path.Combine(
            FindRepoRoot(), "src", "Tempo.Blazor.Demo.SharedUI", "Layout", "MainLayout.razor"));
        razor.Should().Contain("pt-[calc(4rem+env(safe-area-inset-top,0px))]",
            "the content wrapper must offset the fixed header by 4rem plus the real top safe-area inset");
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "TempoBlazor.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("repo root not found");
    }
}
