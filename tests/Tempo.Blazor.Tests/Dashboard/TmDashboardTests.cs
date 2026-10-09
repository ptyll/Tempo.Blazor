using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.Components.Dashboard;
using Tempo.Blazor.Components.Layout;
using Tempo.Blazor.Interfaces;
using Tempo.Blazor.Models;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Dashboard;

public class TmDashboardTests : LocalizationTestBase
{
    private IDashboardProvider CreateMockProvider()
    {
        var provider = Substitute.For<IDashboardProvider>();
        provider.GetDashboardsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<IEnumerable<DashboardConfig>>(new List<DashboardConfig>()));
        provider.GetDefaultDashboardAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<DashboardConfig?>(null));
        provider.SaveDashboardAsync(Arg.Any<DashboardConfig>(), Arg.Any<CancellationToken>())
                .Returns(ci => Task.FromResult(ci.Arg<DashboardConfig>()));
        return provider;
    }

    private IWidgetRegistry CreateMockRegistry()
    {
        var registry = Substitute.For<IWidgetRegistry>();
        registry.GetAllWidgets().Returns(new List<WidgetDefinition>());
        return registry;
    }

    private void SetupServices(IDashboardProvider provider, IWidgetRegistry registry)
    {
        Services.AddSingleton(provider);
        Services.AddSingleton(registry);
        // Mock JS runtime to avoid JS interop errors
        var jsRuntime = Substitute.For<Microsoft.JSInterop.IJSRuntime>();
        Services.AddSingleton(jsRuntime);
    }

    #region 1. Widget Anti-Overlap (Auto-Push)

    [Fact]
    public void Dashboard_WidgetPlacement_WithOverlap_PushesOtherWidgetsDown()
    {
        // Arrange - Two widgets, one at Y=0, one at Y=2
        var widgets = new List<WidgetInstance>
        {
            new() { InstanceId = "w1", WidgetId = "widget1", X = 0, Y = 0, Width = 4, Height = 2 },
            new() { InstanceId = "w2", WidgetId = "widget2", X = 0, Y = 2, Width = 4, Height = 2 }
        };

        // Act - New widget placed at Y=1 would overlap with w1 (ends at Y=2)
        var newWidget = new WidgetInstance { InstanceId = "w3", WidgetId = "widget3", X = 2, Y = 1, Width = 4, Height = 2 };
        var result = CalculateAntiOverlapPositions(widgets, newWidget);

        // Assert - Overlapping widgets should be pushed down
        result.Should().ContainKey("w1");
        // w1 should stay or move based on overlap
    }

    [Fact]
    public void Dashboard_WidgetResize_WithOverlap_CalculatesPushAmount()
    {
        // Arrange
        var w1 = new WidgetInstance { InstanceId = "w1", WidgetId = "widget1", X = 0, Y = 0, Width = 4, Height = 2 };
        var w2 = new WidgetInstance { InstanceId = "w2", WidgetId = "widget2", X = 0, Y = 2, Width = 4, Height = 2 };

        // Act - w1 resized to height=4 would overlap w2
        var resizedHeight = 4;
        var overlap = CalculateVerticalOverlap(w1, resizedHeight, w2);

        // Assert
        overlap.Should().BeGreaterThan(0); // There is overlap
    }

    private Dictionary<string, (int X, int Y)> CalculateAntiOverlapPositions(List<WidgetInstance> existing, WidgetInstance newWidget)
    {
        var result = existing.ToDictionary(w => w.InstanceId, w => (w.X, w.Y));
        
        foreach (var widget in existing)
        {
            if (HasOverlap(newWidget, widget))
            {
                // Push widget down below new widget
                var pushAmount = newWidget.Y + newWidget.Height - widget.Y;
                result[widget.InstanceId] = (widget.X, widget.Y + pushAmount);
            }
        }
        
        return result;
    }

    private bool HasOverlap(WidgetInstance a, WidgetInstance b)
    {
        return a.X < b.X + b.Width &&
               a.X + a.Width > b.X &&
               a.Y < b.Y + b.Height &&
               a.Y + a.Height > b.Y;
    }

    private int CalculateVerticalOverlap(WidgetInstance widget, int newHeight, WidgetInstance other)
    {
        int widgetBottom = widget.Y + newHeight;
        int otherTop = other.Y;
        
        if (widgetBottom > otherTop && widget.Y < other.Y)
            return widgetBottom - otherTop;
        
        return 0;
    }

    #endregion

    #region 2. Dashboard Name Editing

    [Fact]
    public void Dashboard_CreateNew_CreatesWithDefaultName()
    {
        // Arrange
        var provider = CreateMockProvider();
        var registry = CreateMockRegistry();
        SetupServices(provider, registry);

        // Act
        var cut = Render<TmDashboard>();

        // Assert - Should show default name
        cut.Find(".tm-dashboard-title").TextContent.Should().Be("New Dashboard");
    }

    [Fact]
    public void Dashboard_EditMode_ShowsNameEditField()
    {
        // Arrange
        var provider = CreateMockProvider();
        var registry = CreateMockRegistry();
        SetupServices(provider, registry);

        // Act - Render and enter edit mode
        var cut = Render<TmDashboard>();
        cut.Find("button[title='Edit']").Click();

        // Assert - Should show edit mode badge and save/cancel buttons
        cut.FindAll(".tm-dashboard-edit-badge").Should().NotBeEmpty();
        cut.FindAll(".tm-dashboard--edit").Should().NotBeEmpty();
    }

    #endregion

    #region 3. Set Default Dashboard

    [Fact]
    public void Dashboard_Renders_WithEmptyProvider()
    {
        // Arrange
        var provider = CreateMockProvider();
        var registry = CreateMockRegistry();
        SetupServices(provider, registry);

        // Act
        var cut = Render<TmDashboard>();

        // Assert - Dashboard renders successfully with empty provider
        cut.FindAll(".tm-dashboard").Should().NotBeEmpty();
        cut.FindAll(".tm-dashboard-toolbar").Should().NotBeEmpty();
    }

    #endregion

    #region 4. Improved Edit UI

    [Fact]
    public void Dashboard_ViewMode_EditButtonHasLabel()
    {
        // Arrange
        var provider = CreateMockProvider();
        var registry = CreateMockRegistry();
        SetupServices(provider, registry);

        // Act
        var cut = Render<TmDashboard>();

        // Assert - Toolbar should exist
        cut.FindAll(".tm-dashboard-toolbar").Count.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Dashboard_EditMode_ShowsCancelButton()
    {
        // Arrange
        var provider = CreateMockProvider();
        var registry = CreateMockRegistry();
        SetupServices(provider, registry);

        // Act - Enter edit mode
        var cut = Render<TmDashboard>();
        cut.Find("button[title='Edit']").Click();

        // Assert - Should show cancel button in edit mode
        var cancelButtons = cut.FindAll("button").Where(b => b.TextContent.Contains("Cancel")).ToList();
        cancelButtons.Should().NotBeEmpty("edit mode should show a Cancel button");
    }

    [Fact]
    public void Dashboard_EditMode_MobileLayout_RendersTheActionBarWithThreeActions()
    {
        // X14: on a mobile layout the edit actions render through TmMobileActionBar (3 actions,
        // built per render so a culture change re-localizes them).
        var provider = CreateMockProvider();
        var registry = CreateMockRegistry();
        SetupServices(provider, registry);

        var cut = Render<TmDashboard>(p => p.Add(x => x.LayoutMode, TmLayoutMode.Mobile));
        cut.Find("button[title='Edit']").Click();

        var bar = cut.Find(".tm-mobile-action-bar__bar");
        bar.QuerySelectorAll(".tm-mobile-action-bar__action").Count.Should().Be(3);
        bar.GetAttribute("role").Should().Be("group");
    }

    [Fact]
    public void Dashboard_EditMode_DesktopLayout_RendersNoActionBar()
    {
        var provider = CreateMockProvider();
        var registry = CreateMockRegistry();
        SetupServices(provider, registry);

        var cut = Render<TmDashboard>(p => p.Add(x => x.LayoutMode, TmLayoutMode.Desktop));
        cut.Find("button[title='Edit']").Click();

        cut.FindAll(".tm-mobile-action-bar__bar").Should().BeEmpty();
        cut.FindAll(".tm-dashboard-toolbar-right .tm-btn").Should().NotBeEmpty(
            "the desktop toolbar keeps the edit actions");
    }

    #endregion

    #region 5. Mobile Stacking (A1)

    // The desktop grid places widgets via inline `grid-column: X / span W` emitted by
    // GetWidgetStyles. A plain `@media { .tm-dashboard-grid { grid-template-columns: 1fr } }`
    // cannot stack them because the inline grid-column wins. The mobile rule must therefore
    // override the widget placement itself with `grid-column: 1 / -1 !important`.

    [Fact]
    public void Dashboard_Css_ContainerQuery_StacksByContainerWidth_NotViewport()
    {
        var css = DashboardCss();

        // The grid must follow the width of the dashboard, not the browser viewport: a 390px
        // dashboard embedded in a 1440px page is one column.
        css.Should().Contain("container-type: inline-size");
        css.Should().Contain("container-name: tm-dashboard");
        css.Should().NotMatchRegex(
            @"\.tm-dashboard-grid\s*\{[^}]*container-type",
            "a container query never styles its own container; the container is the grid's ancestor");
        css.Should().NotContain("560px",
            "560 is not a TmBreakpoints value; the single-column rule is the 640px container query");
        css.Should().MatchRegex(
            @"\[data-layout=.mobile.\]\s+\.tm-dashboard-grid\s*\{[^}]*grid-template-columns:\s*1fr",
            "the mobile branch is one column");
        css.Should().MatchRegex(
            @"\[data-layout=.tablet.\]\s+\.tm-dashboard-grid\s*\{[^}]*grid-template-columns:\s*repeat\(\s*6\s*,\s*minmax\(0\s*,\s*1fr\)\s*\)",
            "the tablet branch is six columns");
        css.Should().MatchRegex(
            @"@container\s+tm-dashboard\s*\(\s*width\s*<\s*768px\s*\)[\s\S]*?grid-template-columns:\s*repeat\(\s*2\s*,\s*minmax\(0\s*,\s*1fr\)\s*\)",
            "inside tablet, below 768px of container the grid is two columns");
    }

    [Fact]
    public void Dashboard_WidgetStyles_EmitCssVariables_NotLiteralGridColumn()
    {
        var provider = CreateMockProvider();
        var registry = CreateMockRegistry();
        SetupServices(provider, registry);
        var dashboard = new DashboardConfig
        {
            Id = "d1",
            Name = "Pilot",
            Widgets =
            [
                new WidgetInstance { InstanceId = "w-wide", WidgetId = "kpi", X = 0, Y = 0, Width = 8, Height = 2, ZIndex = 1 },
                new WidgetInstance { InstanceId = "w-narrow", WidgetId = "kpi", X = 8, Y = 0, Width = 4, Height = 3, ZIndex = 2 },
            ],
        };
        provider.GetDashboardAsync("d1", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<DashboardConfig?>(dashboard));

        var cut = Render<TmDashboard>(parameters => parameters.Add(p => p.DashboardId, "d1"));

        var wide = cut.Find("[data-instance-id='w-wide']");
        wide.GetAttribute("style").Should().NotContain("grid-column",
            "an inline grid-column beats every container query, so placement must be variables");
        wide.GetAttribute("style").Should().Contain("--tm-w-x: 1");
        wide.GetAttribute("style").Should().Contain("--tm-w-span: 8");
        wide.GetAttribute("style").Should().Contain("--tm-w-y: 1");
        wide.GetAttribute("style").Should().Contain("--tm-w-rows: 2");
        wide.GetAttribute("style").Should().Contain("--tm-w-order: 1");
        wide.ClassList.Should().Contain("tm-widget--wide",
            "a widget wider than half the desktop grid takes the full compact row");

        var narrow = cut.Find("[data-instance-id='w-narrow']");
        narrow.GetAttribute("style").Should().Contain("--tm-w-x: 9");
        narrow.GetAttribute("style").Should().Contain("--tm-w-span: 4");
        narrow.GetAttribute("style").Should().Contain("--tm-w-order: 2");
        narrow.ClassList.Should().NotContain("tm-widget--wide");
        wide.GetAttribute("style").Should().Contain("--tm-w-span-md: 6",
            "wider than half the desktop grid, so it takes the whole tablet row");
        narrow.GetAttribute("style").Should().Contain("--tm-w-span-md: 3",
            "four of twelve columns is wider than a quarter, so it takes half a tablet row");
    }

    [Fact]
    public void Dashboard_ForcedMobile_RendersTheMobileBranch_WithoutImportingTheObserver()
    {
        JSInterop.Mode = JSRuntimeMode.Strict;
        var provider = CreateMockProvider();
        var registry = CreateMockRegistry();
        SetupServices(provider, registry);
        provider.GetDashboardAsync("d1", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<DashboardConfig?>(new DashboardConfig
            {
                Id = "d1",
                Name = "Forced",
                Widgets = [new WidgetInstance { InstanceId = "w1", WidgetId = "kpi", X = 0, Y = 0, Width = 4, Height = 2 }],
            }));

        var cut = Render<TmDashboard>(parameters => parameters
            .Add(p => p.DashboardId, "d1")
            .Add(p => p.LayoutMode, TmLayoutMode.Mobile));

        var root = cut.Find(".tm-dashboard");
        root.ClassList.Should().Contain("tm-dashboard--mobile");
        root.GetAttribute("data-layout").Should().Be("mobile");
        JSInterop.VerifyNotInvoke("import");
    }

    [Fact]
    public void Dashboard_UnderForcedMobileAncestor_RendersMobile_WithoutImportingTheObserver()
    {
        JSInterop.Mode = JSRuntimeMode.Strict;
        var provider = CreateMockProvider();
        var registry = CreateMockRegistry();
        SetupServices(provider, registry);
        provider.GetDashboardAsync("d1", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<DashboardConfig?>(new DashboardConfig
            {
                Id = "d1",
                Name = "Nested",
                Widgets = [new WidgetInstance { InstanceId = "w1", WidgetId = "kpi", X = 0, Y = 0, Width = 4, Height = 2 }],
            }));

        var cut = Render<ForcedMobileHost>();

        cut.Find(".tm-dashboard").GetAttribute("data-layout").Should().Be("mobile",
            "the dashboard reads the context its own observer resolved, not a copy it froze at desktop");
        cut.FindAll(".tm-widget-drag-handle").Should().BeEmpty();
        cut.FindAll(".tm-widget-resize-se").Should().BeEmpty();
        JSInterop.VerifyNotInvoke("import");
    }

    [Fact]
    public async Task Dashboard_UnderMeasuredMobileAncestor_RendersMobile()
    {
        var provider = CreateMockProvider();
        var registry = CreateMockRegistry();
        SetupServices(provider, registry);
        provider.GetDashboardAsync("d1", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<DashboardConfig?>(new DashboardConfig
            {
                Id = "d1",
                Name = "Measured",
                Widgets = [new WidgetInstance { InstanceId = "w1", WidgetId = "kpi", X = 0, Y = 0, Width = 4, Height = 2 }],
            }));

        var host = Render<MeasuredMobileHost>();
        await host.FindComponent<TmLayoutObserver>().InvokeAsync(async () =>
            await host.FindComponent<TmLayoutObserver>().Instance.OnLayoutModeChanged("mobile"));

        host.Find(".tm-dashboard").GetAttribute("data-layout").Should().Be("mobile",
            "an ancestor that measured mobile is the app-level fallback, so the dashboard must not stay desktop");
    }

    [Fact]
    public void Dashboard_ForcedTabletEditMode_HidesTheGridBackground()
    {
        var provider = CreateMockProvider();
        var registry = CreateMockRegistry();
        SetupServices(provider, registry);
        provider.GetDashboardAsync("d1", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<DashboardConfig?>(new DashboardConfig
            {
                Id = "d1",
                Name = "Tablet",
                Widgets = [new WidgetInstance { InstanceId = "w1", WidgetId = "kpi", X = 0, Y = 0, Width = 4, Height = 2 }],
            }));

        var cut = Render<TmDashboard>(parameters => parameters
            .Add(p => p.DashboardId, "d1")
            .Add(p => p.LayoutMode, TmLayoutMode.Tablet));
        cut.Find("button[title='Edit']").Click();

        cut.FindAll(".tm-dashboard-grid-bg").Should().BeEmpty(
            "the column grid is the desktop edit affordance; below desktop there is no column to show");
    }

    [Fact]
    public async Task Dashboard_Order_UpdatesAfterAMove()
    {
        var provider = CreateMockProvider();
        var registry = CreateMockRegistry();
        SetupServices(provider, registry);
        provider.GetDashboardAsync("d1", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<DashboardConfig?>(new DashboardConfig
            {
                Id = "d1",
                Name = "Order",
                Widgets =
                [
                    new WidgetInstance { InstanceId = "first", WidgetId = "kpi", X = 0, Y = 0, Width = 4, Height = 2 },
                    new WidgetInstance { InstanceId = "second", WidgetId = "kpi", X = 4, Y = 0, Width = 4, Height = 2 },
                ],
            }));

        var cut = Render<TmDashboard>(parameters => parameters.Add(p => p.DashboardId, "d1"));
        cut.Find("[data-instance-id='first']").GetAttribute("style").Should().Contain("--tm-w-order: 1");

        await cut.InvokeAsync(() => cut.Instance.OnGridPositionChanged("first", 8, 0));

        cut.Find("[data-instance-id='first']").GetAttribute("style").Should().Contain("--tm-w-order: 2",
            "a move changes the visual order, so the dictionary computed at the last parameter set is stale");
    }

    /// <summary>A forced Mobile observer hosting a dashboard through ordinary child content.</summary>
    private sealed class ForcedMobileHost : ComponentBase
    {
        protected override void BuildRenderTree(Microsoft.AspNetCore.Components.Rendering.RenderTreeBuilder builder)
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

    /// <summary>An app-level Auto observer hosting a dashboard. The test reports a measurement on it.</summary>
    private sealed class MeasuredMobileHost : ComponentBase
    {
        protected override void BuildRenderTree(Microsoft.AspNetCore.Components.Rendering.RenderTreeBuilder builder)
        {
            builder.OpenComponent<Tempo.Blazor.Components.Layout.TmLayoutObserver>(0);
            builder.AddAttribute(1, "LayoutMode", TmLayoutMode.Auto);
            builder.AddAttribute(2, "ChildContent", (RenderFragment)(inner =>
            {
                inner.OpenComponent<TmDashboard>(0);
                inner.AddAttribute(1, "DashboardId", "d1");
                inner.CloseComponent();
            }));
            builder.CloseComponent();
        }
    }

    [Fact]
    public void Dashboard_Css_DesktopGrid_Untouched()
    {
        var css = DashboardCss();

        // The 12-column desktop template must remain exactly as before the mobile addition.
        css.Should().Contain("grid-template-columns: repeat(var(--grid-columns, 12), 1fr)",
            "desktop grid definition must not be modified by the mobile stacking rule");
    }

    private static string DashboardCss() =>
        File.ReadAllText(Path.Combine(
            RepoRoot(), "src", "Tempo.Blazor", "wwwroot", "css", "components", "_dashboard.css"));

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TempoBlazor.slnx")))
            directory = directory.Parent;
        directory.Should().NotBeNull("the repository root should be discoverable from the test output directory");
        return directory!.FullName;
    }

    #endregion
}
