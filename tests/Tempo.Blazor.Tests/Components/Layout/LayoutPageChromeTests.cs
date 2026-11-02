using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Tempo.Blazor.Demo.SharedUI.Pages;
using Tempo.Blazor.Services;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.Layout;

/// <summary>
/// Contract for the /layout demo page's new sidebar/top-bar surfaces (plan 5d396592 phase 1). The
/// component suites prove the primitives; this proves the page actually SHOWS them, so the E2E lane
/// (LayoutChromeE2ETests) has something real to click.
/// </summary>
public class LayoutPageChromeTests : LocalizationTestBase
{
    public LayoutPageChromeTests() => Services.AddSingleton<ToastService>();

    [Fact]
    public void Sectioned_Rail_Renders_Two_Named_Groups()
    {
        var cut = Render<LayoutPage>();
        var rail = cut.Find("[data-testid='sectioned-sidebar']");

        var headings = rail.QuerySelectorAll(".tm-sidebar-section-title");
        headings.Should().HaveCount(2);
        headings.Select(h => h.TextContent.Trim()).Should().Equal(new[] { "Workspace", "Administration" });

        foreach (var heading in headings)
        {
            var list = heading.ParentElement!.QuerySelector("ul.tm-sidebar-nav-list");
            list.Should().NotBeNull();
            list!.GetAttribute("aria-labelledby").Should().Be(heading.Id);
        }
    }

    [Fact]
    public void Sectioned_Rail_Marks_The_Current_Page()
    {
        // The demo page IS /layout, so the fake location has to say so before the rail resolves.
        Services.GetRequiredService<NavigationManager>().NavigateTo("/layout");

        var cut = Render<LayoutPage>();
        var rail = cut.Find("[data-testid='sectioned-sidebar']");

        var current = rail.QuerySelectorAll("[aria-current='page']");
        current.Should().HaveCount(1);
        current[0].GetAttribute("href").Should().Be("/layout");
    }

    [Fact]
    public void Basic_Rail_Keeps_The_Ungrouped_List()
    {
        var cut = Render<LayoutPage>();
        var rail = cut.Find("[data-testid='sidebar']");

        rail.QuerySelectorAll(".tm-sidebar-section-title").Should().BeEmpty();
        rail.QuerySelectorAll("a.tm-sidebar-nav-link").Should().NotBeEmpty();
    }

    [Fact]
    public void TopBar_Menu_Button_Toggles_The_Reported_State()
    {
        var cut = Render<LayoutPage>();
        var button = cut.Find("[data-testid='topbar-menu-button']");

        button.GetAttribute("aria-expanded").Should().Be("false");
        button.GetAttribute("aria-controls").Should().Be("layout-topbar-menu-target");

        button.Click();

        cut.Find("[data-testid='topbar-menu-button']").GetAttribute("aria-expanded").Should().Be("true");
        cut.Find("#layout-topbar-menu-target").Should().NotBeNull("aria-controls must point at a real element");
    }

    [Fact]
    public void TopBar_Account_Menu_Reports_A_Command_Back_To_The_Page()
    {
        var cut = Render<LayoutPage>();

        cut.Find("[data-testid='topbar-user-button']").Click();
        cut.Find("[data-testid='topbar-user-menu-item-delete']").Click();

        cut.FindAll("[data-testid='topbar-user-menu']").Should().BeEmpty();
        cut.Find("[data-testid='topbar-last-action']").TextContent.Should().Contain("Delete account",
            "the page re-renders after its own state changed in the menu callback");
    }

    [Fact]
    public void TopBar_Account_Menu_Separates_SignOut_From_The_Host_Entries()
    {
        var cut = Render<LayoutPage>();
        cut.Find("[data-testid='topbar-user-button']").Click();

        cut.FindAll(".tm-topbar-user-menu-separator").Should().HaveCount(2,
            "one separator closes the host group, one introduces the built-in sign out");
        cut.Find("[data-testid='topbar-sign-out']").TextContent.Trim().Should().Be("Sign out");
    }

    [Fact]
    public void TopBar_Prints_The_Command_Palette_Chord()
    {
        var cut = Render<LayoutPage>();

        cut.Find(".tm-topbar-search-hint").TextContent.Trim().Should().Be("Ctrl+K");
    }
}
