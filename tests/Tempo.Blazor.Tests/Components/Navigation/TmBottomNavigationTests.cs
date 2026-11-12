using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Tempo.Blazor.Components.Navigation;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.Navigation;

public class TmBottomNavigationTests : LocalizationTestBase
{
    [Fact]
    public void TmBottomNavigation_Renders_Items()
    {
        var cut = Render<TmBottomNavigation>(p => p
            .Add(x => x.Items, new[]
            {
                new BottomNavItem { Text = "Home", Icon = "home" },
                new BottomNavItem { Text = "Search", Icon = "search" },
                new BottomNavItem { Text = "Profile", Icon = "user" }
            }));

        var items = cut.FindAll(".tm-bottom-nav__item");
        items.Count.Should().Be(3);
    }

    [Fact]
    public void TmBottomNavigation_Click_Fires_OnItemClick()
    {
        BottomNavItem? clicked = null;
        var items = new[]
        {
            new BottomNavItem { Text = "Home", Icon = "home", Href = "/" },
            new BottomNavItem { Text = "Search", Icon = "search", Href = "/search" }
        };

        var cut = Render<TmBottomNavigation>(p => p
            .Add(x => x.Items, items)
            .Add(x => x.OnItemClick, EventCallback.Factory.Create<BottomNavItem>(this, i => clicked = i)));

        cut.FindAll(".tm-bottom-nav__item")[1].QuerySelector("button, a")?.Click();
        clicked?.Text.Should().Be("Search");
    }

    [Fact]
    public void TmBottomNavigation_SelectedItem_Has_Active_Class()
    {
        var items = new[]
        {
            new BottomNavItem { Text = "Home", Icon = "home" },
            new BottomNavItem { Text = "Search", Icon = "search" }
        };

        var cut = Render<TmBottomNavigation>(p => p
            .Add(x => x.Items, items)
            .Add(x => x.SelectedItem, items[1]));

        var navItems = cut.FindAll(".tm-bottom-nav__item");
        navItems[1].ClassList.Contains("tm-bottom-nav__item--active").Should().BeTrue();
    }

    [Fact]
    public void TmBottomNavigation_Item_With_Href_Renders_Anchor()
    {
        var items = new[]
        {
            new BottomNavItem { Text = "Home", Icon = "home", Href = "/home" }
        };

        var cut = Render<TmBottomNavigation>(p => p
            .Add(x => x.Items, items));

        cut.Find("a.tm-bottom-nav__link").Should().NotBeNull();
    }

    [Fact]
    public void TmBottomNavigation_AriaLabel_Defaults_To_Localized_Main_Navigation()
    {
        var cut = Render<TmBottomNavigation>(p => p
            .Add(x => x.Items, new[] { new BottomNavItem { Text = "Home", Icon = "home" } }));

        cut.Find("nav.tm-bottom-nav").GetAttribute("aria-label").Should().Be("Main navigation");
    }

    [Fact]
    public void TmBottomNavigation_Does_Not_Render_Raw_Resource_Key_In_AriaLabel_Or_Text()
    {
        var cut = Render<TmBottomNavigation>(p => p
            .Add(x => x.Items, new[] { new BottomNavItem { Text = "Home", Icon = "home" } }));

        var nav = cut.Find("nav.tm-bottom-nav");
        nav.GetAttribute("aria-label").Should().NotBe("TmBottomNavigation_AriaLabel");
        cut.Markup.Should().NotContain("TmBottomNavigation_AriaLabel");
    }

    [Fact]
    public void TmBottomNavigation_AriaLabel_Falls_Back_To_Localized_Value_When_Parameter_Null()
    {
        var cut = Render<TmBottomNavigation>(p => p
            .Add(x => x.Items, new[] { new BottomNavItem { Text = "Home", Icon = "home" } })
            .Add(x => x.AriaLabel, (string?)null));

        cut.Find("nav.tm-bottom-nav").GetAttribute("aria-label").Should().Be("Main navigation");
    }

    [Fact]
    public void TmBottomNavigation_AriaLabel_Uses_Czech_Localized_Value_In_Cs_Culture()
    {
        Services.AddSingleton<Tempo.Blazor.Localization.ITmLocalizer>(BuildCzechLocalizer());

        var cut = Render<TmBottomNavigation>(p => p
            .Add(x => x.Items, new[] { new BottomNavItem { Text = "Domů", Icon = "home" } }));

        cut.Find("nav.tm-bottom-nav").GetAttribute("aria-label").Should().Be("Hlavní navigace");
        cut.Markup.Should().NotContain("TmBottomNavigation_AriaLabel");
    }

    [Fact]
    public void TmBottomNavigation_AriaLabel_Whitespace_Falls_Back_To_Localized_Value()
    {
        var cut = Render<TmBottomNavigation>(p => p
            .Add(x => x.Items, new[] { new BottomNavItem { Text = "Home", Icon = "home" } })
            .Add(x => x.AriaLabel, "  "));

        cut.Find("nav.tm-bottom-nav").GetAttribute("aria-label").Should().Be("Main navigation");
    }

    [Fact]
    public void TmBottomNavigation_AriaLabel_Parameter_Overrides_Localized_Default()
    {
        var cut = Render<TmBottomNavigation>(p => p
            .Add(x => x.Items, new[] { new BottomNavItem { Text = "Home", Icon = "home" } })
            .Add(x => x.AriaLabel, "Section navigation"));

        cut.Find("nav.tm-bottom-nav").GetAttribute("aria-label").Should().Be("Section navigation");
    }
}
