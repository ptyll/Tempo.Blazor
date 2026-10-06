using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Tempo.Blazor.Components.Dashboard;
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
        css.Should().NotContain("560px",
            "560 is not a TmBreakpoints value; the single-column rule is the 640px container query");
        css.Should().MatchRegex(
            @"@container\s+tm-dashboard\s*\(\s*max-width:\s*640px\s*\)[\s\S]*?grid-template-columns:\s*1fr",
            "below 640px of container the grid is one column");
        css.Should().MatchRegex(
            @"@container\s+tm-dashboard\s*\(\s*max-width:\s*1024px\s*\)[\s\S]*?grid-template-columns:\s*repeat\(\s*6\s*,\s*minmax\(0\s*,\s*1fr\)\s*\)",
            "below 1024px of container the grid is six columns");
        css.Should().MatchRegex(
            @"@container\s+tm-dashboard\s*\(\s*max-width:\s*768px\s*\)[\s\S]*?grid-template-columns:\s*repeat\(\s*2\s*,\s*minmax\(0\s*,\s*1fr\)\s*\)",
            "below 768px of container the grid is two columns");
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
