using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using System.Text.RegularExpressions;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.Components.Dashboard;
using Tempo.Blazor.Components.Layout;
using Tempo.Blazor.Interfaces;
using Tempo.Blazor.Models;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Dashboard;

/// <summary>
/// F3 redesign of the TmDashboard responsive grid and header against
/// planning/dashboard-redesign/f3-ux-spec.md: the DOM (and therefore Tab and screen readers)
/// follows the visual (Y, X) order in every mode, a collapsed or minimized card releases its
/// height on tablet and mobile, the tablet grid grows content-driven heights, the view-mode
/// header stacks on tablet and mobile whenever a visible heading renders, and the root is a
/// named region. bUnit cannot run container queries, so every compact-mode test forces
/// <see cref="TmLayoutMode.Tablet"/>/<see cref="TmLayoutMode.Mobile"/>.
/// </summary>
public class TmDashboardResponsiveTests : LocalizationTestBase
{
    private (IDashboardProvider Provider, IWidgetRegistry Registry) SetupDashboard(DashboardConfig dashboard)
    {
        var provider = Substitute.For<IDashboardProvider>();
        provider.GetDashboardsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<IEnumerable<DashboardConfig>>([dashboard]));
        provider.GetDefaultDashboardAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<DashboardConfig?>(dashboard));
        provider.GetDashboardAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(ci => Task.FromResult<DashboardConfig?>(
                    dashboard.Id == ci.Arg<string>() ? dashboard : null));
        provider.SaveDashboardAsync(Arg.Any<DashboardConfig>(), Arg.Any<CancellationToken>())
                .Returns(ci => Task.FromResult(ci.Arg<DashboardConfig>()));
        Services.AddSingleton(provider);

        var registry = Substitute.For<IWidgetRegistry>();
        registry.GetWidget(Arg.Any<string>()).Returns((WidgetDefinition?)null);
        Services.AddSingleton(registry);
        return (provider, registry);
    }

    private static DashboardConfig Dashboard(string id, string name, bool isDefault = false, params WidgetInstance[] widgets) =>
        new() { Id = id, Name = name, IsDefault = isDefault, Widgets = [.. widgets] };

    // ── §4: DOM order = (Y, X) in every mode ──────────────────────

    [Fact]
    public void Dashboard_WidgetOrder_FollowsRowThenColumn_OnDesktop()
    {
        SetupDashboard(Dashboard("d1", "Pilot", isDefault: true,
            // Added out of order: C sits on row 4, A and B share row 1 (B right of A).
            new WidgetInstance { InstanceId = "c", WidgetId = "kpi", X = 0, Y = 4, Width = 4, Height = 2 },
            new WidgetInstance { InstanceId = "a", WidgetId = "kpi", X = 0, Y = 0, Width = 4, Height = 2 },
            new WidgetInstance { InstanceId = "b", WidgetId = "kpi", X = 4, Y = 0, Width = 4, Height = 2 }));

        var cut = Render<TmDashboard>(p => p
            .Add(x => x.DashboardId, "d1")
            .Add(x => x.LayoutMode, TmLayoutMode.Desktop));

        cut.FindAll(".tm-widget")
            .Select(w => w.GetAttribute("data-instance-id"))
            .Should().ContainInOrder(new[] { "a", "b", "c" },
                "the DOM order is the visual row-then-column order, so Tab and the screen reader match the eye (WCAG 2.4.3/1.3.2)");
        cut.Find("[data-instance-id='a']").GetAttribute("style").Should().Contain("--tm-w-order: 1");
        cut.Find("[data-instance-id='b']").GetAttribute("style").Should().Contain("--tm-w-order: 2");
        cut.Find("[data-instance-id='c']").GetAttribute("style").Should().Contain("--tm-w-order: 3");
    }

    [Fact]
    public void Dashboard_WidgetOrder_FollowsRowThenColumn_OnMobile()
    {
        SetupDashboard(Dashboard("d1", "Pilot", isDefault: true,
            new WidgetInstance { InstanceId = "c", WidgetId = "kpi", X = 0, Y = 4, Width = 4, Height = 2 },
            new WidgetInstance { InstanceId = "a", WidgetId = "kpi", X = 0, Y = 0, Width = 4, Height = 2 },
            new WidgetInstance { InstanceId = "b", WidgetId = "kpi", X = 4, Y = 0, Width = 4, Height = 2 }));

        var cut = Render<TmDashboard>(p => p
            .Add(x => x.DashboardId, "d1")
            .Add(x => x.LayoutMode, TmLayoutMode.Mobile));

        cut.FindAll(".tm-widget")
            .Select(w => w.GetAttribute("data-instance-id"))
            .Should().ContainInOrder(new[] { "a", "b", "c" },
                "the mobile stack reads in the same order as the desktop grid");
        cut.Find("[data-instance-id='c']").GetAttribute("style").Should().Contain("--tm-w-order: 3");
    }

    [Fact]
    public async Task Dashboard_WidgetOrder_ReSortedAfterAMove()
    {
        SetupDashboard(Dashboard("d1", "Pilot", isDefault: true,
            new WidgetInstance { InstanceId = "first", WidgetId = "kpi", X = 0, Y = 0, Width = 4, Height = 2 },
            new WidgetInstance { InstanceId = "second", WidgetId = "kpi", X = 4, Y = 0, Width = 4, Height = 2 }));
        var cut = Render<TmDashboard>(p => p.Add(x => x.DashboardId, "d1"));

        // A drop below the second widget (OnGridPositionChanged fires at drop) re-sorts the DOM.
        await cut.InvokeAsync(() => cut.Instance.OnGridPositionChanged("first", 0, 4));

        cut.FindAll(".tm-widget")
            .Select(w => w.GetAttribute("data-instance-id"))
            .Should().ContainInOrder(new[] { "second", "first" },
                "the drop re-renders, and the render re-sorts the DOM to the new (Y, X)");
    }

    // ── §5: collapse / minimize release rows without !important ───

    [Theory]
    [InlineData(TmLayoutMode.Mobile)]
    [InlineData(TmLayoutMode.Tablet)]
    public void Dashboard_CollapsedWidget_ReleasesRows(TmLayoutMode mode)
    {
        SetupDashboard(Dashboard("d1", "Pilot", isDefault: true,
            new WidgetInstance { InstanceId = "w1", WidgetId = "kpi", X = 0, Y = 0, Width = 4, Height = 2 },
            new WidgetInstance { InstanceId = "w2", WidgetId = "kpi", X = 0, Y = 2, Width = 4, Height = 2 }));
        var cut = Render<TmDashboard>(p => p
            .Add(x => x.DashboardId, "d1")
            .Add(x => x.LayoutMode, mode));

        cut.Find("[data-instance-id='w1'] .tm-widget-btn").Click();

        cut.Find("[data-instance-id='w1']").ClassList.Should().Contain("tm-widget--collapsed");
        cut.FindAll("[data-instance-id='w1'] .tm-widget-content").Should().BeEmpty("the collapsed card is its header");
        // The slot model is untouched: expanding restores the original height, and dashboard.js
        // still reads the saved Height for its drop preview.
        cut.Find("[data-instance-id='w1']").GetAttribute("style").Should().Contain("--tm-w-rows: 2");
    }

    [Fact]
    public void Dashboard_MinimizedWidget_CarriesTheAliasClass_AndKeepsItsSlotVariables()
    {
        SetupDashboard(Dashboard("d1", "Pilot", isDefault: true,
            new WidgetInstance { InstanceId = "w1", WidgetId = "kpi", X = 2, Y = 1, Width = 4, Height = 3, IsMinimized = true }));
        var cut = Render<TmDashboard>(p => p
            .Add(x => x.DashboardId, "d1")
            .Add(x => x.LayoutMode, TmLayoutMode.Desktop));

        var widget = cut.Find("[data-instance-id='w1']");
        widget.ClassList.Should().Contain("tm-widget--minimized", "IsMinimized is the layout alias of collapse (M-7)");
        widget.GetAttribute("style").Should().Contain("--tm-w-y: 2")
            .And.Contain("--tm-w-rows: 3", "the minimized card keeps its slot variables — CSS, not a !important reset, sizes it");
    }

    [Fact]
    public void Dashboard_Css_CollapseReleasesRows_WithoutImportant()
    {
        var css = DashboardCss();

        css.Should().NotMatchRegex(@"grid-(row|column)\s*:\s*[^;}]*!important",
            "placement is variable-driven; the minimized rule's !important also reset the desktop Y");
        css.Should().MatchRegex(
            @"\.tm-dashboard-grid \.tm-widget--collapsed\s*,\s*\.tm-dashboard-grid \.tm-widget--minimized\s*\{[^}]*align-self:\s*start",
            "on desktop the slot stays reserved (M-1) and only the card shrinks to its header");
        css.Should().MatchRegex(
            @"\[data-layout=.tablet.\][^{]*\.tm-widget--collapsed\s*,[^{]*--minimized[^{]*\{[^}]*grid-row:\s*auto[^}]*min-height:\s*auto",
            "tablet and mobile release the height so the stack closes up");
        css.Should().MatchRegex(
            @"\[data-layout=.mobile.\][^{]*\.tm-widget--collapsed\s*,[^{]*--minimized[^{]*\{[^}]*grid-row:\s*auto[^}]*min-height:\s*auto");
    }

    // ── §3.2/§3.3: compact-mode heights ───────────────────────────

    [Fact]
    public void Dashboard_Css_TabletGrowsAutoHeights_AndMobileIncludesTheGaps()
    {
        var css = DashboardCss();

        css.Should().MatchRegex(
            @"\.tm-dashboard-grid \.tm-widget\s*\{[^}]*--tm-w-min-block:\s*calc\([^;]*--tm-w-rows[^;]*--row-height[^;]*--grid-gap",
            "the compact min-height equals the desktop slot height, inner gaps included");
        css.Should().MatchRegex(
            @"\[data-layout=.tablet.\]\s+\.tm-dashboard-grid\s*\{[^}]*grid-auto-rows:\s*auto",
            "the tablet grid is content-driven (M-3), not fixed rows that clip");
        css.Should().MatchRegex(
            @"\[data-layout=.tablet.\]\s+\.tm-dashboard-grid \.tm-widget\s*\{[^}]*grid-row:\s*auto;[^}]*min-height:\s*var\(--tm-w-min-block\)");
        css.Should().MatchRegex(
            @"\.tm-dashboard\[data-layout=.mobile.\] \.tm-dashboard-grid \.tm-widget,\s*\.tm-dashboard\[data-layout=.mobile.\] \.tm-dashboard-grid \.tm-widget--wide\s*\{[^}]*min-height:\s*var\(--tm-w-min-block\)",
            "the mobile min-height includes the (H-1) inner gaps (M-9) — the desktop slot height");
    }

    // ── §2: the stacked header on tablet and mobile ───────────────

    [Fact]
    public void Dashboard_Mobile_HeaderStacksTitleAboveActions()
    {
        SetupDashboard(Dashboard("d1", "Aurora", isDefault: true));
        var cut = Render<TmDashboard>(p => p
            .Add(x => x.LayoutMode, TmLayoutMode.Mobile)
            .Add(x => x.Title, "Project Aurora")
            .Add(x => x.HeaderActions, (RenderFragment)(builder =>
            {
                builder.OpenElement(0, "span");
                builder.AddContent(1, "host-action");
                builder.CloseElement();
            })));

        var toolbar = cut.Find(".tm-dashboard-toolbar");
        toolbar.ClassList.Should().Contain("tm-dashboard-toolbar--with-heading",
            "a visible heading is what makes the header worth stacking");
        toolbar.ClassList.Should().Contain("tm-dashboard-toolbar--with-actions");

        var css = DashboardCss();
        MobileRule(css, ".tm-dashboard-heading").Should().Contain("grid-row: 1", "the title is the first row");
        MobileRule(css, ".tm-dashboard-views").Should().Contain("grid-row: 2", "the switcher sits below the title");
        MobileRule(css, ".tm-dashboard-toolbar-right").Should().Contain("grid-row: 2", "the Edit button shares the switcher's row");
        MobileRule(css, ".tm-dashboard-toolbar--with-actions .tm-dashboard-toolbar-right").Should().Contain("grid-row: 3",
            "with host actions the right group moves under the switcher");
        css.Should().MatchRegex(
            @"\.tm-dashboard\[data-layout=.mobile.\] \.tm-dashboard-title\s*\{[^}]*--tm-font-size-xl",
            "the mobile title steps down to xl (reference ~20px, long names at 320px)");
    }

    [Fact]
    public void Dashboard_Tablet_HeaderPutsTheSwitcherAboveTheTitle()
    {
        SetupDashboard(Dashboard("d1", "Aurora", isDefault: true));
        var cut = Render<TmDashboard>(p => p
            .Add(x => x.LayoutMode, TmLayoutMode.Tablet)
            .Add(x => x.Title, "Project Aurora"));

        cut.Find(".tm-dashboard-toolbar").ClassList.Should().Contain("tm-dashboard-toolbar--with-heading");

        var css = DashboardCss();
        TabletRule(css, ".tm-dashboard-header-start").Should().Contain("grid-row: 1",
            "HeaderStart shares row 1 with the switcher (M-6: focus order = visual order)");
        TabletRule(css, ".tm-dashboard-views").Should().Contain("grid-row: 1");
        TabletRule(css, ".tm-dashboard-heading").Should().Contain("grid-row: 2", "title + subtitle go under the switcher row");
        TabletRule(css, ".tm-dashboard-toolbar-right").Should().Contain("grid-row: 2", "the Edit button sits at the title's level");
    }

    [Fact]
    public void Dashboard_TitleHidden_ToolbarHasNoHeadingModifier()
    {
        // Title null + a provider: the switcher already shows the name (the h2 is sr-only), so the
        // header stays a single flex row even on tablet and mobile.
        SetupDashboard(Dashboard("d1", "Aurora", isDefault: true));
        var cut = Render<TmDashboard>(p => p.Add(x => x.LayoutMode, TmLayoutMode.Mobile));

        cut.Find(".tm-dashboard-toolbar").ClassList.Should().NotContain("tm-dashboard-toolbar--with-heading");
        cut.Find(".tm-dashboard-title").ClassList.Should().Contain("tm-sr-only");

        // ShowTitle=false is the other no-heading case.
        var hidden = Render<TmDashboard>(p => p
            .Add(x => x.LayoutMode, TmLayoutMode.Mobile)
            .Add(x => x.ShowTitle, false));
        hidden.Find(".tm-dashboard-toolbar").ClassList.Should().NotContain("tm-dashboard-toolbar--with-heading");
    }

    [Fact]
    public void Dashboard_HeaderActions_AddsActionsModifier_EditModeEmitsNeither()
    {
        SetupDashboard(Dashboard("d1", "Aurora", isDefault: true));
        var withActions = Render<TmDashboard>(p => p.Add(
            x => x.HeaderActions,
            (RenderFragment)(builder =>
            {
                builder.OpenElement(0, "span");
                builder.AddContent(1, "host-action");
                builder.CloseElement();
            })));
        withActions.Find(".tm-dashboard-toolbar").ClassList.Should().Contain("tm-dashboard-toolbar--with-actions");

        var withoutActions = Render<TmDashboard>();
        withoutActions.Find(".tm-dashboard-toolbar").ClassList.Should().NotContain("tm-dashboard-toolbar--with-actions");

        // Edit mode always renders the desktop-style flex header — no stacking modifiers there.
        withoutActions.Find("[data-testid='dashboard-edit']").Click();
        withoutActions.Find(".tm-dashboard-toolbar").ClassList.Should().NotContain("tm-dashboard-toolbar--with-heading");
        withoutActions.Find(".tm-dashboard-toolbar").ClassList.Should().NotContain("tm-dashboard-toolbar--with-actions");
    }

    [Fact]
    public void Dashboard_Css_MobileWithoutHeading_KeepsOneRow()
    {
        var css = DashboardCss();

        css.Should().NotMatchRegex(
            @"\[data-layout=.mobile.\]\s+\.tm-dashboard-toolbar-right\s*\{[^}]*flex-basis:\s*100%",
            "the full-width wrap would force the Edit button onto its own row in the one-row no-heading header");
        css.Should().MatchRegex(
            @"\.tm-dashboard\[data-layout=.mobile.\] \.tm-dashboard-views\s*\{[^}]*flex:\s*1 1 auto",
            "the switcher takes the free space beside the Edit button");
        css.Should().MatchRegex(
            @"\.tm-dashboard\[data-layout=.mobile.\] \.tm-dashboard-switcher\s*\{[^}]*min-inline-size:\s*0",
            "the F2 14rem floor must go on mobile or 320px overflows");
    }

    // ── §8.3: the root is a named region ──────────────────────────

    [Fact]
    public void Dashboard_Root_IsRegionLabelledByTitle()
    {
        SetupDashboard(Dashboard("d1", "Aurora", isDefault: true));

        // Visible heading (host Title).
        var titled = Render<TmDashboard>(p => p.Add(x => x.Title, "Project Aurora"));
        var root = titled.Find(".tm-dashboard");
        root.GetAttribute("role").Should().Be("region");
        root.GetAttribute("aria-labelledby").Should().Be(titled.Find(".tm-dashboard-title").GetAttribute("id"));

        // Visually hidden heading (Title null + provider) still labels the region.
        var hidden = Render<TmDashboard>();
        hidden.Find(".tm-dashboard").GetAttribute("aria-labelledby")
            .Should().Be(hidden.Find(".tm-dashboard-title").GetAttribute("id"),
                "the sr-only h2 is still the accessible name of the region");

        // No heading at all: the fallback aria-label names the region.
        var nameless = Render<TmDashboard>(p => p.Add(x => x.ShowTitle, false));
        nameless.Find(".tm-dashboard").GetAttribute("aria-label").Should().Be("Aurora");
        nameless.Find(".tm-dashboard").GetAttribute("aria-labelledby").Should().BeNull();

        // Edit mode renders the h2 too (the rename target), so the region stays labelled.
        nameless.Find("[data-testid='dashboard-edit']").Click();
        nameless.Find(".tm-dashboard").GetAttribute("aria-labelledby")
            .Should().Be(nameless.Find(".tm-dashboard-title").GetAttribute("id"));
    }

    // ── §7: touch targets and the drag cursor ─────────────────────

    [Fact]
    public void Dashboard_Css_TouchTargets_CoverHeaderSwitcherAndMenu()
    {
        var css = DashboardCss();

        css.Should().MatchRegex(@"\.tm-widget-btn[^{]*\{[^}]*min-height:\s*var\(--tm-touch-target\)",
            "the coarse block keeps the 44px chevron");
        css.Should().MatchRegex(
            @"\.tm-dashboard\[data-layout=.mobile.\] \.tm-widget-btn\s*,[^{]*\{[^}]*min-height:\s*var\(--tm-touch-target\)",
            "a mobile layout is a touch layout regardless of the pointer hardware (M-4)");
        css.Should().MatchRegex(
            @"\.tm-dashboard-switcher\s*[,{][^}]*min-block-size:\s*var\(--tm-touch-target\)");
        css.Should().MatchRegex(
            @"\.tm-dashboard\[data-layout=.mobile.\] \.tm-dashboard-switcher\s*,[^{]*\{[^}]*min-block-size:\s*var\(--tm-touch-target\)");
        css.Should().MatchRegex(
            @"\.tm-dashboard-toolbar \.tm-btn\s*[,{][^}]*min-block-size:\s*var\(--tm-touch-target\)");
        css.Should().MatchRegex(
            @"\.tm-dashboard-menu-item__select\s*,[^{]*\{[^}]*min-block-size:\s*var\(--tm-touch-target\)",
            "the switcher menu rows are coarse targets too");
        css.Should().MatchRegex(
            @"\.tm-dashboard-empty \.tm-btn\s*\{[^}]*min-block-size:\s*var\(--tm-touch-target\)");
    }

    [Fact]
    public void Dashboard_Css_GrabCursor_IsDesktopOnly()
    {
        var css = DashboardCss();

        css.Should().MatchRegex(
            @"\.tm-dashboard--edit\[data-layout=.desktop.\] \.tm-widget-header\s*\{[^}]*cursor:\s*grab",
            "dragging exists only on the desktop grid");
        css.Should().NotMatchRegex(
            @"(?m)^\s*\.tm-dashboard--edit \.tm-widget-header\s*\{[^}]*cursor",
            "an unscoped grab cursor would lie on tablet and mobile where no drag exists");
    }

    [Fact]
    public void Dashboard_ForcedMobileHost_GridStacks()
    {
        SetupDashboard(Dashboard("d1", "Nested", isDefault: true,
            new WidgetInstance { InstanceId = "w1", WidgetId = "kpi", X = 0, Y = 0, Width = 4, Height = 2 }));
        var cut = Render<ForcedMobileHost>();

        cut.Find(".tm-dashboard").GetAttribute("data-layout").Should().Be("mobile");
        cut.Find("[data-instance-id='w1']").GetAttribute("style").Should().Contain("--tm-w-order: 1");
    }

    /// <summary>A forced Mobile observer hosting a dashboard through ordinary child content.</summary>
    private sealed class ForcedMobileHost : ComponentBase
    {
        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenComponent<TmLayoutObserver>(0);
            builder.AddAttribute(1, "LayoutMode", TmLayoutMode.Mobile);
            builder.AddAttribute(2, "ChildContent", (RenderFragment)(inner =>
            {
                inner.OpenComponent<TmDashboard>(0);
                inner.AddAttribute(1, "DashboardId", "d1");
                inner.CloseComponent();
            }));
            builder.CloseComponent();
        }
    }

    // ── CSS helpers ───────────────────────────────────────────────

    private static string DashboardCss() =>
        File.ReadAllText(Path.Combine(
            RepoRoot(), "src", "Tempo.Blazor", "wwwroot", "css", "components", "_dashboard.css"));

    /// <summary>The block of the F3 mobile stacked-header rule for one element inside the toolbar grid.</summary>
    private static string MobileRule(string css, string element) => ModeRule(css, "mobile", element);

    private static string TabletRule(string css, string element) => ModeRule(css, "tablet", element);

    private static string ModeRule(string css, string mode, string element)
    {
        var pattern = @"\.tm-dashboard\[data-layout=." + mode + @".\]:not\(\.tm-dashboard--edit\) [^{]*" +
                      Regex.Escape(element) + @"\s*\{";
        var match = Regex.Match(css, pattern);
        match.Success.Should().BeTrue("the stacked-header rule for {0} must exist in the {1} block", element, mode);
        var end = css.IndexOf('}', match.Index);
        return css[match.Index..end];
    }

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TempoBlazor.slnx")))
            directory = directory.Parent;
        directory.Should().NotBeNull("the repository root should be discoverable from the test output directory");
        return directory!.FullName;
    }
}
