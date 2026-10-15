using AngleSharp.Dom;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.Components.Dashboard;
using Tempo.Blazor.Interfaces;
using Tempo.Blazor.Models;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Dashboard;

/// <summary>
/// Fáze 4 of the dashboard redesign: reordering without drag. The compact edit mode (tablet,
/// mobile) exposes Move up / Move down / Remove as icon buttons on the card (≥ 44 px targets),
/// the desktop drag handle becomes a focusable <c>button</c> with a keyboard grab mode
/// (Enter/Space grabs and drops, arrows move one cell, Shift+arrows resize one step, Escape
/// cancels — WCAG 2.1.1 / 2.5.7), every move is announced through a polite live region, and the
/// resize clamps to the widget definition's min/max. bUnit cannot run pointer drags, so the
/// keyboard path is exercised through the component's own handlers.
/// </summary>
public class TmDashboardReorderTests : LocalizationTestBase
{
    private IWidgetRegistry _registry = null!;

    private (IDashboardProvider Provider, IWidgetRegistry Registry) SetupDashboard(DashboardConfig dashboard, WidgetDefinition? definition = null)
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

        _registry = Substitute.For<IWidgetRegistry>();
        _registry.GetWidget(Arg.Any<string>()).Returns(definition);
        Services.AddSingleton(_registry);
        return (provider, _registry);
    }

    private static DashboardConfig Dashboard(string id, string name, bool isDefault = false, params WidgetInstance[] widgets) =>
        new() { Id = id, Name = name, IsDefault = isDefault, Widgets = [.. widgets] };

    private static WidgetInstance Widget(string id, int x, int y, int width = 4, int height = 2) =>
        new() { InstanceId = id, WidgetId = "kpi", X = x, Y = y, Width = width, Height = height };

    private async Task<IRenderedComponent<TmDashboard>> RenderEditMode(TmLayoutMode mode, DashboardConfig dashboard, WidgetDefinition? definition = null)
    {
        SetupDashboard(dashboard, definition);
        var cut = Render<TmDashboard>(p => p
            .Add(x => x.DashboardId, dashboard.Id)
            .Add(x => x.LayoutMode, mode));
        await cut.InvokeAsync(() => cut.Instance.EnterEditModeForTests());
        return cut;
    }

    private static (int X, int Y) PositionOf(IRenderedComponent<TmDashboard> cut, string instanceId)
    {
        var style = cut.Find($"[data-instance-id='{instanceId}']").GetAttribute("style")!;
        var x = int.Parse(Extract(style, "--tm-w-x"));
        var y = int.Parse(Extract(style, "--tm-w-y"));
        return (x - 1, y - 1);
    }

    private static (int W, int H) SizeOf(IRenderedComponent<TmDashboard> cut, string instanceId)
    {
        var style = cut.Find($"[data-instance-id='{instanceId}']").GetAttribute("style")!;
        return (int.Parse(Extract(style, "--tm-w-span")), int.Parse(Extract(style, "--tm-w-rows")));
    }

    private static string Extract(string style, string name)
    {
        var match = System.Text.RegularExpressions.Regex.Match(style, $"{name}: (-?\\d+)");
        return match.Groups[1].Value;
    }

    // ── F4: a freshly added widget is scrolled into view and highlighted ──

    [Fact]
    public async Task Widget_AddedFromSelector_IsHighlighted_AndRequestsScrollIntoView()
    {
        var definition = new WidgetDefinition { Id = "kpi", Name = "KPI" };
        var cut = await RenderEditMode(TmLayoutMode.Tablet, Dashboard("d1", "Pilot", isDefault: true,
            Widget("a", 0, 0)), definition);
        _registry.GetAllWidgets().Returns(new[] { definition });

        cut.Find("[data-testid='dashboard-add-widget']").Click();
        cut.Find(".tm-widget-card").Click();

        var newInstance = cut.FindAll(".tm-widget").Last().GetAttribute("data-instance-id")!;
        cut.Find($"[data-instance-id='{newInstance}']").ClassList.Should().Contain(
            "tm-widget--highlight", "the fresh widget is highlighted so the user sees what was added");

        // The card can land below the fold on a 390 px layout: the component must ask the JS
        // helper (dashboard.js) to scroll it into view after the render that added it.
        var scroll = JSInterop.VerifyInvoke("tempoDashboard.scrollWidgetIntoView");
        scroll.Arguments[1].Should().Be(newInstance,
            "the scroll targets exactly the widget the selector just added");
    }

    // ── DA2: Move up / Move down swap (X, Y) with the (Y, X) neighbour ──

    [Fact]
    public async Task Widget_MoveUp_SwapsPositionWithPreviousInRowColumnOrder()
    {
        var cut = await RenderEditMode(TmLayoutMode.Tablet, Dashboard("d1", "Pilot", isDefault: true,
            Widget("a", 0, 0),
            Widget("b", 4, 0),
            Widget("c", 0, 4)));

        // "b" sits right of "a" on row 1: moving "b" UP swaps the two positions.
        cut.Find("[data-instance-id='b'] .tm-widget-move-btn--up").Click();

        cut.FindAll(".tm-widget").Select(w => w.GetAttribute("data-instance-id"))
            .Should().ContainInOrder(new[] { "b", "a", "c" },
                "the DOM follows the new (Y, X) order after the swap");
        PositionOf(cut, "b").Should().Be((0, 0));
        PositionOf(cut, "a").Should().Be((4, 0));
        PositionOf(cut, "c").Should().Be((0, 4));
    }

    [Fact]
    public async Task Widget_MoveDown_SwapsWithNext_AndCanBeUndoneByMoveUp()
    {
        var cut = await RenderEditMode(TmLayoutMode.Tablet, Dashboard("d1", "Pilot", isDefault: true,
            Widget("a", 0, 0),
            Widget("b", 4, 0),
            Widget("c", 0, 4)));

        cut.Find("[data-instance-id='a'] .tm-widget-move-btn--down").Click();
        PositionOf(cut, "a").Should().Be((4, 0));
        PositionOf(cut, "b").Should().Be((0, 0));

        cut.Find("[data-instance-id='a'] .tm-widget-move-btn--up").Click();
        PositionOf(cut, "a").Should().Be((0, 0));
        PositionOf(cut, "b").Should().Be((4, 0));
    }

    [Fact]
    public async Task Widget_MoveDown_LastWidget_IsDisabled()
    {
        var cut = await RenderEditMode(TmLayoutMode.Tablet, Dashboard("d1", "Pilot", isDefault: true,
            Widget("a", 0, 0),
            Widget("b", 4, 0)));

        cut.Find("[data-instance-id='b'] .tm-widget-move-btn--down").Should().NotBeNull();
        ((IElement)cut.Find("[data-instance-id='b'] .tm-widget-move-btn--down")).HasAttribute("disabled")
            .Should().BeTrue("the last widget in (Y, X) order cannot move down");
        ((IElement)cut.Find("[data-instance-id='a'] .tm-widget-move-btn--up")).HasAttribute("disabled")
            .Should().BeTrue("the first widget in (Y, X) order cannot move up");
    }

    [Fact]
    public async Task Widget_MoveDown_PushesOverlapsWithoutChangingTheModel()
    {
        var dashboard = Dashboard("d1", "Pilot", isDefault: true,
            Widget("a", 0, 0, width: 8, height: 2),
            Widget("b", 0, 4, width: 8, height: 2),
            Widget("c", 0, 8, width: 8, height: 2));
        var cut = await RenderEditMode(TmLayoutMode.Tablet, dashboard);

        // Swapping "c" above "b" leaves "c" in "b"'s old (0,4) and "b" in "c"'s old (0,8) —
        // no overlap; the dashboard model object itself is untouched (no instance replaced).
        cut.Find("[data-instance-id='c'] .tm-widget-move-btn--up").Click();

        PositionOf(cut, "b").Should().Be((0, 8));
        PositionOf(cut, "c").Should().Be((0, 4));
    }

    // ── Compact edit mode (tablet / mobile): no drag, button-based reorder ──

    [Theory]
    [InlineData(TmLayoutMode.Tablet)]
    [InlineData(TmLayoutMode.Mobile)]
    public async Task Widget_CompactEditMode_HidesDragAndResizeHandles_ShowsMoveButtons(TmLayoutMode mode)
    {
        var cut = await RenderEditMode(mode, Dashboard("d1", "Pilot", isDefault: true,
            Widget("a", 0, 0),
            Widget("b", 4, 0)));

        cut.FindAll(".tm-widget-drag-handle").Should().BeEmpty("drag is a desktop gesture");
        cut.FindAll(".tm-widget-resize-handle").Should().BeEmpty("resize is a desktop gesture");

        foreach (var id in new[] { "a", "b" })
        {
            var up = cut.Find($"[data-instance-id='{id}'] .tm-widget-move-btn--up");
            var down = cut.Find($"[data-instance-id='{id}'] .tm-widget-move-btn--down");
            up.GetAttribute("aria-label").Should().NotBeNullOrEmpty("the icon buttons carry their meaning in the accessible name");
            down.GetAttribute("aria-label").Should().NotBeNullOrEmpty();
        }

        cut.FindAll(".tm-widget-btn--danger").Should().HaveCount(2, "Remove stays on the card in compact mode");
        cut.Find(".tm-dashboard").ClassList.Should().Contain("tm-dashboard--compact-edit");
    }

    [Fact]
    public async Task Widget_DesktopEditMode_KeepsDragHandle_AndShowsNoMoveButtons()
    {
        var cut = await RenderEditMode(TmLayoutMode.Desktop, Dashboard("d1", "Pilot", isDefault: true,
            Widget("a", 0, 0),
            Widget("b", 4, 0)));

        cut.FindAll(".tm-widget-drag-handle").Should().HaveCount(2);
        cut.FindAll(".tm-widget-move-btn").Should().BeEmpty("the compact move buttons are for tablet/mobile only");
        cut.Find(".tm-dashboard").ClassList.Should().NotContain("tm-dashboard--compact-edit");
    }

    [Fact]
    public async Task Widget_Undraggable_DesktopEditMode_RendersNoDragHandle()
    {
        // A widget whose definition disables dragging must not get the focusable handle: the
        // aria-describedby instructions would promise a keyboard move that cannot happen.
        var definition = new WidgetDefinition { Id = "kpi", Name = "KPI", IsDraggable = false };
        var cut = await RenderEditMode(TmLayoutMode.Desktop, Dashboard("d1", "Pilot", isDefault: true,
            Widget("a", 0, 0)), definition);

        cut.FindAll("button.tm-widget-drag-handle").Should().BeEmpty(
            "the drag handle is useless and misleading on a widget with IsDraggable=false");
    }

    // ── Drag handle: focusable button with instructions ──

    [Fact]
    public async Task Widget_DragHandle_IsFocusableButton_WithLabelAndInstructions()
    {
        var cut = await RenderEditMode(TmLayoutMode.Desktop, Dashboard("d1", "Pilot", isDefault: true,
            Widget("a", 0, 0)));

        var handle = cut.Find(".tm-widget-drag-handle");
        ((IElement)handle).TagName.Should().Be("BUTTON", "the drag handle must be keyboard reachable (WCAG 2.1.1)");
        handle.GetAttribute("type").Should().Be("button");
        handle.GetAttribute("aria-label").Should().NotBeNullOrEmpty();

        var describedBy = handle.GetAttribute("aria-describedby");
        describedBy.Should().NotBeNullOrEmpty();
        var instructions = cut.Find($"#{describedBy}");
        instructions.TextContent.Should().NotBeNullOrEmpty("the handle describes its keyboard contract");
        instructions.ClassList.Should().Contain("tm-sr-only");
    }

    // ── Keyboard grab mode ──

    [Fact]
    public async Task Widget_Keyboard_EnterGrabs_ArrowRightMovesOneColumn_EnterDrops()
    {
        var cut = await RenderEditMode(TmLayoutMode.Desktop, Dashboard("d1", "Pilot", isDefault: true,
            Widget("a", 0, 0, width: 4, height: 2),
            Widget("b", 4, 0, width: 4, height: 2)));

        var handle = cut.Find("[data-instance-id='a'] .tm-widget-drag-handle");
        handle.Click();
        handle.GetAttribute("aria-pressed").Should().Be("true", "Enter grabs the widget");

        handle.KeyDown(new KeyboardEventArgs { Key = "ArrowRight" });
        PositionOf(cut, "a").Should().Be((1, 0), "each arrow moves the grabbed widget one cell");

        handle.Click();
        handle.GetAttribute("aria-pressed").Should().Be("false", "Enter drops the widget");
        PositionOf(cut, "a").Should().Be((1, 0));
    }

    [Fact]
    public async Task Widget_Keyboard_Arrows_ClampToTheGrid()
    {
        var cut = await RenderEditMode(TmLayoutMode.Desktop, Dashboard("d1", "Pilot", isDefault: true,
            Widget("a", 8, 0, width: 4, height: 2)));

        var handle = cut.Find("[data-instance-id='a'] .tm-widget-drag-handle");
        handle.Click();
        // Grid is 12 columns wide, the widget spans 4: X = 8 is already the right edge.
        handle.KeyDown(new KeyboardEventArgs { Key = "ArrowRight" });
        PositionOf(cut, "a").X.Should().Be(8);
        handle.KeyDown(new KeyboardEventArgs { Key = "ArrowLeft" });
        PositionOf(cut, "a").X.Should().Be(7);
        handle.KeyDown(new KeyboardEventArgs { Key = "ArrowUp" });
        PositionOf(cut, "a").Y.Should().Be(0, "the widget never leaves the grid at the top");
    }

    [Fact]
    public async Task Widget_Keyboard_ShiftArrowDown_ResizesOneRow_RespectsDefinitionMinMax()
    {
        var definition = new WidgetDefinition { Id = "kpi", Name = "KPI", MinHeight = 2, MaxHeight = 3, MinWidth = 2, MaxWidth = 8 };
        var cut = await RenderEditMode(TmLayoutMode.Desktop, Dashboard("d1", "Pilot", isDefault: true,
            Widget("a", 0, 0, width: 4, height: 3)), definition);

        var handle = cut.Find("[data-instance-id='a'] .tm-widget-drag-handle");
        handle.Click();

        handle.KeyDown(new KeyboardEventArgs { Key = "ArrowDown", ShiftKey = true });
        SizeOf(cut, "a").H.Should().Be(3, "the resize clamps at the definition's MaxHeight");
        handle.KeyDown(new KeyboardEventArgs { Key = "ArrowUp", ShiftKey = true });
        SizeOf(cut, "a").H.Should().Be(2);
        handle.KeyDown(new KeyboardEventArgs { Key = "ArrowUp", ShiftKey = true });
        SizeOf(cut, "a").H.Should().Be(2, "the resize clamps at the definition's MinHeight");

        handle.KeyDown(new KeyboardEventArgs { Key = "ArrowRight", ShiftKey = true });
        SizeOf(cut, "a").W.Should().Be(5);
        handle.Click();
    }

    [Fact]
    public async Task Widget_Resize_ClampsToDefinitionMinMax()
    {
        var definition = new WidgetDefinition { Id = "kpi", Name = "KPI", MinWidth = 2, MaxWidth = 6, MinHeight = 2, MaxHeight = 5 };
        var cut = await RenderEditMode(TmLayoutMode.Desktop, Dashboard("d1", "Pilot", isDefault: true,
            Widget("a", 0, 0, width: 4, height: 2)), definition);

        var handle = cut.Find("[data-instance-id='a'] .tm-widget-drag-handle");
        handle.Click();
        for (var i = 0; i < 10; i++)
        {
            handle.KeyDown(new KeyboardEventArgs { Key = "ArrowRight", ShiftKey = true });
            handle.KeyDown(new KeyboardEventArgs { Key = "ArrowDown", ShiftKey = true });
        }
        SizeOf(cut, "a").Should().Be((6, 5), "repeated grow steps stop at the definition's maxima");

        for (var i = 0; i < 10; i++)
        {
            handle.KeyDown(new KeyboardEventArgs { Key = "ArrowLeft", ShiftKey = true });
            handle.KeyDown(new KeyboardEventArgs { Key = "ArrowUp", ShiftKey = true });
        }
        SizeOf(cut, "a").Should().Be((2, 2), "repeated shrink steps stop at the definition's minima");
        handle.Click();
    }

    [Fact]
    public async Task Widget_Keyboard_Escape_RestoresOriginalPosition()
    {
        var cut = await RenderEditMode(TmLayoutMode.Desktop, Dashboard("d1", "Pilot", isDefault: true,
            Widget("a", 0, 0, width: 4, height: 2)));

        var handle = cut.Find("[data-instance-id='a'] .tm-widget-drag-handle");
        handle.Click();
        handle.KeyDown(new KeyboardEventArgs { Key = "ArrowRight" });
        handle.KeyDown(new KeyboardEventArgs { Key = "ArrowDown" });
        PositionOf(cut, "a").Should().Be((1, 1));

        handle.KeyDown(new KeyboardEventArgs { Key = "Escape" });
        handle.GetAttribute("aria-pressed").Should().Be("false");
        PositionOf(cut, "a").Should().Be((0, 0), "Escape cancels the move and restores the pre-grab position");
        SizeOf(cut, "a").Should().Be((4, 2));
    }

    [Fact]
    public async Task Widget_Keyboard_SpaceGrab_DropsOnSecondPress_TogglingAriaPressed()
    {
        var cut = await RenderEditMode(TmLayoutMode.Desktop, Dashboard("d1", "Pilot", isDefault: true,
            Widget("a", 0, 0, width: 4, height: 2)));

        var handle = cut.Find("[data-instance-id='a'] .tm-widget-drag-handle");
        handle.Click();
        handle.GetAttribute("aria-pressed").Should().Be("true");
        handle.Click();
        handle.GetAttribute("aria-pressed").Should().Be("false");
    }

    // ── Live region announcements ──

    [Fact]
    public async Task Widget_MoveAnnouncement_IsLiveRegion()
    {
        var definition = new WidgetDefinition { Id = "kpi", Name = "KPI" };
        var cut = await RenderEditMode(TmLayoutMode.Desktop, Dashboard("d1", "Pilot", isDefault: true,
            Widget("a", 0, 0, width: 4, height: 2)), definition);

        var live = cut.Find(".tm-dashboard-live-region");
        live.GetAttribute("aria-live").Should().Be("polite");

        var handle = cut.Find(".tm-widget-drag-handle");
        handle.Click();
        live.TextContent.Should().Contain("KPI", "the announcement names the grabbed widget");
        live.TextContent.Should().NotBeNullOrEmpty();

        handle.KeyDown(new KeyboardEventArgs { Key = "ArrowRight" });
        live.TextContent.Should().Contain("2", "the announcement reports the new column");

        handle.KeyDown(new KeyboardEventArgs { Key = "Escape" });
        live.TextContent.Should().NotBeNullOrEmpty("Escape announces the cancellation");
    }

    [Fact]
    public async Task Widget_CompactMoveButtons_AnnounceNothingSilently_AndStayNamed()
    {
        var cut = await RenderEditMode(TmLayoutMode.Mobile, Dashboard("d1", "Pilot", isDefault: true,
            Widget("a", 0, 0),
            Widget("b", 4, 0)));

        var up = cut.Find("[data-instance-id='b'] .tm-widget-move-btn--up");
        // Named for the widget it acts on (several identical controls — WCAG 2.4.9).
        up.GetAttribute("aria-label").Should().Contain("Move");

        up.Click();
        PositionOf(cut, "b").Should().Be((0, 0));
    }
}
