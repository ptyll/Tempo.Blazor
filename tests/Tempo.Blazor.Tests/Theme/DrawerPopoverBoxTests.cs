using FluentAssertions;

namespace Tempo.Blazor.Tests.Theme;

/// <summary>
/// A modal drawer root is a top-layer popover. The UA popover reset zeroes the root box
/// (<c>inset/width/height: auto</c>), so without an additive box the side sheet and the bottom
/// sheet collapse and the slide-in finishes off-screen. Hosts must not re-pin
/// <c>.tm-drawer__panel</c> position, animation or transform.
/// </summary>
public sealed class DrawerPopoverBoxTests
{
    [Fact]
    public void PopoverDrawer_KeepsAViewportBox_WithoutKillingTheSlide()
    {
        var css = File.ReadAllText(Path.Combine(FindCoreCss(), "components", "_drawer.css"));

        css.Should().Contain(".tm-drawer[popover].tm-drawer--right");
        css.Should().Contain(".tm-drawer[popover].tm-drawer--left");
        css.Should().Contain(".tm-drawer[popover].tm-drawer--bottom");
        css.Should().Contain("inset: 0");
        css.Should().Contain("animation: tm-drawer-slide-right");
        css.Should().Contain("animation: tm-drawer-slide-up");
    }

    [Fact]
    public void SheetPanel_FitsTheViewport_AndScrollsItsBody()
    {
        var css = File.ReadAllText(Path.Combine(FindCoreCss(), "components", "_drawer.css"));

        css.Should().Contain("max-block-size: 100dvh");
        css.Should().Contain("min-block-size: 0");
        css.Should().Contain(".tm-drawer__body");
        css.Should().MatchRegex(@"\.tm-drawer__body\s*\{[^}]*overflow:\s*auto");
    }

    private static string FindCoreCss()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "TempoBlazor.slnx")))
            dir = dir.Parent;
        return Path.Combine(dir!.FullName, "src", "Tempo.Blazor", "wwwroot", "css");
    }
}
