using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Tempo.Blazor.Demo.SharedUI.Layout;

namespace Tempo.Blazor.Tests.Theme;

/// <summary>
/// Decision G4: indigo is opt-in, so the demo must expose a switch and the choice has to survive
/// a reload. The attribute is <c>data-tm-theme</c> — distinct from <c>data-theme</c>, which already
/// means light/dark and must not be overloaded. It belongs on <c>&lt;html&gt;</c>, never on the
/// switch itself: a portaled overlay renders outside the switch.
/// </summary>
public class ColorThemeSwitchTests : BunitContext
{
    public ColorThemeSwitchTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddScoped<ColorThemeState>();
    }

    [Fact]
    public void Switch_DefaultsToTheBlueTheme()
    {
        var cut = Render<ColorThemeSwitch>();

        cut.Find("button[aria-pressed]").TextContent.Should().Contain("Blue");
        cut.FindAll("button").Should().HaveCount(2);
    }

    [Fact]
    public void Switch_TogglesToIndigo_AndPersistsTheChoice()
    {
        var cut = Render<ColorThemeSwitch>();

        cut.FindAll("button")[1].Click();

        cut.Find("button[aria-pressed]").TextContent.Should().Contain("Indigo");
        JSInterop.VerifyInvoke("tmColorTheme.apply")
            .Arguments[0].Should().Be("indigo");
    }

    [Fact]
    public void Switch_DoesNotCarryTheThemeAttribute()
    {
        var cut = Render<ColorThemeSwitch>();

        cut.Find("[data-testid='color-theme-switch']").HasAttribute("data-tm-theme")
            .Should().BeFalse("the theme attribute belongs on <html>, not on the control");
    }

    [Fact]
    public void Switch_RestoresIndigo_FromStorage()
    {
        JSInterop.Setup<string?>("tmColorTheme.stored").SetResult("indigo");

        var cut = Render<ColorThemeSwitch>();

        cut.WaitForAssertion(() =>
            cut.Find("button[aria-pressed]").TextContent.Should().Contain("Indigo"));
    }

    [Fact]
    public void TwoSwitches_StayInSync()
    {
        var first = Render<ColorThemeSwitch>();
        var second = Render<ColorThemeSwitch>();

        first.FindAll("button")[1].Click();

        second.Find("button[aria-pressed]").TextContent.Should().Contain("Indigo",
            "both copies share one scoped state, so the sidebar and the header cannot disagree");
    }
}
