using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.Components.Feedback;
using Tempo.Blazor.Components.Layout;
using Tempo.Blazor.Components.Navigation;
using Tempo.Blazor.Interfaces;
using Tempo.Blazor.Models;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Layout;

/// <summary>
/// The phone presentation of the application frame (plan 5d396592, phase 4). A phone container has no
/// room for the docked column or the tablet rail, so the navigation becomes a hamburger in the top bar
/// that opens the full navigation in a modal drawer (focus into the drawer, Escape / the close button /
/// choosing an entry close it, focus returns to the hamburger), or — with
/// <see cref="TmAppShell.MobileNav"/> set to <see cref="AppShellMobileNav.BottomNavigation"/> — a
/// bottom navigation bar with no drawer at all. The work area stops being a dragged column and becomes
/// a <c>TmSidePanel</c> bottom sheet, so a phone renders no resize separator.
/// Every test pins <see cref="TmAppShell.LayoutMode"/>, so no DOM measurement is involved.
/// </summary>
public class TmAppShellMobileTests : LocalizationTestBase
{
    /// <summary>The three destinations of the bottom bar: two links and one command entry.</summary>
    internal static IReadOnlyList<BottomNavItem> DefaultBottomItems =>
    [
        new() { Text = "Home", Icon = "home", Href = "/" },
        new() { Text = "Reports", Icon = "chart", Href = "/reports" },
        new() { Text = "Settings", Icon = "settings" },
    ];

    private IRenderedComponent<TmAppShell> RenderShell(
        TmLayoutMode mode = TmLayoutMode.Mobile,
        Action<ComponentParameterCollectionBuilder<TmAppShell>>? extra = null)
        => Render<TmAppShell>(p =>
        {
            p.Add(x => x.LayoutMode, mode);
            p.Add(x => x.Title, "Acme");
            p.Add(x => x.NavItems, TmAppShellTests.DefaultItems);
            extra?.Invoke(p);
        });

    /// <summary>
    /// The navigation disclosure button: <c>TmTopBar</c>'s own button with the built-in bar, the
    /// frame's own opener in front of a host <see cref="TmAppShell.TopBar"/> slot.
    /// </summary>
    private static AngleSharp.Dom.IElement MenuButton(IRenderedComponent<TmAppShell> cut)
        => cut.Find("[data-testid='topbar-menu-button'], [data-testid='app-shell-menu-button']");

    private static string[] NavLabels(IRenderedComponent<TmAppShell> cut, string scope)
        => cut.Find(scope).QuerySelectorAll(".tm-sidebar-nav-label").Select(a => a.TextContent.Trim()).ToArray();

    // ── No docked column, no rail ───────────────────────────────────────────

    [Fact]
    public void Mobile_RendersNeitherTheDockedColumnNorTheTabletRail()
    {
        var cut = RenderShell();

        cut.Find("[data-testid='app-shell']").GetAttribute("data-layout").Should().Be("mobile");
        cut.FindAll("[data-region='sidebar']").Should().BeEmpty("a phone has no room for a docked navigation column");
        cut.FindAll(".tm-sidebar").Should().BeEmpty("the icon rail is the tablet presentation; a phone opens the full navigation in a drawer");
        cut.FindAll("nav[aria-label]").Should().BeEmpty("the navigation exists only inside the drawer at load");
    }

    // ── The hamburger and the drawer ────────────────────────────────────────

    [Fact]
    public void Mobile_TopBar_RendersTheNavigationDisclosureButton()
    {
        var cut = RenderShell();

        var button = MenuButton(cut);
        button.GetAttribute("type").Should().Be("button");
        button.GetAttribute("aria-label").Should().Be("Navigation menu");
        button.GetAttribute("aria-expanded").Should().Be("false", "the drawer is closed at load");
        button.GetAttribute("aria-controls").Should().BeNull("a closed drawer is not in the DOM, so there is nothing to point at");
    }

    [Fact]
    public void Mobile_Hamburger_OpensTheFullNavigationInTheDrawer()
    {
        var cut = RenderShell();

        MenuButton(cut).Click();

        var overlay = cut.Find(".tm-app-shell__overlay");
        NavLabels(cut, ".tm-app-shell__overlay").Should().Contain(["Home", "Reports"]);
        overlay.QuerySelector("nav[aria-label]").Should().NotBeNull("the drawer carries the shell's one named navigation landmark");

        var opened = MenuButton(cut);
        opened.GetAttribute("aria-expanded").Should().Be("true");
        opened.GetAttribute("aria-controls").Should().Be(overlay.Id,
            "the state and the controlled surface must describe the same drawer");
    }

    [Fact]
    public void Mobile_Drawer_IsAModalTrappedSurface_ThatReturnsTheFocusToTheHamburger()
    {
        var cut = RenderShell();
        var hamburgerId = MenuButton(cut).Id;
        hamburgerId.Should().NotBeNullOrWhiteSpace("the frame owns the id, so the trap can hand the focus back to it");

        MenuButton(cut).Click();

        var scope = cut.FindComponent<TmFocusScope>();
        scope.Instance.Modal.Should().BeTrue("the page behind the drawer is inert");
        scope.Instance.CloseOnEscape.Should().BeTrue();
        scope.Instance.RestoreFocus.Should().BeTrue();

        var root = cut.Find("[data-testid='app-shell-nav-overlay']");
        root.GetAttribute("data-restore-target").Should().Be(hamburgerId,
            "Escape, the close button and an entry all hand the focus back to the hamburger that opened the drawer");
    }

    [Fact]
    public void Mobile_Drawer_IsNamedByTheNavigationLabel()
    {
        var cut = RenderShell();
        MenuButton(cut).Click();

        var overlay = cut.Find(".tm-app-shell__overlay");
        var labelledBy = overlay.GetAttribute("aria-labelledby");
        labelledBy.Should().NotBeNullOrWhiteSpace("a dialog needs an accessible name");
        cut.Find("#" + labelledBy).TextContent.Trim().Should().Be("Main navigation");
    }

    [Fact]
    public void Mobile_Drawer_CarriesTheAccountFooter()
    {
        var cut = RenderShell(extra: p => p.Add(x => x.SidebarFooter,
            b => b.AddMarkupContent(0, "<span id='account'>me@example.com</span>")));

        MenuButton(cut).Click();

        cut.Find(".tm-app-shell__overlay .tm-sidebar-footer #account").Should().NotBeNull(
            "the signed-in account slot has to stay reachable from the phone drawer");
    }

    [Fact]
    public void Mobile_Drawer_HasNoCollapseToggle()
    {
        var cut = RenderShell();
        MenuButton(cut).Click();

        cut.FindAll(".tm-app-shell__overlay .tm-sidebar-toggle").Should().BeEmpty(
            "a collapse toggle inside a drawer collapses nothing the phone shows — the drawer's own close button is the way out");
    }

    [Fact]
    public void Mobile_TabletDrawer_KeepsItsCollapseToggle()
    {
        var cut = RenderShell(TmLayoutMode.Tablet);
        cut.Find(".tm-sidebar-toggle").Click();

        cut.FindAll(".tm-app-shell__overlay .tm-sidebar-toggle").Should().HaveCount(1,
            "the tablet overlay keeps its phase-3 markup");
    }

    [Fact]
    public void Mobile_Drawer_ChoosingAnEntry_ClosesIt()
    {
        var cut = RenderShell();
        var hamburgerId = MenuButton(cut).Id;
        MenuButton(cut).Click();

        var overlay = cut.Find("[data-testid='app-shell-nav-overlay']");
        overlay.GetAttribute("data-restore-target").Should().Be(hamburgerId,
            "a drawer that closes on choosing an entry hands the focus back to the hamburger that opened it");

        cut.Find(".tm-app-shell__overlay .tm-sidebar-nav-link").Click();

        cut.FindAll(".tm-app-shell__overlay").Should().BeEmpty("choosing a destination closes the drawer it was chosen from");
        MenuButton(cut).GetAttribute("aria-expanded").Should().Be("false");
    }

    // ── Only a chosen destination closes the phone drawer ────────────────────

    /// <summary>
    /// A host-slot control with a REAL Blazor click handler. bUnit refuses to dispatch a click at an
    /// element that has no handler anywhere in its ancestry, and the point of these tests is a control
    /// that lives inside the drawer — the account trigger, a workspace switcher, a slot expander.
    /// </summary>
    /// <param name="id">The control's DOM id.</param>
    /// <param name="onClick">What the control does when it is activated.</param>
    private RenderFragment SlotControl(string id, Action onClick) => fragment =>
    {
        fragment.OpenElement(0, "button");
        fragment.AddAttribute(1, "type", "button");
        fragment.AddAttribute(2, "id", id);
        fragment.AddAttribute(3, "onclick", EventCallback.Factory.Create<Microsoft.AspNetCore.Components.Web.MouseEventArgs>(this, onClick));
        fragment.AddContent(4, id);
        fragment.CloseElement();
    };

    [Fact]
    public void Mobile_Drawer_FooterControls_DoNotCloseIt()
    {
        // The drawer body is not one big "close" target: the account area sits in SidebarFooter, so a tap
        // on its trigger must reach the menu, not unmount the surface the menu lives in.
        var clicks = 0;
        var cut = RenderShell(extra: p => p.Add(x => x.SidebarFooter, SlotControl("account-trigger", () => clicks++)));

        MenuButton(cut).Click();
        cut.Find(".tm-app-shell__overlay .tm-sidebar-footer #account-trigger").Click();

        clicks.Should().Be(1, "the click reached the slot control instead of being swallowed by the drawer");
        cut.FindAll(".tm-app-shell__overlay").Should().ContainSingle(
            "the account stays reachable on a phone: tapping it opens its menu instead of closing the drawer");
    }

    [Fact]
    public void Mobile_Drawer_HeaderControls_DoNotCloseIt()
    {
        var clicks = 0;
        var cut = RenderShell(extra: p => p.Add(x => x.SidebarHeader, SlotControl("workspace-switcher", () => clicks++)));

        MenuButton(cut).Click();
        cut.Find(".tm-app-shell__overlay .tm-sidebar-header #workspace-switcher").Click();

        clicks.Should().Be(1);
        cut.FindAll(".tm-app-shell__overlay").Should().ContainSingle(
            "a workspace switcher in the drawer header opens its own menu, it does not dismiss the drawer");
    }

    [Fact]
    public void Mobile_Drawer_SectionTitleAndPadding_AreNotClickTargets()
    {
        // A heading and the drawer's own padding are not destinations, so nothing in their ancestry
        // handles a click at all: bUnit reports exactly that, which is the guarantee the review asked for.
        var cut = RenderShell(extra: p => p.Add(x => x.NavSections, TmAppShellTests.DefaultSections));
        MenuButton(cut).Click();

        var title = cut.Find(".tm-app-shell__overlay .tm-sidebar-section-title");
        var body = cut.Find(".tm-app-shell__overlay .tm-app-shell__nav");

        ((Action)(() => title.Click())).Should().Throw<MissingEventHandlerException>(
            "a section heading is not a destination — dismissing on it would be a surprise");
        ((Action)(() => body.Click())).Should().Throw<MissingEventHandlerException>(
            "the drawer body carries no blanket click handler at all");
        cut.FindAll(".tm-app-shell__overlay").Should().ContainSingle();
    }

    [Fact]
    public void Mobile_Drawer_HostSidebarSlot_IsNotDismissedByAClick()
    {
        // A host navigation surface may carry expanders and other controls: the frame cannot guess what a
        // click meant, so it lets the slot decide and closes on NAVIGATION instead.
        var clicks = 0;
        var cut = RenderShell(extra: p => p.Add(x => x.Sidebar, SlotControl("group-expander", () => clicks++)));
        MenuButton(cut).Click();

        cut.Find(".tm-app-shell__overlay #group-expander").Click();

        clicks.Should().Be(1);
        cut.FindAll(".tm-app-shell__overlay").Should().ContainSingle();
    }

    [Fact]
    public void Mobile_Drawer_ClosesWhenTheLocationChanges()
    {
        // What a host `Sidebar` slot (or any link inside the drawer) really does: it navigates. The frame
        // listens to the router instead of sniffing clicks, so it closes whatever produced the navigation.
        var cut = RenderShell(extra: p => p.Add(x => x.Sidebar,
            b => b.AddMarkupContent(0, "<a id='host-link' href='/reports'>Reports</a>")));
        MenuButton(cut).Click();

        var navigation = Services.GetRequiredService<NavigationManager>();
        cut.InvokeAsync(() => navigation.NavigateTo("/reports"));

        cut.FindAll(".tm-app-shell__overlay").Should().BeEmpty(
            "a navigation is exactly the moment the drawer it was started from stops being useful");
        MenuButton(cut).GetAttribute("aria-expanded").Should().Be("false");
    }

    [Fact]
    public void Mobile_Drawer_StaysClosedWhenTheLocationChanges()
    {
        var cut = RenderShell();
        MenuButton(cut).Click();
        cut.Find(".tm-app-shell__overlay .tm-drawer__close").Click();

        var navigation = Services.GetRequiredService<NavigationManager>();
        cut.InvokeAsync(() => navigation.NavigateTo("/reports"));

        MenuButton(cut).GetAttribute("aria-expanded").Should().Be("false",
            "a navigation with the drawer already closed must not resurrect the surface");
        cut.FindAll(".tm-app-shell__overlay").Should().BeEmpty();
    }

    [Fact]
    public void Tablet_Drawer_StaysOpen_AfterNavigation()
    {
        // The close-on-navigate rule is the PHONE drawer's: the tablet overlay keeps surviving a route
        // change exactly as it did in phase 3.
        var cut = RenderShell(TmLayoutMode.Tablet);
        cut.Find(".tm-sidebar-toggle").Click();

        var navigation = Services.GetRequiredService<NavigationManager>();
        cut.InvokeAsync(() => navigation.NavigateTo("/reports"));

        cut.FindAll(".tm-app-shell__overlay").Should().ContainSingle();
    }

    [Fact]
    public void Tablet_Drawer_KeepsItsPhaseThreeBehaviour_AnEntryDoesNotCloseIt()
    {
        // The close-on-choose is the PHONE drawer's behaviour: the tablet overlay keeps the markup and
        // behaviour it shipped with, so an existing tablet host sees no change.
        var cut = RenderShell(TmLayoutMode.Tablet);
        cut.Find(".tm-sidebar-toggle").Click();

        cut.Find(".tm-app-shell__overlay .tm-sidebar-nav-link").Click();

        cut.FindAll(".tm-app-shell__overlay").Should().ContainSingle();
    }

    [Fact]
    public void Mobile_Drawer_EscapeClosesIt()
    {
        var cut = RenderShell();
        MenuButton(cut).Click();

        var scope = cut.FindComponent<TmFocusScope>();
        cut.InvokeAsync(() => scope.Instance.HandleFocusTrapEscapeAsync());

        cut.FindAll(".tm-app-shell__overlay").Should().BeEmpty();
        MenuButton(cut).GetAttribute("aria-expanded").Should().Be("false");
    }

    [Fact]
    public void Mobile_Drawer_CloseButtonClosesIt()
    {
        var cut = RenderShell();
        MenuButton(cut).Click();

        cut.Find(".tm-app-shell__overlay .tm-drawer__close").Click();

        cut.FindAll(".tm-app-shell__overlay").Should().BeEmpty();
    }

    [Fact]
    public void Mobile_Drawer_IsNotNestedInTheSizeContainer()
    {
        var cut = RenderShell();
        MenuButton(cut).Click();

        cut.Find(".tm-app-shell__frame").QuerySelector(".tm-app-shell__overlay").Should().BeNull(
            "an overlay inside the shell's CSS size container would be clipped and pinned by it");
        cut.Find("[data-testid='app-shell']").ClassList.Should().Contain("tm-layout--no-container");
    }

    [Fact]
    public void Mobile_Drawer_ClosesWhenTheLayoutLeavesThePhone()
    {
        var cut = RenderShell();
        MenuButton(cut).Click();

        cut.Render(p => p.Add(x => x.LayoutMode, TmLayoutMode.Desktop));

        cut.FindAll(".tm-app-shell__overlay").Should().BeEmpty();
    }

    [Fact]
    public void Mobile_HostTopBar_GetsTheFramesOwnOpener()
    {
        var cut = RenderShell(extra: p => p.Add(x => x.TopBar,
            b => b.AddMarkupContent(0, "<span id='custom-bar'>Custom</span>")));

        var opener = cut.Find("[data-testid='app-shell-menu-button']");
        opener.GetAttribute("aria-label").Should().Be("Navigation menu");
        cut.Find("header #custom-bar").Should().NotBeNull("the host bar still renders inside the frame's own header landmark");

        opener.Click();

        NavLabels(cut, ".tm-app-shell__overlay").Should().Contain(["Home", "Reports"]);
    }

    [Fact]
    public async Task Mobile_HostTopBar_CanOptOutOfTheFramesOpener_AndDriveTheDrawerItself()
    {
        var cut = RenderShell(extra: p =>
        {
            p.Add(x => x.TopBar, b => b.AddMarkupContent(0, "<button type='button' id='own-opener'>Menu</button>"));
            p.Add(x => x.ShowTopBarMenuButton, false);
        });

        cut.FindAll("[data-testid='app-shell-menu-button']").Should().BeEmpty("a bar that carries its own opener must not get a second one");
        cut.Find("header #own-opener").Should().NotBeNull();

        // The frame's drawer stays reachable from the host's own control, so opting out never leaves a
        // phone with no navigation at all.
        await cut.InvokeAsync(() => cut.Instance.OpenNavigationAsync());
        NavLabels(cut, ".tm-app-shell__overlay").Should().Contain(["Home", "Reports"]);

        await cut.InvokeAsync(() => cut.Instance.CloseNavigationAsync());
        cut.FindAll(".tm-app-shell__overlay").Should().BeEmpty();
    }

    [Fact]
    public async Task OpenNavigationAsync_IsANoOpOnDesktop_AndInBottomNavigationMode()
    {
        var desktop = RenderShell(TmLayoutMode.Desktop);
        await desktop.InvokeAsync(() => desktop.Instance.OpenNavigationAsync());
        desktop.FindAll(".tm-app-shell__overlay").Should().BeEmpty("a desktop column is always visible: there is nothing to open");

        var bottom = RenderShell(extra: p => p.Add(x => x.MobileNav, AppShellMobileNav.BottomNavigation));
        await bottom.InvokeAsync(() => bottom.Instance.OpenNavigationAsync());
        bottom.FindAll(".tm-app-shell__overlay").Should().BeEmpty("the bar is the navigation in this mode");
    }

    [Fact]
    public void Mobile_NavigationModelOnly_RendersABarWithJustTheOpener()
    {
        var cut = Render<TmAppShell>(p =>
        {
            p.Add(x => x.LayoutMode, TmLayoutMode.Mobile);
            p.Add(x => x.NavItems, TmAppShellTests.DefaultItems);
        });

        cut.FindAll("header").Should().HaveCount(1, "without a bar the hamburger would have nowhere to live, so a phone frame renders one");
        cut.Find("[data-testid='topbar-menu-button']").Should().NotBeNull();
    }

    [Fact]
    public void TabletAndDesktop_RenderNoHamburger()
    {
        RenderShell(TmLayoutMode.Tablet).FindAll("[data-testid='topbar-menu-button'], [data-testid='app-shell-menu-button']")
            .Should().BeEmpty("the tablet opener is the rail toggle, and a desktop column opens nothing");
        RenderShell(TmLayoutMode.Desktop).FindAll("[data-testid='topbar-menu-button'], [data-testid='app-shell-menu-button']")
            .Should().BeEmpty();
    }

    // ── Bottom navigation instead of the drawer ─────────────────────────────

    [Fact]
    public void Mobile_BottomNavigation_RendersTmBottomNavigation_InsteadOfADrawer()
    {
        var cut = RenderShell(extra: p =>
        {
            p.Add(x => x.MobileNav, AppShellMobileNav.BottomNavigation);
            p.Add(x => x.BottomNavItems, DefaultBottomItems);
        });

        cut.FindComponent<TmBottomNavigation>().Instance.Items.Should().HaveCount(3);
        cut.FindAll("[data-testid='topbar-menu-button']").Should().BeEmpty(
            "the bottom bar IS the navigation: there is no drawer to open, so a hamburger would lie");
        cut.FindAll(".tm-app-shell__overlay").Should().BeEmpty();
        cut.FindAll(".tm-sidebar").Should().BeEmpty();

        var bar = cut.Find("[data-region='bottom-nav']");
        bar.QuerySelectorAll(".tm-bottom-nav__link").Select(a => a.TextContent.Trim())
            .Should().Contain(["Home", "Reports"]);
        bar.QuerySelectorAll(".tm-bottom-nav__button").Select(a => a.TextContent.Trim())
            .Should().Contain("Settings", "an entry without an href is a command, and renders as a button");
        cut.FindAll("[data-region='bottom-nav'] nav[aria-label]").Should().HaveCount(1,
            "the bar is the shell's one navigation landmark on this layout");
    }

    [Fact]
    public void Mobile_BottomNavigation_NamesTheBarAfterTheShellsNavigationName()
    {
        // Phase-1 review carry-over (plan 92e96b25): the bar is the shell's navigation landmark, so it
        // carries the SAME accessible name the shell gives its navigation column / drawer (NavigationName,
        // i.e. SidebarNavLabel or the localized TmAppShell_NavLabel) — never a generic name that would
        // make two landmarks on the page indistinguishable.
        var cut = RenderShell(extra: p => p.Add(x => x.MobileNav, AppShellMobileNav.BottomNavigation));

        cut.FindComponent<TmBottomNavigation>().Instance.AriaLabel.Should().Be("Main navigation",
            "the default NavigationName is the localized TmAppShell_NavLabel");
        cut.Find("[data-region='bottom-nav'] nav").GetAttribute("aria-label").Should().Be("Main navigation");

        var named = RenderShell(extra: p =>
        {
            p.Add(x => x.MobileNav, AppShellMobileNav.BottomNavigation);
            p.Add(x => x.SidebarNavLabel, "Workspace");
        });
        named.Find("[data-region='bottom-nav'] nav").GetAttribute("aria-label").Should().Be("Workspace",
            "a host-set SidebarNavLabel renames the bar together with the navigation column");
    }

    [Fact]
    public void Mobile_BottomNavigation_DerivesItsEntriesFromTheNavigationModel()
    {
        var cut = RenderShell(extra: p => p.Add(x => x.MobileNav, AppShellMobileNav.BottomNavigation));

        cut.FindComponent<TmBottomNavigation>().Instance.Items.Select(i => i.Text).Should().Equal("Home", "Reports");
        cut.Find(".tm-bottom-nav__item--active").TextContent.Should().Contain("Home",
            "the model's current entry is the current destination");
    }

    [Fact]
    public void Mobile_BottomNavigation_DerivesSectionsInOrder_IgnoringSubEntries()
    {
        var sections = new List<SidebarNavSection>
        {
            new("Workspace",
            [
                new SidebarNavItem
                {
                    Id = "home", Label = "Home", Icon = "home", Href = "/", IsActive = true,
                    Children = [new SidebarNavItem { Id = "child", Label = "Child", Href = "/child" }],
                },
            ]),
            new("Admin", [new SidebarNavItem { Id = "users", Label = "Users", Icon = "users", Href = "/users" }]),
        };

        var cut = RenderShell(extra: p =>
        {
            p.Add(x => x.MobileNav, AppShellMobileNav.BottomNavigation);
            p.Add(x => x.NavSections, sections);
        });

        cut.FindComponent<TmBottomNavigation>().Instance.Items.Select(i => i.Text)
            .Should().Equal(new[] { "Home", "Users" },
                "a bar cannot show a tree: one entry per top-level item, sections in order");
    }

    [Fact]
    public void Mobile_BottomNavigation_RendersWithoutANavigationModel()
    {
        // BottomNavItems is a model of its own: a host may drive the phone bar without a sidebar model.
        var cut = Render<TmAppShell>(p =>
        {
            p.Add(x => x.LayoutMode, TmLayoutMode.Mobile);
            p.Add(x => x.MobileNav, AppShellMobileNav.BottomNavigation);
            p.Add(x => x.BottomNavItems, DefaultBottomItems);
        });

        cut.Find("[data-region='bottom-nav']").Should().NotBeNull();
        cut.FindAll("nav[aria-label]").Should().HaveCount(1);
        cut.FindAll("[data-region='sidebar']").Should().BeEmpty();
    }

    [Fact]
    public void Mobile_BottomNavigation_Tap_RaisesTheCallbackAndNavigates()
    {
        var clicked = new List<BottomNavItem>();
        var cut = RenderShell(extra: p =>
        {
            p.Add(x => x.MobileNav, AppShellMobileNav.BottomNavigation);
            p.Add(x => x.BottomNavItems, DefaultBottomItems);
            p.Add(x => x.BottomNavItemClicked, EventCallback.Factory.Create<BottomNavItem>(this, item => clicked.Add(item)));
        });

        cut.FindAll(".tm-bottom-nav__link")[1].Click();

        clicked.Should().HaveCount(1);
        clicked[0].Text.Should().Be("Reports");
        Services.GetRequiredService<NavigationManager>().Uri.Should().EndWith("/reports");
        cut.Find(".tm-bottom-nav__item--active").TextContent.Should().Contain("Reports",
            "the tapped entry is the current destination until the host says otherwise");
    }

    [Fact]
    public void Mobile_BottomNavigation_WithNoNavigationModel_RendersNothing()
    {
        var cut = Render<TmAppShell>(p =>
        {
            p.Add(x => x.LayoutMode, TmLayoutMode.Mobile);
            p.Add(x => x.MobileNav, AppShellMobileNav.BottomNavigation);
        });

        cut.FindAll("[data-region='bottom-nav']").Should().BeEmpty("an empty bar is noise");
    }

    [Fact]
    public void Desktop_AndTablet_NeverRenderTheBottomNavigation()
    {
        var extra = (Action<ComponentParameterCollectionBuilder<TmAppShell>>)(p =>
        {
            p.Add(x => x.MobileNav, AppShellMobileNav.BottomNavigation);
            p.Add(x => x.BottomNavItems, DefaultBottomItems);
        });

        RenderShell(TmLayoutMode.Desktop, extra).FindAll("[data-region='bottom-nav']")
            .Should().BeEmpty("the bottom bar is the phone presentation");
        RenderShell(TmLayoutMode.Tablet, extra).FindAll("[data-region='bottom-nav']").Should().BeEmpty();
    }

    // ── The work area as a sheet ────────────────────────────────────────────

    private Action<ComponentParameterCollectionBuilder<TmAppShell>> WorkArea(
        Action<ComponentParameterCollectionBuilder<TmAppShell>>? extra = null) => p =>
    {
        p.Add(x => x.WorkArea, b => b.AddContent(0, "Sources"));
        p.Add(x => x.WorkAreaOpen, true);
        extra?.Invoke(p);
    };

    [Fact]
    public void Mobile_WorkArea_PresentsAsABottomSheet()
    {
        var cut = RenderShell(extra: WorkArea());

        var panel = cut.FindComponent<TmSidePanel>();
        panel.Instance.Presentation.Should().Be(SidePanelPresentation.Sheet, "a phone has no room for a dragged column");
        panel.Instance.LayoutMode.Should().Be(TmLayoutMode.Mobile, "the sheet geometry follows the phone layout, not the viewport probe");
        panel.Instance.Title.Should().Be("Work area", "the region keeps its (localizable) name in the sheet");

        cut.FindAll("aside").Should().BeEmpty();
        cut.FindAll("[role='separator']").Should().BeEmpty("a phone has no resizable work area");
        cut.Find(".tm-drawer--bottom").TextContent.Should().Contain("Sources");
    }

    [Fact]
    public void Mobile_WorkAreaSheet_IsNotInTheDomWhileClosed()
    {
        var cut = RenderShell(extra: p =>
        {
            p.Add(x => x.WorkArea, b => b.AddContent(0, "Sources"));
            p.Add(x => x.WorkAreaOpen, false);
        });

        cut.FindComponent<TmSidePanel>().Instance.Open.Should().BeFalse();
        cut.FindAll(".tm-drawer--bottom").Should().BeEmpty();
    }

    [Fact]
    public void Mobile_WorkAreaSheet_CloseButtonRaisesTheTwoWayClose()
    {
        var raised = new List<bool>();
        var cut = RenderShell(extra: WorkArea(p => p.Add(x => x.WorkAreaOpenChanged,
            EventCallback.Factory.Create<bool>(this, v => raised.Add(v)))));

        cut.Find(".tm-drawer__close").Click();

        raised.Should().Equal(new[] { false });
        cut.FindAll(".tm-drawer--bottom").Should().BeEmpty();
    }

    [Fact]
    public void Mobile_WorkAreaSheet_ClosesWithoutAHostHandler()
    {
        // A modal sheet whose close is dropped by an unbound host callback would trap the user on the
        // phone: the frame closes it itself and the host's WorkAreaOpen stays its own decision.
        var cut = RenderShell(extra: WorkArea());

        cut.Find(".tm-drawer__close").Click();

        cut.FindAll(".tm-drawer--bottom").Should().BeEmpty();
        cut.Instance.WorkAreaOpen.Should().BeTrue("the parameter is the host's; the frame only stops showing the sheet");
    }

    [Fact]
    public void Desktop_WorkArea_StaysTheDockedColumn()
    {
        var cut = RenderShell(TmLayoutMode.Desktop, WorkArea());

        cut.FindAll(".tm-app-shell__work-area").Should().HaveCount(1);
        cut.FindAll("[role='separator']").Should().HaveCount(1);
        cut.FindAll("aside").Should().HaveCount(1);
    }
}
