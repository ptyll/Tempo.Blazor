using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Tempo.Blazor.Components.Layout;
using Tempo.Blazor.Interfaces;
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

    private static List<SidebarNavItem> InactiveItems =>
    [
        new() { Id = "home",     Label = "Home",     Icon = "home",     Href = "/" },
        new() { Id = "settings", Label = "Settings", Icon = "settings", Href = "/settings" }
    ];

    private static List<SidebarNavSection> SampleSections =>
    [
        new("Main", [new SidebarNavItem { Id = "home", Label = "Home", Icon = "home", Href = "/", IsActive = true }]),
        new("Administration", [new SidebarNavItem { Id = "settings", Label = "Settings", Icon = "settings", Href = "/settings" }])
    ];

    // ── Base markup (regression) ────────────────────────────────────────────────

    [Fact]
    public void TmSidebar_Has_Base_CssClass()
    {
        var cut = Render<TmSidebar>(p => p
            .Add(c => c.Items, SampleItems));

        cut.Find("aside").ClassList.Should().Contain("tm-sidebar");
    }

    [Fact]
    public void TmSidebar_Expanded_By_Default()
    {
        var cut = Render<TmSidebar>(p => p
            .Add(c => c.Items, SampleItems));

        cut.Find("aside").ClassList.Should().Contain("tm-sidebar-expanded");
    }

    [Fact]
    public void TmSidebar_Collapsed_Adds_Collapsed_CssClass()
    {
        var cut = Render<TmSidebar>(p => p
            .Add(c => c.Collapsed, true)
            .Add(c => c.Items, SampleItems));

        cut.Find("aside").ClassList.Should().Contain("tm-sidebar-collapsed");
    }

    [Fact]
    public void TmSidebar_Renders_Nav_Items()
    {
        var cut = Render<TmSidebar>(p => p
            .Add(c => c.Items, SampleItems));

        cut.FindAll(".tm-sidebar-nav-item").Count.Should().Be(2);
    }

    [Fact]
    public void TmSidebar_Active_Item_Has_Active_CssClass()
    {
        var cut = Render<TmSidebar>(p => p
            .Add(c => c.Items, SampleItems));

        var items = cut.FindAll(".tm-sidebar-nav-item");
        items.First().ClassList.Should().Contain("tm-sidebar-nav-item-active");
    }

    [Fact]
    public void TmSidebar_Badge_Renders_When_BadgeCount_Set()
    {
        var cut = Render<TmSidebar>(p => p
            .Add(c => c.Items, SampleItems));

        cut.FindAll(".tm-sidebar-badge").Should().NotBeEmpty();
    }

    [Fact]
    public void TmSidebar_Toggle_Button_Fires_OnToggle()
    {
        var toggled = false;
        var cut = Render<TmSidebar>(p => p
            .Add(c => c.Items, SampleItems)
            .Add(c => c.OnToggle, EventCallback.Factory.Create(this, () => toggled = true)));

        cut.Find(".tm-sidebar-toggle").Click();

        toggled.Should().BeTrue();
    }

    // ── Sections (F1): headings that name their group ───────────────────────────

    [Fact]
    public void Sections_Render_A_Heading_Per_Group()
    {
        var cut = Render<TmSidebar>(p => p
            .Add(c => c.Sections, SampleSections));

        var headings = cut.FindAll(".tm-sidebar-section-title");
        headings.Should().HaveCount(2);
        headings[0].TagName.Should().Be("H2");
        headings[0].TextContent.Trim().Should().Be("Main");
        headings[1].TextContent.Trim().Should().Be("Administration");
    }

    [Fact]
    public void Sections_Name_Their_List_Through_AriaLabelledby()
    {
        var cut = Render<TmSidebar>(p => p
            .Add(c => c.Sections, SampleSections));

        var groups = cut.FindAll("ul.tm-sidebar-nav-list");
        groups.Should().HaveCount(2);

        foreach (var group in groups)
        {
            var labelledBy = group.GetAttribute("aria-labelledby");
            labelledBy.Should().NotBeNullOrEmpty("a section list must be named by its heading");
            var heading = cut.Find($"#{labelledBy}");
            heading.ClassList.Should().Contain("tm-sidebar-section-title");
        }

        groups.Select(g => g.GetAttribute("aria-labelledby")).Should().OnlyHaveUniqueItems(
            "two headings with one id would leave the second list named by the first heading");
    }

    [Fact]
    public void Sections_Render_Every_Item_Of_Every_Group()
    {
        var cut = Render<TmSidebar>(p => p
            .Add(c => c.Sections, SampleSections));

        cut.FindAll(".tm-sidebar-nav-item").Should().HaveCount(2);
        cut.FindAll(".tm-sidebar-nav-item-active").Should().HaveCount(1);
    }

    [Fact]
    public void Flat_Items_And_Sections_Coexist()
    {
        var cut = Render<TmSidebar>(p => p
            .Add(c => c.Items, InactiveItems)
            .Add(c => c.Sections, SampleSections));

        // The flat list keeps its own unnamed list; the two section lists follow it.
        cut.FindAll("ul.tm-sidebar-nav-list").Should().HaveCount(3);
        cut.FindAll(".tm-sidebar-nav-item").Should().HaveCount(4);
    }

    [Fact]
    public void Without_Sections_The_Nav_Holds_One_Plain_List()
    {
        var cut = Render<TmSidebar>(p => p
            .Add(c => c.Items, SampleItems));

        cut.FindAll(".tm-sidebar-section-title").Should().BeEmpty();
        cut.FindAll("ul.tm-sidebar-nav-list").Should().HaveCount(1);
        cut.Find("ul.tm-sidebar-nav-list").GetAttribute("aria-labelledby").Should().BeNull();
    }

    [Fact]
    public void An_Empty_Section_Renders_Its_Heading_And_An_Empty_List()
    {
        var cut = Render<TmSidebar>(p => p
            .Add(c => c.Sections, new List<SidebarNavSection> { new("Empty", null) }));

        cut.FindAll(".tm-sidebar-section-title").Should().HaveCount(1);
        cut.FindAll("ul.tm-sidebar-nav-list").Should().HaveCount(1);
        cut.FindAll(".tm-sidebar-nav-item").Should().BeEmpty();
    }

    // ── aria-current ────────────────────────────────────────────────────────────

    [Fact]
    public void Active_Item_Carries_AriaCurrent_Page()
    {
        var cut = Render<TmSidebar>(p => p
            .Add(c => c.Items, SampleItems));

        var links = cut.FindAll("a.tm-sidebar-nav-link");
        links[0].GetAttribute("aria-current").Should().Be("page");
        links[1].GetAttribute("aria-current").Should().BeNull("only the active entry is the current page");
    }

    [Fact]
    public void Active_Child_Carries_AriaCurrent_Page()
    {
        var items = new List<SidebarNavItem>
        {
            new()
            {
                Id = "settings", Label = "Settings", Icon = "settings", Href = "/settings",
                Children = [new SidebarNavItem { Id = "general", Label = "General", Href = "/settings", IsActive = true }]
            }
        };

        var cut = Render<TmSidebar>(p => p.Add(c => c.Items, items));

        cut.Find("a.tm-sidebar-nav-link-child").GetAttribute("aria-current").Should().Be("page");
        cut.Find(".tm-sidebar-nav-children .tm-sidebar-nav-item").ClassList
            .Should().Contain("tm-sidebar-nav-item-active");
    }

    [Fact]
    public void Active_Item_Exposes_AriaCurrent_Even_When_Collapsed()
    {
        var cut = Render<TmSidebar>(p => p
            .Add(c => c.Collapsed, true)
            .Add(c => c.Items, SampleItems));

        cut.FindAll("a.tm-sidebar-nav-link")[0].GetAttribute("aria-current").Should().Be("page");
    }

    // ── AutoActive (NavigationManager) ──────────────────────────────────────────

    [Fact]
    public void AutoActive_Is_Off_By_Default()
    {
        Services.GetRequiredService<NavigationManager>().NavigateTo("/settings");

        var cut = Render<TmSidebar>(p => p
            .Add(c => c.Items, InactiveItems));

        cut.FindAll("[aria-current='page']").Should().BeEmpty("nothing asked the sidebar to follow the location");
    }

    [Fact]
    public void AutoActive_Marks_The_Current_Path()
    {
        Services.GetRequiredService<NavigationManager>().NavigateTo("/settings");

        var cut = Render<TmSidebar>(p => p
            .Add(c => c.AutoActive, true)
            .Add(c => c.Items, InactiveItems));

        var links = cut.FindAll("a.tm-sidebar-nav-link");
        links[0].GetAttribute("aria-current").Should().BeNull();
        links[1].GetAttribute("aria-current").Should().Be("page");
    }

    [Fact]
    public void AutoActive_Exact_Mode_Ignores_A_Deeper_Path()
    {
        Services.GetRequiredService<NavigationManager>().NavigateTo("/settings/profile");

        var cut = Render<TmSidebar>(p => p
            .Add(c => c.AutoActive, true)
            .Add(c => c.Items, InactiveItems));

        cut.FindAll("[aria-current='page']").Should().BeEmpty(
            "exact matching must not light up a parent entry for a page it does not own");
    }

    [Fact]
    public void AutoActive_Prefix_Mode_Matches_A_Deeper_Path_On_A_Segment_Boundary()
    {
        Services.GetRequiredService<NavigationManager>().NavigateTo("/settings/profile");

        var cut = Render<TmSidebar>(p => p
            .Add(c => c.AutoActive, true)
            .Add(c => c.AutoActiveMatchMode, SidebarNavMatchMode.Prefix)
            .Add(c => c.Items, InactiveItems));

        cut.FindAll("[aria-current='page']").Should().HaveCount(1);
        cut.FindAll("a.tm-sidebar-nav-link")[1].GetAttribute("aria-current").Should().Be("page");
    }

    [Fact]
    public void AutoActive_Prefix_Mode_Respects_Segments_And_Not_String_Prefixes()
    {
        Services.GetRequiredService<NavigationManager>().NavigateTo("/settings-old");

        var cut = Render<TmSidebar>(p => p
            .Add(c => c.AutoActive, true)
            .Add(c => c.AutoActiveMatchMode, SidebarNavMatchMode.Prefix)
            .Add(c => c.Items, InactiveItems));

        cut.FindAll("[aria-current='page']").Should().BeEmpty("'/settings' is not a prefix of '/settings-old'");
    }

    [Fact]
    public void AutoActive_Marks_The_Root_Entry_On_The_Root_Path()
    {
        var items = new List<SidebarNavItem>
        {
            new() { Id = "home", Label = "Home", Icon = "home", Href = "/" },
            new() { Id = "settings", Label = "Settings", Icon = "settings", Href = "/settings" }
        };

        // The bUnit navigation manager starts at the root, so no navigation is needed.
        var cut = Render<TmSidebar>(p => p
            .Add(c => c.AutoActive, true)
            .Add(c => c.Items, items));

        cut.FindAll("a.tm-sidebar-nav-link")[0].GetAttribute("aria-current").Should().Be("page");
        cut.FindAll("[aria-current='page']").Should().HaveCount(1);
    }

    [Fact]
    public void AutoActive_Prefix_Mode_Still_Marks_The_Root_Entry_On_The_Root_Path()
    {
        var items = new List<SidebarNavItem>
        {
            new() { Id = "home", Label = "Home", Icon = "home", Href = "/" }
        };

        var cut = Render<TmSidebar>(p => p
            .Add(c => c.AutoActive, true)
            .Add(c => c.AutoActiveMatchMode, SidebarNavMatchMode.Prefix)
            .Add(c => c.Items, items));

        cut.FindAll("[aria-current='page']").Should().HaveCount(1);
    }

    [Fact]
    public void AutoActive_Prefix_Mode_Keeps_The_Root_Link_Exact()
    {
        Services.GetRequiredService<NavigationManager>().NavigateTo("/datatable");

        var cut = Render<TmSidebar>(p => p
            .Add(c => c.AutoActive, true)
            .Add(c => c.AutoActiveMatchMode, SidebarNavMatchMode.Prefix)
            .Add(c => c.Items, InactiveItems));

        cut.FindAll("[aria-current='page']").Should().BeEmpty(
            "a '/' entry in prefix mode would otherwise claim every page");
    }

    [Fact]
    public void AutoActive_Keeps_An_Explicit_IsActive()
    {
        Services.GetRequiredService<NavigationManager>().NavigateTo("/datatable");

        var cut = Render<TmSidebar>(p => p
            .Add(c => c.AutoActive, true)
            .Add(c => c.Items, SampleItems)); // home is explicitly active

        cut.FindAll("a.tm-sidebar-nav-link")[0].GetAttribute("aria-current").Should().Be("page");
    }

    [Fact]
    public void AutoActive_Follows_Later_Navigation()
    {
        // Start somewhere no entry owns, so the marker has to appear on the later navigation.
        Services.GetRequiredService<NavigationManager>().NavigateTo("/datatable");

        var cut = Render<TmSidebar>(p => p
            .Add(c => c.AutoActive, true)
            .Add(c => c.Items, InactiveItems));

        cut.FindAll("[aria-current='page']").Should().BeEmpty();

        Services.GetRequiredService<NavigationManager>().NavigateTo("/settings");
        cut.WaitForAssertion(() => cut.FindAll("a.tm-sidebar-nav-link")[1]
            .GetAttribute("aria-current").Should().Be("page"));
    }

    // ── Localized labels ────────────────────────────────────────────────────────

    [Fact]
    public void Toggle_AriaLabel_Comes_From_Localization()
    {
        var cut = Render<TmSidebar>(p => p
            .Add(c => c.Items, SampleItems));

        cut.Find(".tm-sidebar-toggle").GetAttribute("aria-label").Should().Be("Collapse menu");
    }

    [Fact]
    public void Toggle_AriaLabel_Follows_The_Collapsed_State()
    {
        var cut = Render<TmSidebar>(p => p
            .Add(c => c.Collapsed, true)
            .Add(c => c.Items, SampleItems));

        cut.Find(".tm-sidebar-toggle").GetAttribute("aria-label").Should().Be("Expand menu");
    }

    [Fact]
    public void Toggle_AriaLabel_Is_Translated()
    {
        UseCzechLocalization();

        var cut = Render<TmSidebar>(p => p
            .Add(c => c.Items, SampleItems));

        cut.Find(".tm-sidebar-toggle").GetAttribute("aria-label").Should().Be("Sbalit menu");
    }

    [Fact]
    public void Toggle_Reports_The_Surface_It_Opens_Only_When_A_Host_Says_So()
    {
        var plain = Render<TmSidebar>(p => p
            .Add(c => c.Items, SampleItems));

        plain.Find(".tm-sidebar-toggle").GetAttribute("aria-expanded").Should().BeNull(
            "a plain collapse toggle is not a disclosure: it opens no surface of its own");
        plain.Find(".tm-sidebar-toggle").GetAttribute("aria-controls").Should().BeNull();

        var opener = Render<TmSidebar>(p => p
            .Add(c => c.Collapsed, true)
            .Add(c => c.Items, SampleItems)
            .Add(c => c.ToggleAriaExpanded, true)
            .Add(c => c.ToggleAriaControls, "nav-overlay"));

        var toggle = opener.Find(".tm-sidebar-toggle");
        toggle.GetAttribute("aria-expanded").Should().Be("true",
            "a rail toggle that opens an overlay drawer must say whether that drawer is open");
        toggle.GetAttribute("aria-controls").Should().Be("nav-overlay",
            "the expanded state is only meaningful next to the surface it describes");

        var closed = Render<TmSidebar>(p => p
            .Add(c => c.Items, SampleItems)
            .Add(c => c.ToggleAriaExpanded, false));

        closed.Find(".tm-sidebar-toggle").GetAttribute("aria-expanded").Should().Be("false");
    }

    [Fact]
    public void Nav_Has_A_Localized_Accessible_Name()
    {
        var cut = Render<TmSidebar>(p => p
            .Add(c => c.Items, SampleItems));

        cut.Find("nav").GetAttribute("aria-label").Should().Be("Sidebar navigation",
            "a bare nav landmark gives a screen-reader user no way to tell it from any other navigation");
    }

    [Fact]
    public void NavLabel_Parameter_Names_The_Landmark()
    {
        var cut = Render<TmSidebar>(p => p
            .Add(c => c.NavLabel, "Product navigation")
            .Add(c => c.Items, SampleItems));

        cut.Find("nav").GetAttribute("aria-label").Should().Be("Product navigation");
    }

    // ── Collapsed state: aria-label + tooltip ───────────────────────────────────

    [Fact]
    public void Collapsed_Link_With_No_Label_Does_Not_Get_An_Empty_AriaLabel()
    {
        var items = new List<SidebarNavItem>
        {
            new() { Id = "blank", Label = string.Empty, Icon = "home", Href = "/" }
        };

        var cut = Render<TmSidebar>(p => p
            .Add(c => c.Collapsed, true)
            .Add(c => c.Items, items));

        cut.Find("a.tm-sidebar-nav-link").GetAttribute("aria-label").Should().BeNull(
            "an empty aria-label would erase the name instead of leaving it to the content");
    }

    [Fact]
    public void Collapsed_Link_Carries_Its_Label_As_AriaLabel()
    {
        var cut = Render<TmSidebar>(p => p
            .Add(c => c.Collapsed, true)
            .Add(c => c.Items, InactiveItems));

        var links = cut.FindAll("a.tm-sidebar-nav-link");
        links[0].GetAttribute("aria-label").Should().Be("Home");
        links[1].GetAttribute("aria-label").Should().Be("Settings");
    }

    [Fact]
    public void Collapsed_Link_Announces_Its_Badge_Count()
    {
        var cut = Render<TmSidebar>(p => p
            .Add(c => c.Collapsed, true)
            .Add(c => c.Items, SampleItems));

        cut.FindAll("a.tm-sidebar-nav-link")[1].GetAttribute("aria-label").Should().Be("Settings (3)",
            "the badge is invisible in the icon-only state, so the name has to carry it");
    }

    [Fact]
    public void Collapsed_Link_Is_Wrapped_In_A_Tooltip_Carrying_The_Label()
    {
        var cut = Render<TmSidebar>(p => p
            .Add(c => c.Collapsed, true)
            .Add(c => c.Items, InactiveItems));

        var tooltips = cut.FindAll(".tm-tooltip");
        tooltips.Should().HaveCount(2);
        cut.FindAll(".tm-tooltip__content").Select(t => t.TextContent.Trim())
            .Should().Equal(new[] { "Home", "Settings" });
    }

    [Fact]
    public void Expanded_Link_Does_Not_Repeat_Its_Label_As_AriaLabel()
    {
        var cut = Render<TmSidebar>(p => p
            .Add(c => c.Items, InactiveItems));

        cut.FindAll(".tm-tooltip").Should().BeEmpty("the visible label is already the accessible name");
        cut.FindAll("a.tm-sidebar-nav-link").Select(l => l.GetAttribute("aria-label"))
            .Should().OnlyContain(value => value == null);
    }

    [Fact]
    public void CollapseTooltips_Can_Be_Turned_Off_Without_Losing_The_Name()
    {
        var cut = Render<TmSidebar>(p => p
            .Add(c => c.Collapsed, true)
            .Add(c => c.CollapseTooltips, false)
            .Add(c => c.Items, InactiveItems));

        cut.FindAll(".tm-tooltip").Should().BeEmpty();
        cut.FindAll("a.tm-sidebar-nav-link")[0].GetAttribute("aria-label").Should().Be("Home",
            "the accessible name is not an optional affordance");
        cut.Find("aside").ClassList.Should().NotContain("tm-sidebar-tooltips");
    }

    [Fact]
    public void Collapsed_Sidebar_With_Tooltips_Opts_Out_Of_The_Clip()
    {
        var cut = Render<TmSidebar>(p => p
            .Add(c => c.Collapsed, true)
            .Add(c => c.Items, InactiveItems));

        cut.Find("aside").ClassList.Should().Contain("tm-sidebar-tooltips",
            "an absolutely positioned tooltip cannot escape an overflow-clipped sidebar");
    }

    // ── Toggle chrome (additive) ────────────────────────────────────────────────

    [Fact]
    public void ShowToggle_Defaults_To_True_And_Renders_The_Toggle()
    {
        var cut = Render<TmSidebar>(p => p.Add(c => c.Items, SampleItems));

        cut.Instance.ShowToggle.Should().BeTrue("the default keeps today's markup");
        cut.FindAll(".tm-sidebar-toggle").Should().HaveCount(1);
    }

    [Fact]
    public void ShowToggle_False_Removes_The_Collapse_Control()
    {
        // A drawer that is already open has no column to collapse: its own close button is the way out,
        // and a chevron labelled "Collapse menu" that closes a drawer is a mislabelled control.
        var cut = Render<TmSidebar>(p => p
            .Add(c => c.Collapsed, false)
            .Add(c => c.ShowToggle, false)
            .Add(c => c.Items, SampleItems));

        cut.FindAll(".tm-sidebar-toggle").Should().BeEmpty();
        cut.Find("nav").Should().NotBeNull("the navigation itself stays — this is chrome, not the menu");
    }

    // ── OnItemSelected (additive, phase 4 review finding 1) ─────────────────────

    [Fact]
    public void OnItemSelected_Defaults_To_Unbound_So_Existing_Markup_Is_Unchanged()
    {
        var cut = Render<TmSidebar>(p => p.Add(c => c.Items, SampleItems));

        cut.Instance.OnItemSelected.HasDelegate.Should().BeFalse(
            "an unbound callback keeps today's behaviour for every existing caller");

        // Nothing is raised and nothing throws: choosing an entry is still plain navigation.
        cut.FindAll("a.tm-sidebar-nav-link")[0].Click();
        cut.FindAll("nav").Should().HaveCount(1);
    }

    [Fact]
    public void OnItemSelected_Raises_The_Chosen_Entry()
    {
        ISidebarNavItem? chosen = null;
        var cut = Render<TmSidebar>(p => p
            .Add(c => c.Items, SampleItems)
            .Add(c => c.OnItemSelected,
                EventCallback.Factory.Create<ISidebarNavItem>(this, item => chosen = item)));

        cut.FindAll("a.tm-sidebar-nav-link")[1].Click();

        chosen.Should().NotBeNull("the sidebar reports WHICH entry was chosen, so a host can react without guessing");
        chosen!.Id.Should().Be("settings");
    }

    [Fact]
    public void OnItemSelected_Is_Not_Raised_For_An_Entry_Without_A_Destination()
    {
        var raised = new List<ISidebarNavItem>();
        var cut = Render<TmSidebar>(p => p
            .Add(c => c.Items, new List<SidebarNavItem> { new() { Id = "command", Label = "Command" } })
            .Add(c => c.OnItemSelected,
                EventCallback.Factory.Create<ISidebarNavItem>(this, item => raised.Add(item))));

        cut.Find("a.tm-sidebar-nav-link").Click();

        raised.Should().BeEmpty("an entry with no Href navigates nowhere, so it is not a chosen destination");
    }

    [Fact]
    public void OnItemSelected_Never_Prevents_The_Native_Navigation()
    {
        // The callback hangs off the anchor's own click (Enter on a focused link fires that click too), so
        // the component adds no key handler of its own and the browser still follows the href.
        var cut = Render<TmSidebar>(p => p
            .Add(c => c.Items, SampleItems)
            .Add(c => c.OnItemSelected,
                EventCallback.Factory.Create<ISidebarNavItem>(this, _ => { })));

        var link = cut.FindAll("a.tm-sidebar-nav-link")[0];
        link.GetAttribute("href").Should().Be("/", "navigation still happens: the callback never prevents the default");
        link.GetAttribute("onkeydown").Should().BeNull("one activation per key press — the link is already native");
    }
}
