using AngleSharp.Dom;
using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.Components.Dashboard;
using Tempo.Blazor.Interfaces;
using Tempo.Blazor.Models;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Dashboard;

/// <summary>
/// Fáze 5 of the dashboard redesign: the widget move/resize gesture moved from HTML5
/// drag-and-drop + mouse events onto the shared <c>pointer-drag.js</c> helper (Pointer Events —
/// mouse, touch and pen). The Razor surface must therefore emit NO html5-drag or mouse handlers
/// any more (dashboard.js owns the gesture) and must publish every widget's definition min/max
/// as <c>data-*</c> attributes so the JS resize can clamp without hardcoded values. The clamp
/// itself stays in C# (Fáze 4).
/// </summary>
public class TmDashboardPointerDragTests : LocalizationTestBase
{
    private IWidgetRegistry _registry = null!;

    private async Task<IRenderedComponent<TmDashboard>> RenderEditMode(
        TmLayoutMode mode,
        WidgetDefinition? definition = null,
        int columns = 12)
    {
        var dashboard = new DashboardConfig
        {
            Id = "d1",
            Name = "Pilot",
            IsDefault = true,
            Grid = new GridConfig { Columns = columns, RowHeight = 60, Gap = 16 },
            Widgets = [new WidgetInstance { InstanceId = "a", WidgetId = "kpi", X = 0, Y = 0, Width = 4, Height = 2 }],
        };
        var provider = Substitute.For<IDashboardProvider>();
        provider.GetDashboardsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<IEnumerable<DashboardConfig>>([dashboard]));
        provider.GetDefaultDashboardAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<DashboardConfig?>(dashboard));
        provider.GetDashboardAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<DashboardConfig?>(dashboard));
        Services.AddSingleton(provider);

        _registry = Substitute.For<IWidgetRegistry>();
        _registry.GetWidget(Arg.Any<string>()).Returns(definition);
        Services.AddSingleton(_registry);

        var cut = Render<TmDashboard>(p => p
            .Add(x => x.DashboardId, dashboard.Id)
            .Add(x => x.LayoutMode, mode));
        await cut.InvokeAsync(() => cut.Instance.EnterEditModeForTests());
        return cut;
    }

    [Fact]
    public async Task Dashboard_Widget_EmitsMinMaxDataAttributes()
    {
        var definition = new WidgetDefinition { Id = "kpi", Name = "KPI", MinWidth = 2, MaxWidth = 6, MinHeight = 2, MaxHeight = 5 };
        var cut = await RenderEditMode(TmLayoutMode.Desktop, definition);

        var widget = cut.Find("[data-instance-id='a']");
        widget.GetAttribute("data-min-w").Should().Be("2", "the JS resize clamps at the definition minimum");
        widget.GetAttribute("data-max-w").Should().Be("6");
        widget.GetAttribute("data-min-h").Should().Be("2");
        widget.GetAttribute("data-max-h").Should().Be("5");
    }

    [Fact]
    public async Task Dashboard_Widget_WithoutDefinition_EmitsGridDefaults()
    {
        var cut = await RenderEditMode(TmLayoutMode.Desktop, definition: null, columns: 12);

        var widget = cut.Find("[data-instance-id='a']");
        // No definition: one cell minimum, the grid is the width ceiling, 0 = unlimited height.
        widget.GetAttribute("data-min-w").Should().Be("1");
        widget.GetAttribute("data-max-w").Should().Be("12");
        widget.GetAttribute("data-min-h").Should().Be("1");
        widget.GetAttribute("data-max-h").Should().Be("0");
    }

    [Fact]
    public async Task Dashboard_Widget_MaxWidthAboveGrid_IsClampedToTheGrid()
    {
        var definition = new WidgetDefinition { Id = "kpi", Name = "KPI", MaxWidth = 99 };
        var cut = await RenderEditMode(TmLayoutMode.Desktop, definition, columns: 12);

        cut.Find("[data-instance-id='a']").GetAttribute("data-max-w").Should().Be("12");
    }

    [Fact]
    public async Task Dashboard_Razor_HasNoHtml5DragOrMouseHandlers()
    {
        var cut = await RenderEditMode(TmLayoutMode.Desktop);

        // The gesture belongs to pointer-drag.js now: the Razor surface must not carry the
        // legacy @ondragstart/@ondragend/@onmousedown handlers (bUnit renders them as
        // blazor:* attributes) nor the HTML5 draggable attribute on the widget header.
        cut.Markup.Should().NotContain("ondragstart");
        cut.Markup.Should().NotContain("ondragend");
        cut.Markup.Should().NotContain("onmousedown");

        var header = cut.Find("[data-instance-id='a'] .tm-widget-header");
        ((IElement)header).HasAttribute("draggable").Should().BeFalse(
            "HTML5 drag-and-drop is replaced by the pointer-drag helper");

        foreach (var handle in cut.FindAll(".tm-widget-resize-handle"))
        {
            ((IElement)handle).HasAttribute("onmousedown").Should().BeFalse();
        }
    }
}
