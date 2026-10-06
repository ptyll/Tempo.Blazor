using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Tempo.Blazor.Demo.SharedUI.Layout;

namespace Tempo.Blazor.Tests.Theme;

/// <summary>
/// Decision G4: indigo is opt-in, so the demo must expose a switch and the choice has to survive
/// a reload. The attribute is <c>data-tm-theme</c> — distinct from <c>data-theme</c>, which already
/// means light/dark and must not be overloaded.
/// </summary>
public class ColorThemeSwitchTests : BunitContext
{
    public ColorThemeSwitchTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void Switch_DefaultsToTheBlueTheme()
    {
        var cut = Render<ColorThemeSwitch>();

        cut.Find("[data-testid='color-theme-switch']").GetAttribute("data-tm-theme")
            .Should().Be("default");
    }

    [Fact]
    public void Switch_TogglesToIndigo_AndPersistsTheChoice()
    {
        var cut = Render<ColorThemeSwitch>();

        cut.Find("[data-testid='color-theme-switch']").Click();

        cut.Find("[data-testid='color-theme-switch']").GetAttribute("data-tm-theme")
            .Should().Be("indigo");
        JSInterop.VerifyInvoke("localStorage.setItem")
            .Arguments[0].Should().Be("tm-demo-color-theme");
        JSInterop.VerifyInvoke("localStorage.setItem")
            .Arguments[1].Should().Be("indigo");
    }

    [Fact]
    public void Switch_RestoresIndigo_FromStorage()
    {
        JSInterop.Setup<string?>("localStorage.getItem", "tm-demo-color-theme").SetResult("indigo");

        var cut = Render<ColorThemeSwitch>();

        cut.WaitForAssertion(() =>
            cut.Find("[data-testid='color-theme-switch']").GetAttribute("data-tm-theme")
                .Should().Be("indigo"));
    }
}
