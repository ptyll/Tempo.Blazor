using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Components.Layout;
using Tempo.Blazor.Models;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.Layout;

/// <summary>TDD tests for TmSidebar.</summary>
public class TmSidebarTests : LocalizationTestBase
{
    private static List<SidebarNavItem> SampleItems =>
    [
        new() { Id = "home",     Label = "Home",     Icon = "home",     Href = "/",         IsActive = true },
        new() { Id = "settings", Label = "Settings", Icon = "settings", Href = "/settings", BadgeCount = 3 }
    ];

    [Fact]
    public void TmSidebar_Has_Base_CssClass()
    {
        var cut = RenderComponent<TmSidebar>(p => p
            .Add(c => c.Items, SampleItems));

        cut.Find("aside").ClassList.Should().Contain("tm-sidebar");
    }

    [Fact]
    public void TmSidebar_Expanded_By_Default()
    {
        var cut = RenderComponent<TmSidebar>(p => p
            .Add(c => c.Items, SampleItems));

        cut.Find("aside").ClassList.Should().Contain("tm-sidebar-expanded");
    }

    [Fact]
    public void TmSidebar_Collapsed_Adds_Collapsed_CssClass()
    {
        var cut = RenderComponent<TmSidebar>(p => p
            .Add(c => c.Collapsed, true)
            .Add(c => c.Items, SampleItems));

        cut.Find("aside").ClassList.Should().Contain("tm-sidebar-collapsed");
    }

    [Fact]
    public void TmSidebar_Renders_Nav_Items()
    {
        var cut = RenderComponent<TmSidebar>(p => p
            .Add(c => c.Items, SampleItems));

        cut.FindAll(".tm-sidebar-nav-item").Count.Should().Be(2);
    }

    [Fact]
    public void TmSidebar_Active_Item_Has_Active_CssClass()
    {
        var cut = RenderComponent<TmSidebar>(p => p
            .Add(c => c.Items, SampleItems));

        var items = cut.FindAll(".tm-sidebar-nav-item");
        items.First().ClassList.Should().Contain("tm-sidebar-nav-item-active");
    }

    [Fact]
    public void TmSidebar_Badge_Renders_When_BadgeCount_Set()
    {
        var cut = RenderComponent<TmSidebar>(p => p
            .Add(c => c.Items, SampleItems));

        cut.FindAll(".tm-sidebar-badge").Should().NotBeEmpty();
    }

    [Fact]
    public void TmSidebar_Toggle_Button_Fires_OnToggle()
    {
        var toggled = false;
        var cut = RenderComponent<TmSidebar>(p => p
            .Add(c => c.Items, SampleItems)
            .Add(c => c.OnToggle, EventCallback.Factory.Create(this, () => toggled = true)));

        cut.Find(".tm-sidebar-toggle").Click();

        toggled.Should().BeTrue();
    }
}
