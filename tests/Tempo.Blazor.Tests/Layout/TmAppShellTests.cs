using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.Components.Feedback;
using Tempo.Blazor.Components.Layout;
using Tempo.Blazor.Interfaces;
using Tempo.Blazor.Models;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Layout;

/// <summary>
/// The application frame (plan 5d396592, phase 3 — desktop and tablet). The shell is a container-owned
/// composite over the existing primitives: it resolves its own layout (TmLayoutObserver), renders the
/// page's landmarks exactly once (header / nav / main / aside), owns the resizable work area, and
/// presents the navigation as a docked column on desktop and as a rail with an overlay drawer on tablet.
/// Every test pins <see cref="TmAppShell.LayoutMode"/>, so no DOM measurement is involved.
/// </summary>
public class TmAppShellTests : LocalizationTestBase
{
    internal const string ResizeModule = "./_content/Tempo.Blazor/js/tm-panel-resize.js";

    internal static IReadOnlyList<ISidebarNavItem> DefaultItems =>
    [
        new SidebarNavItem { Id = "home", Label = "Home", Href = "/", Icon = "home", IsActive = true },
        new SidebarNavItem { Id = "reports", Label = "Reports", Href = "/reports", Icon = "chart", BadgeCount = 3 },
    ];

    internal static IReadOnlyList<SidebarNavSection> DefaultSections =>
    [
        new("Workspace", DefaultItems),
        new("Administration", [new SidebarNavItem { Id = "users", Label = "Users", Href = "/users", Icon = "users" }]),
    ];

    /// <summary>Registers the shared resize module with the values a pinned-layout test needs.</summary>
    internal sealed class Mod
    {
        public required Func<IEnumerable<JSRuntimeInvocation>> Attach;
        public required Func<IEnumerable<JSRuntimeInvocation>> Detach;
        public required Func<IEnumerable<JSRuntimeInvocation>> Save;
        public required Func<IEnumerable<JSRuntimeInvocation>> Load;
    }

    internal Mod Module(double content = 1200, string? stored = null, Exception? loadError = null)
    {
        var module = JSInterop.SetupModule(ResizeModule);
        module.Setup<double>("measureRegion", _ => true).SetResult(content);
        module.Setup<double>("measureWidth", _ => true).SetResult(420);
        var attach = module.SetupVoid("attachResize", _ => true); attach.SetVoidResult();
        var detach = module.SetupVoid("detachResize", _ => true); detach.SetVoidResult();
        var save = module.SetupVoid("saveStored", _ => true); save.SetVoidResult();
        var load = module.Setup<string?>("loadStored", _ => true);
        if (loadError is not null) load.SetException(loadError); else load.SetResult(stored);

        return new Mod
        {
            Attach = () => attach.Invocations,
            Detach = () => detach.Invocations,
            Save = () => save.Invocations,
            Load = () => load.Invocations,
        };
    }

    protected IRenderedComponent<TmAppShell> RenderShell(
        TmLayoutMode mode = TmLayoutMode.Desktop,
        Action<ComponentParameterCollectionBuilder<TmAppShell>>? extra = null)
        => Render<TmAppShell>(p =>
        {
            p.Add(x => x.LayoutMode, mode);
            p.Add(x => x.Title, "Acme");
            p.Add(x => x.NavItems, DefaultItems);
            extra?.Invoke(p);
        });

    /// <summary>Renders the shell without the harness defaults (no title, no flat item list), for the tests that pin those parameters themselves.</summary>
    private IRenderedComponent<TmAppShell> RenderBare(
        TmLayoutMode mode = TmLayoutMode.Desktop,
        Action<ComponentParameterCollectionBuilder<TmAppShell>>? extra = null)
        => Render<TmAppShell>(p =>
        {
            p.Add(x => x.LayoutMode, mode);
            extra?.Invoke(p);
        });

    /// <summary>The elements a keyboard user reaches with Tab, in DOM order.</summary>
    internal static IReadOnlyList<AngleSharp.Dom.IElement> Focusables(IRenderedComponent<TmAppShell> cut)
        => cut.FindAll("a[href], button, input, select, textarea, [tabindex]")
            .Where(e => e.GetAttribute("tabindex") != "-1" && e.GetAttribute("disabled") is null)
            .ToList();

    // ── The frame and its landmarks ─────────────────────────────────────────

    [Fact]
    public void Desktop_RendersThePageLandmarksExactlyOnce()
    {
        Module();
        var cut = RenderShell(extra: p =>
        {
            p.Add(x => x.WorkArea, b => b.AddContent(0, "Sources"));
            p.Add(x => x.WorkAreaOpen, true);
        });

        cut.FindAll("header").Should().HaveCount(1, "the shell owns the one banner landmark");
        cut.FindAll("main").Should().HaveCount(1, "the shell owns the one main landmark");
        cut.FindAll("nav[aria-label]").Should().HaveCount(1, "the shell owns the one named navigation landmark");
        cut.FindAll("aside").Should().HaveCount(1, "the only complementary landmark is the work area");
        cut.Find("aside").ClassList.Should().Contain("tm-app-shell__work-area");
    }

    [Fact]
    public void RootCarriesTheShellClassAndTheResolvedLayout()
    {
        Module();
        var cut = RenderShell();

        var root = cut.Find("[data-testid='app-shell']");
        root.ClassList.Should().Contain("tm-app-shell");
        root.GetAttribute("data-layout").Should().Be("desktop");
        cut.Find(".tm-app-shell__frame").GetAttribute("data-variant").Should().Be("inset");
    }

    [Fact]
    public void SkipLink_IsTheFirstFocusable_AndTargetsAnExistingMainId()
    {
        Module();
        var cut = RenderShell();

        var first = Focusables(cut).First();
        first.ClassList.Should().Contain("tm-app-shell__skip-link", "a keyboard user must reach the skip link before the navigation");
        first.TextContent.Trim().Should().Be("Skip to content");
        var target = first.GetAttribute("href")!.TrimStart('#');

        var main = cut.Find("main");
        main.Id.Should().Be(target, "the skip link has to resolve to the shell's own main region");
        main.GetAttribute("tabindex").Should().Be("-1", "main is a focus target, not a tab stop");
        target.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void SkipLinkTarget_OverridesTheMainId()
    {
        Module();
        var cut = RenderShell(extra: p => p.Add(x => x.SkipLinkTarget, "content"));

        cut.Find("main").Id.Should().Be("content");
        cut.Find(".tm-app-shell__skip-link").GetAttribute("href").Should().Be("#content");
    }

    [Theory]
    [InlineData(AppShellVariant.Inset, "tm-app-shell--inset", "inset")]
    [InlineData(AppShellVariant.Flat, "tm-app-shell--flat", "flat")]
    [InlineData(AppShellVariant.Workspace, "tm-app-shell--workspace", "workspace")]
    public void Variant_AddsItsModifierClassAndDataAttribute(AppShellVariant variant, string css, string data)
    {
        Module();
        var cut = RenderShell(extra: p => p.Add(x => x.Variant, variant));

        var frame = cut.Find(".tm-app-shell__frame");
        frame.ClassList.Should().Contain(css);
        frame.GetAttribute("data-variant").Should().Be(data);
    }

    [Fact]
    public void Variant_DefaultsToInset_AndTheEnumValuesAreStable()
    {
        Module();
        ((int)AppShellVariant.Inset).Should().Be(0, "the default must stay the first enum value");
        ((int)AppShellVariant.Flat).Should().Be(1);
        ((int)AppShellVariant.Workspace).Should().Be(2, "the TG-008 variant is additive, appended after Flat");

        var cut = Render<TmAppShell>(p => p.Add(x => x.LayoutMode, TmLayoutMode.Desktop));
        cut.Instance.Variant.Should().Be(AppShellVariant.Inset);
    }

    [Fact]
    public void TestIdPrefix_NamespacesTheShellsOwnTestIds()
    {
        Module();
        var cut = RenderShell(extra: p => p.Add(x => x.TestIdPrefix, "shell1"));

        cut.Find(".tm-app-shell__frame");
        cut.Find("[data-testid='shell1-app-shell-main']").TagName.Should().Be("MAIN");
        cut.Find("[data-testid='shell1-app-shell-skip-link']").TagName.Should().Be("A");
    }

    [Fact]
    public void Class_AndAdditionalAttributes_LandOnTheRoot()
    {
        Module();
        var cut = RenderShell(extra: p =>
        {
            p.Add(x => x.Class, "my-frame");
            p.AddUnmatched("data-host", "42");
        });

        var root = cut.Find("[data-testid='app-shell']");
        root.ClassList.Should().Contain("my-frame");
        root.GetAttribute("data-host").Should().Be("42");
    }

    // ── Navigation ─────────────────────────────────────────────────────────

    [Fact]
    public void BuiltInSidebar_RendersNavItems_AsLinks()
    {
        Module();
        var cut = RenderShell();

        var nav = cut.Find("nav[aria-label]");
        nav.GetAttribute("aria-label").Should().Be("Main navigation");
        nav.QuerySelectorAll(".tm-sidebar-nav-label").Select(a => a.TextContent.Trim()).Should().Contain(["Home", "Reports"]);
        nav.QuerySelectorAll("a[aria-current='page']").Should().HaveCount(1, "the active entry is the current page");
    }

    [Fact]
    public void BuiltInSidebar_RendersNavSections_UnderTheirHeadings()
    {
        Module();
        var cut = RenderBare(extra: p => p.Add(x => x.NavSections, DefaultSections));

        cut.FindAll(".tm-sidebar-section").Should().HaveCount(2);
        var headings = cut.FindAll(".tm-sidebar-section-title").Select(h => h.TextContent.Trim()).ToList();
        headings.Should().Equal("Workspace", "Administration");
        var nav = cut.Find("nav[aria-label]");
        nav.QuerySelectorAll(".tm-sidebar-nav-label").Select(a => a.TextContent.Trim()).Should().Contain(["Home", "Reports", "Users"]);
    }

    [Fact]
    public void NavSections_WinOverNavItems()
    {
        Module();
        var cut = RenderBare(extra: p =>
        {
            p.Add(x => x.NavItems, DefaultItems);
            p.Add(x => x.NavSections, DefaultSections);
        });

        // The flat list is NOT rendered when sections are supplied: a shell shows one navigation model.
        cut.Find("nav[aria-label]").QuerySelectorAll(".tm-sidebar-nav-label").Select(a => a.TextContent.Trim())
            .Should().Equal("Home", "Reports", "Users");
        cut.FindAll(".tm-sidebar-section").Should().HaveCount(2);
    }

    [Fact]
    public void SidebarSlot_ReplacesTheBuiltInSidebar()
    {
        Module();
        var cut = RenderBare(extra: p => p.Add(x => x.Sidebar, b => b.AddMarkupContent(0, "<a href='/custom'>Custom nav</a>")));

        var nav = cut.Find("nav[aria-label]");
        nav.TextContent.Should().Contain("Custom nav");
        nav.QuerySelectorAll("a").Should().HaveCount(1);
    }

    [Fact]
    public void SidebarNavLabel_NamesTheNavigationLandmark()
    {
        Module();
        var cut = RenderShell(extra: p => p.Add(x => x.SidebarNavLabel, "Product"));

        cut.Find("nav[aria-label]").GetAttribute("aria-label").Should().Be("Product");
    }

    [Fact]
    public void SidebarHeaderAndFooter_RenderInsideTheNavigationColumn()
    {
        Module();
        var cut = RenderShell(extra: p =>
        {
            p.Add(x => x.SidebarHeader, b => b.AddMarkupContent(0, "<span id='brand'>Workspace</span>"));
            p.Add(x => x.SidebarFooter, b => b.AddMarkupContent(0, "<span id='account'>me@example.com</span>"));
        });

        var column = cut.Find("[data-region='sidebar']");
        column.QuerySelector("#brand").Should().NotBeNull();
        column.QuerySelector("#account").Should().NotBeNull();
    }

    [Fact]
    public void NoNavigation_WhenNeitherItemsNorSlotAreGiven()
    {
        Module();
        var cut = RenderBare();

        cut.FindAll("nav[aria-label]").Should().BeEmpty("a shell without a navigation model must not render an empty nav landmark");
        cut.FindAll("[data-region='sidebar']").Should().BeEmpty();
    }

    [Fact]
    public void SidebarToggle_RaisesSidebarCollapsedChanged_AndCollapsesTheDesktopRail()
    {
        Module();
        var raised = new List<bool>();
        var cut = RenderShell(extra: p => p.Add(x => x.SidebarCollapsedChanged,
            EventCallback.Factory.Create<bool>(this, v => raised.Add(v))));

        cut.Find(".tm-sidebar-toggle").Click();

        raised.Should().Equal(true);
        cut.Find(".tm-sidebar").ClassList.Should().Contain("tm-sidebar-collapsed");

        cut.Find(".tm-sidebar-toggle").Click();
        raised.Should().Equal(true, false);
        cut.Find(".tm-sidebar").ClassList.Should().Contain("tm-sidebar-expanded");
    }

    [Fact]
    public void BoundSidebarCollapsed_DrivesTheRenderedState()
    {
        Module();
        var collapsed = RenderShell(extra: p => p.Add(x => x.SidebarCollapsed, true));
        collapsed.Find(".tm-sidebar").ClassList.Should().Contain("tm-sidebar-collapsed");

        var expanded = RenderShell();
        expanded.Find(".tm-sidebar").ClassList.Should().Contain("tm-sidebar-expanded");
    }

    [Fact]
    public void AHostEchoOfTheUsersCollapse_IsNotReimposedOverTheUi()
    {
        Module();
        var collapsed = false;
        var cut = RenderShell(extra: p =>
        {
            p.Add(x => x.SidebarCollapsed, collapsed);
            p.Add(x => x.SidebarCollapsedChanged, EventCallback.Factory.Create<bool>(this, v => collapsed = v));
        });

        cut.Find(".tm-sidebar-toggle").Click();
        cut.Render(p => p.Add(x => x.SidebarCollapsed, collapsed));

        collapsed.Should().BeTrue();
        cut.Find(".tm-sidebar").ClassList.Should().Contain("tm-sidebar-collapsed");
    }

    // ── The top bar ────────────────────────────────────────────────────────

    [Fact]
    public void BuiltInTopBar_RendersTheTitleInsideTheShellsHeader_WithoutANestedHeader()
    {
        Module();
        var cut = RenderShell();

        var header = cut.Find("header");
        header.ClassList.Should().Contain("tm-app-shell__topbar");
        header.TextContent.Should().Contain("Acme");
        header.QuerySelectorAll("header").Should().BeEmpty("a nested banner inside the frame's header is not a landmark a user can navigate");
    }

    [Fact]
    public void TopBarSlot_ReplacesTheBuiltInBar_InsideTheSameHeader()
    {
        Module();
        var cut = RenderBare(extra: p => p.Add(x => x.TopBar, b => b.AddMarkupContent(0, "<span id='custom-bar'>Custom</span>")));

        var header = cut.Find("header");
        header.QuerySelector("#custom-bar").Should().NotBeNull();
        cut.FindAll("header").Should().HaveCount(1);
    }

    [Fact]
    public void NoTopBarContent_MeansNoBannerLandmark()
    {
        Module();
        var cut = RenderBare(extra: p => p.Add(x => x.NavItems, DefaultItems));

        cut.FindAll("header").Should().BeEmpty("an empty header landmark is noise; without bar content there is no banner");
    }

    // ── Tablet: rail + overlay ─────────────────────────────────────────────

    [Fact]
    public void Tablet_RendersTheSidebarAsARail_EvenWhenTheHostDidNotCollapseIt()
    {
        Module();
        var cut = RenderShell(TmLayoutMode.Tablet);

        cut.Find(".tm-sidebar").ClassList.Should().Contain("tm-sidebar-collapsed");
        cut.Find("[data-region='sidebar']").ClassList.Should().Contain("tm-app-shell__sidebar--rail");
        cut.FindAll(".tm-sidebar-nav-label").Should().BeEmpty("an icon-only rail does not render the labels");
    }

    [Fact]
    public void Tablet_NoOverlayAtLoad()
    {
        Module();
        var cut = RenderShell(TmLayoutMode.Tablet);

        cut.FindAll(".tm-app-shell__overlay").Should().BeEmpty("the overlay drawer opens on demand, never at load");
    }

    [Fact]
    public void Tablet_Toggle_OpensTheNavigationOverlay_AsAModalTrappedDrawer()
    {
        Module();
        var cut = RenderShell(TmLayoutMode.Tablet);

        cut.Find(".tm-sidebar-toggle").Click();

        var overlay = cut.Find(".tm-app-shell__overlay");
        overlay.QuerySelector("nav[aria-label]").Should().NotBeNull();
        overlay.QuerySelectorAll(".tm-sidebar-nav-label").Select(a => a.TextContent.Trim()).Should().Contain(["Home", "Reports"]);

        var scope = cut.FindComponent<TmFocusScope>();
        scope.Instance.Modal.Should().BeTrue("the overlay is a modal surface: the page behind it is inert");
        scope.Instance.CloseOnEscape.Should().BeTrue();
        scope.Instance.RestoreFocus.Should().BeTrue("Escape and the close button return the focus to the rail toggle");
        scope.Instance.RestoreFocusTarget.Should().BeNull("the opener is the restore target: the scope notes it when the drawer opens");
    }

    [Fact]
    public void Tablet_BuiltInRail_Toggle_Reports_TheOverlayItOpens()
    {
        Module();
        var cut = RenderShell(TmLayoutMode.Tablet);

        var toggle = cut.Find("[data-region='sidebar'] .tm-sidebar-toggle");
        toggle.GetAttribute("aria-expanded").Should().Be("false",
            "the rail toggle opens the navigation drawer, which is closed at load");
        toggle.GetAttribute("aria-controls").Should().BeNull("there is no drawer to point at until it opens");

        toggle.Click();

        var overlay = cut.Find(".tm-app-shell__overlay");
        var opened = cut.Find("[data-region='sidebar'] .tm-sidebar-toggle");
        opened.GetAttribute("aria-expanded").Should().Be("true");
        opened.GetAttribute("aria-controls").Should().Be(overlay.Id,
            "the state and the controlled surface must describe the same drawer");
        overlay.Id.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Tablet_Overlay_IsNamedByItsHeading()
    {
        Module();
        var cut = RenderShell(TmLayoutMode.Tablet);
        cut.Find(".tm-sidebar-toggle").Click();

        var overlay = cut.Find(".tm-app-shell__overlay");
        var labelledBy = overlay.GetAttribute("aria-labelledby");
        labelledBy.Should().NotBeNullOrWhiteSpace("a dialog needs an accessible name");
        cut.Find("#" + labelledBy).TextContent.Trim().Should().Be("Main navigation");
    }

    [Fact]
    public void Tablet_Overlay_IsNotNestedInTheSizeContainer()
    {
        Module();
        var cut = RenderShell(TmLayoutMode.Tablet);
        cut.Find(".tm-sidebar-toggle").Click();

        cut.Find(".tm-app-shell__frame").QuerySelector(".tm-app-shell__overlay").Should().BeNull(
            "an overlay inside the shell's CSS size container would be clipped and pinned by it: the "
            + "frame carries container-type, the drawer is rendered beside it");
        cut.Find("[data-testid='app-shell']").ClassList.Should().Contain("tm-layout--no-container",
            "the measured root must not establish containment of its own, or the drawer would be clipped by it too");
    }

    [Fact]
    public void Tablet_Overlay_Escape_ClosesIt()
    {
        Module();
        var cut = RenderShell(TmLayoutMode.Tablet);
        cut.Find(".tm-sidebar-toggle").Click();

        var scope = cut.FindComponent<TmFocusScope>();
        cut.InvokeAsync(() => scope.Instance.HandleFocusTrapEscapeAsync());

        cut.FindAll(".tm-app-shell__overlay").Should().BeEmpty();
    }

    [Fact]
    public void Tablet_Overlay_CloseButton_ClosesIt()
    {
        Module();
        var cut = RenderShell(TmLayoutMode.Tablet);
        cut.Find(".tm-sidebar-toggle").Click();

        cut.Find(".tm-app-shell__overlay .tm-drawer__close").Click();

        cut.FindAll(".tm-app-shell__overlay").Should().BeEmpty();
    }

    [Fact]
    public void Desktop_ToggleDoesNotOpenAnOverlay()
    {
        Module();
        var cut = RenderShell(TmLayoutMode.Desktop);

        cut.Find(".tm-sidebar-toggle").Click();

        cut.FindAll(".tm-app-shell__overlay").Should().BeEmpty("the overlay is a tablet presentation only");
        cut.Find(".tm-sidebar").ClassList.Should().Contain("tm-sidebar-collapsed");
    }

    [Fact]
    public void Mobile_HasNoResizeSeparator_AndNoDockedWorkArea()
    {
        Module();
        var cut = RenderShell(TmLayoutMode.Mobile, p =>
        {
            p.Add(x => x.WorkArea, b => b.AddContent(0, "Sources"));
            p.Add(x => x.WorkAreaOpen, true);
        });

        cut.FindAll("[role='separator']").Should().BeEmpty("a phone has no room for a dragged work area");
        cut.FindAll("aside").Should().BeEmpty();
    }

    // ── Focus, reduced motion and forced colors are CSS contracts ──────────

    [Fact]
    public void TheWorkAreaSeparator_IsAKeyboardFocusableSeparator()
    {
        Module();
        var cut = RenderShell(extra: p =>
        {
            p.Add(x => x.WorkArea, b => b.AddContent(0, "Sources"));
            p.Add(x => x.WorkAreaOpen, true);
        });

        var separator = cut.Find("[role='separator']");
        separator.GetAttribute("tabindex").Should().Be("0");
        separator.GetAttribute("aria-orientation").Should().Be("vertical");
    }
}
