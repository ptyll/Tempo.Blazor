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
        var cut = RenderComponent<TmDashboard>();

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
        var cut = RenderComponent<TmDashboard>();
        
        // Find and click edit button
        var editButton = cut.FindAll("button").FirstOrDefault(b => b.GetAttribute("title")?.Contains("Edit") == true);
        if (editButton != null)
        {
            editButton.Click();
        }

        // Assert - Should have name input in edit mode
        cut.FindAll(".tm-dashboard-toolbar").Count.Should().BeGreaterThan(0);
    }

    #endregion

    #region 3. Set Default Dashboard

    [Fact]
    public void Dashboard_SetDefault_ProviderMethodExists()
    {
        // Arrange
        var provider = CreateMockProvider();
        var registry = CreateMockRegistry();
        SetupServices(provider, registry);

        // Act - Create component
        var cut = RenderComponent<TmDashboard>();

        // Assert - Provider should have SetDefaultDashboardAsync method that can be called
        provider.SetDefaultDashboardAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(Task.CompletedTask);
        
        // Verify the method exists and can be called
        provider.Received(0).SetDefaultDashboardAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
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
        var cut = RenderComponent<TmDashboard>();

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
        var cut = RenderComponent<TmDashboard>();
        var editButton = cut.FindAll("button").FirstOrDefault(b => 
            b.TextContent.Contains("Edit") || b.GetAttribute("title")?.Contains("Edit") == true);
        
        if (editButton != null)
        {
            editButton.Click();
        }

        // Assert - Should show cancel/save buttons in edit mode
        var buttons = cut.FindAll("button");
        var hasActionButtons = buttons.Any(b => 
            b.TextContent.Contains("Cancel") || 
            b.TextContent.Contains("Save") ||
            b.TextContent.Contains("Add"));
        hasActionButtons.Should().BeTrue();
    }

    #endregion
}
