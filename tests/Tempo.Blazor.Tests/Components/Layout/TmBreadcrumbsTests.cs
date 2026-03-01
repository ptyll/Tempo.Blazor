using Bunit;
using FluentAssertions;
using Tempo.Blazor.Components.Layout;
using Tempo.Blazor.Models;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.Layout;

/// <summary>TDD tests for TmBreadcrumbs.</summary>
public class TmBreadcrumbsTests : LocalizationTestBase
{
    private static List<BreadcrumbItem> TwoItems =>
    [
        new("Home", "/"),
        new("Settings", "/settings")
    ];

    private static List<BreadcrumbItem> ThreeItems =>
    [
        new("Home", "/"),
        new("Admin", "/admin"),
        new("Users")
    ];

    [Fact]
    public void TmBreadcrumbs_Renders_Nav_Element()
    {
        var cut = RenderComponent<TmBreadcrumbs>(p => p
            .Add(c => c.Items, TwoItems));

        cut.Find("nav").Should().NotBeNull();
    }

    [Fact]
    public void TmBreadcrumbs_Has_Base_CssClass()
    {
        var cut = RenderComponent<TmBreadcrumbs>(p => p
            .Add(c => c.Items, TwoItems));

        cut.Find("nav").ClassList.Should().Contain("tm-breadcrumbs");
    }

    [Fact]
    public void TmBreadcrumbs_Renders_All_Items()
    {
        var cut = RenderComponent<TmBreadcrumbs>(p => p
            .Add(c => c.Items, ThreeItems));

        cut.FindAll(".tm-breadcrumb-item").Count.Should().Be(3);
    }

    [Fact]
    public void TmBreadcrumbs_Last_Item_Has_Current_Class()
    {
        var cut = RenderComponent<TmBreadcrumbs>(p => p
            .Add(c => c.Items, ThreeItems));

        var items = cut.FindAll(".tm-breadcrumb-item");
        items.Last().ClassList.Should().Contain("tm-breadcrumb-current");
    }

    [Fact]
    public void TmBreadcrumbs_Last_Item_Has_No_Link()
    {
        var cut = RenderComponent<TmBreadcrumbs>(p => p
            .Add(c => c.Items, ThreeItems));

        // The last item (Users) has no href, so it should not render an <a>
        var lastItem = cut.FindAll(".tm-breadcrumb-item").Last();
        lastItem.QuerySelectorAll("a").Length.Should().Be(0);
    }

    [Fact]
    public void TmBreadcrumbs_Non_Last_Item_With_Href_Is_Link()
    {
        var cut = RenderComponent<TmBreadcrumbs>(p => p
            .Add(c => c.Items, ThreeItems));

        var firstItem = cut.FindAll(".tm-breadcrumb-item").First();
        firstItem.QuerySelectorAll("a").Length.Should().Be(1);
    }

    [Fact]
    public void TmBreadcrumbs_Separators_Between_Items()
    {
        var cut = RenderComponent<TmBreadcrumbs>(p => p
            .Add(c => c.Items, ThreeItems));

        // 3 items → 2 separators
        cut.FindAll(".tm-breadcrumb-separator").Count.Should().Be(2);
    }

    [Fact]
    public void TmBreadcrumbs_Empty_Items_Renders_Nothing()
    {
        var cut = RenderComponent<TmBreadcrumbs>(p => p
            .Add(c => c.Items, new List<BreadcrumbItem>()));

        cut.FindAll("nav").Should().BeEmpty();
    }
}
