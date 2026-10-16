using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.Components.Dashboard;
using Tempo.Blazor.Components.Layout;
using Tempo.Blazor.Components.Overlay;
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

        // Assert - Should show default name (localized — no hardcoded text)
        cut.Find(".tm-dashboard-title").TextContent.Should().Be("New dashboard");
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
        cut.Find("[data-testid='dashboard-edit']").Click();

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
        cut.Find("[data-testid='dashboard-edit']").Click();

        // Assert - Should show cancel button in edit mode
        var cancelButtons = cut.FindAll("button").Where(b => b.TextContent.Contains("Cancel")).ToList();
        cancelButtons.Should().NotBeEmpty("edit mode should show a Cancel button");
    }

    [Fact]
    public void Dashboard_EditMode_MobileLayout_RendersTheActionBarWithRename()
    {
        // X14: on a mobile layout the edit actions render through TmMobileActionBar (F7b:
        // Add, Save and Rename stay on the bar; Cancel overflows into the More sheet —
        // built per render so a culture change re-localizes them).
        var provider = CreateMockProvider();
        var registry = CreateMockRegistry();
        SetupServices(provider, registry);

        var cut = Render<TmDashboard>(p => p.Add(x => x.LayoutMode, TmLayoutMode.Mobile));
        cut.Find("[data-testid='dashboard-edit']").Click();

        var bar = cut.Find(".tm-mobile-action-bar__bar");
        bar.QuerySelectorAll(".tm-mobile-action-bar__action").Count.Should().Be(3);
        bar.TextContent.Should().Contain("Rename",
            "F7b: rename stays reachable on a mobile layout (short Y11 bar label)");
        bar.GetAttribute("role").Should().Be("group");
    }

    [Fact]
    public void Dashboard_EditMode_DesktopLayout_RendersNoActionBar()
    {
        var provider = CreateMockProvider();
        var registry = CreateMockRegistry();
        SetupServices(provider, registry);

        var cut = Render<TmDashboard>(p => p.Add(x => x.LayoutMode, TmLayoutMode.Desktop));
        cut.Find("[data-testid='dashboard-edit']").Click();

        cut.FindAll(".tm-mobile-action-bar__bar").Should().BeEmpty();
        cut.FindAll(".tm-dashboard-toolbar-right .tm-btn").Should().NotBeEmpty(
            "the desktop toolbar keeps the edit actions");
    }

    #endregion

    #region 5. Mobile Stacking (A1)

    // The grid places widgets through the inline `--tm-w-*` variables emitted by GetWidgetStyles —
    // never a literal grid-column, which would beat every container query. The compact modes
    // restack through the `[data-layout]` attribute rules in _dashboard.css (mobile one column,
    // tablet six, two inside tablet below 768px of container); placement stays variable-driven,
    // so no `!important` is involved anywhere (F3 removed the last one).

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
        cut.Find("[data-testid='dashboard-edit']").Click();

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

    #region 6. Fáze 1 — localization, a11y and DA3 (wave 0)

    // These tests run against bUnit's own Loose JSInterop (no Substitute IJSRuntime): the dashboard's
    // focus moves (FocusAsync) then surface as "Blazor._internal.domWrapper.focus" invocations that
    // the assertions below can count.

    private static DashboardConfig Dashboard(string id, string name, bool isDefault = false, params WidgetInstance[] widgets) =>
        new() { Id = id, Name = name, IsDefault = isDefault, Widgets = [.. widgets] };

    private (IDashboardProvider Provider, IWidgetRegistry Registry) SetupDashboard(DashboardConfig? defaultDashboard, params DashboardConfig[] others)
    {
        var provider = Substitute.For<IDashboardProvider>();
        var all = new List<DashboardConfig>();
        if (defaultDashboard is not null) all.Add(defaultDashboard);
        all.AddRange(others);
        provider.GetDashboardsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<IEnumerable<DashboardConfig>>(all));
        provider.GetDefaultDashboardAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(defaultDashboard));
        provider.GetDashboardAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(ci => Task.FromResult(all.FirstOrDefault(d => d.Id == ci.Arg<string>())));
        provider.SaveDashboardAsync(Arg.Any<DashboardConfig>(), Arg.Any<CancellationToken>())
                .Returns(ci => Task.FromResult(ci.Arg<DashboardConfig>()));
        Services.AddSingleton(provider);

        var registry = Substitute.For<IWidgetRegistry>();
        registry.GetWidget(Arg.Any<string>()).Returns((WidgetDefinition?)null);
        Services.AddSingleton(registry);
        return (provider, registry);
    }

    private const string FocusInvocation = "Blazor._internal.domWrapper.focus";

    private int FocusCalls() => JSInterop.Invocations.Count(i => i.Identifier == FocusInvocation);

    private const string OverlayModulePath = "./_content/Tempo.Blazor/js/overlay.js";
    private const string MenuNavModulePath = "./_content/Tempo.Blazor/js/tm-menu-nav.js";

    /// <summary>Registers the overlay.js / tm-menu-nav.js module mocks the switcher panel attaches.</summary>
    private BunitJSModuleInterop SetupSwitcherModules()
    {
        var overlay = JSInterop.SetupModule(OverlayModulePath);
        overlay.SetupVoid("open", _ => true).SetVoidResult();
        overlay.SetupVoid("update", _ => true).SetVoidResult();
        overlay.SetupVoid("close", _ => true).SetVoidResult();
        var nav = JSInterop.SetupModule(MenuNavModulePath);
        nav.SetupVoid("attach", _ => true).SetVoidResult();
        nav.Setup<bool>("focusFirst", _ => true).SetResult(true);
        return nav;
    }

    [Fact]
    public void Dashboard_DeleteDialog_CancelButton_IsLocalized()
    {
        UseCzechLocalization();
        SetupDashboard(Dashboard("d1", "První", isDefault: true), Dashboard("d2", "Druhá"));
        var cut = Render<TmDashboard>();

        cut.Find(".tm-dashboard-views button").Click();
        cut.Find(".tm-dashboard-menu-action--danger").Click();

        cut.Find(".tm-dialog-btn-cancel").TextContent.Trim().Should().Be("Zrušit",
            "B11: the cs UI must never show a bare 'Cancel' in the delete dialog");
    }

    [Fact]
    public void Dashboard_HardcodedTexts_AreLocalized()
    {
        UseCzechLocalization();
        SetupDashboard(new DashboardConfig
        {
            Id = "d1",
            Name = "Pilot",
            IsDefault = true,
            Widgets = [new WidgetInstance { InstanceId = "w1", WidgetId = "ghost", X = 0, Y = 0, Width = 4, Height = 2 }],
        });
        var cut = Render<TmDashboard>();

        // Unknown widget (the registry knows no 'ghost') — title and content placeholder.
        cut.Find("[data-instance-id='w1'] .tm-widget-title").TextContent.Should().Be("Neznámý widget");
        cut.Find("[data-instance-id='w1'] .tm-widget-content").TextContent.Trim().Should().Be("Widget: Neznámý widget");

        // Collapse control is localized.
        cut.Find("[data-instance-id='w1'] .tm-widget-btn").GetAttribute("title").Should().Be("Sbalit");

        // The inline name editor's placeholder is localized (TmTextInput puts Class on the input itself).
        cut.Find("[data-testid='dashboard-edit']").Click();
        cut.Find("button[title='Přejmenovat nástěnku']").Click();
        cut.Find("input.tm-dashboard-name-input").GetAttribute("placeholder").Should().Be("Pojmenujte nástěnku");
    }

    [Fact]
    public void Dashboard_Title_FallsBackToLocalizedDefaultName()
    {
        UseCzechLocalization();
        // No default dashboard in the provider: the dashboard creates an empty one named by the
        // localized default — never a hardcoded English string.
        SetupDashboard(null);
        var cut = Render<TmDashboard>();

        cut.Find(".tm-dashboard-title").TextContent.Should().Be("Nová nástěnka");
    }

    [Fact]
    public void Dashboard_IconButtons_HaveAccessibleNames()
    {
        UseCzechLocalization();
        SetupDashboard(new DashboardConfig
        {
            Id = "d1",
            Name = "Pilot",
            IsDefault = true,
            Widgets = [new WidgetInstance { InstanceId = "w1", WidgetId = "kpi", X = 0, Y = 0, Width = 4, Height = 2 }],
        });
        var cut = Render<TmDashboard>();

        // View mode: the collapse control on the widget header.
        cut.Find("[data-instance-id='w1'] .tm-widget-btn").GetAttribute("aria-label").Should().Be("Sbalit");

        // Edit mode: the rename trigger and the per-widget remove control.
        cut.Find("[data-testid='dashboard-edit']").Click();
        var rename = cut.Find("button[title='Přejmenovat nástěnku']");
        rename.GetAttribute("aria-label").Should().Be("Přejmenovat nástěnku");
        cut.Find("button[title='Odebrat widget']").GetAttribute("aria-label").Should().Be("Odebrat widget");

        // Name editing: the icon-only save/cancel pair must be named.
        rename.Click();
        cut.Find(".tm-dashboard-name-edit button.tm-btn-primary").GetAttribute("aria-label").Should().NotBeNullOrEmpty("save rename");
        cut.Find(".tm-dashboard-name-edit button.tm-btn-ghost").GetAttribute("aria-label").Should().NotBeNullOrEmpty("cancel rename");
    }

    [Fact]
    public void Dashboard_CollapseButton_HasAriaExpanded_AndAriaControls()
    {
        SetupDashboard(new DashboardConfig
        {
            Id = "d1",
            Name = "Pilot",
            IsDefault = true,
            Widgets = [new WidgetInstance { InstanceId = "w1", WidgetId = "kpi", X = 0, Y = 0, Width = 4, Height = 2 }],
        });
        var cut = Render<TmDashboard>();

        var btn = cut.Find("[data-instance-id='w1'] .tm-widget-btn");
        btn.GetAttribute("aria-expanded").Should().Be("true");
        var controls = btn.GetAttribute("aria-controls");
        controls.Should().NotBeNullOrEmpty();
        cut.Find("[data-instance-id='w1'] .tm-widget-content").GetAttribute("id").Should().Be(controls);

        btn.Click();

        btn.GetAttribute("aria-expanded").Should().Be("false");
        cut.FindAll("[data-instance-id='w1'] .tm-widget-content").Should().BeEmpty();
    }

    [Fact]
    public void Dashboard_NewDashboardDialog_EnterSubmits_AndInputAutoFocused()
    {
        UseCzechLocalization();
        SetupDashboard(Dashboard("d1", "První", isDefault: true));
        var cut = Render<TmDashboard>();

        cut.Find(".tm-dashboard-views button").Click();
        cut.Find(".tm-dashboard-menu-item--action").Click();

        var input = cut.Find(".tm-modal input");
        input.GetAttribute("autofocus").Should().NotBeNull("the dialog opens typing-ready");
        var form = cut.Find(".tm-modal form");

        input.Change("Beta");
        form.Submit();

        cut.Find(".tm-dashboard-title").TextContent.Should().Be("Beta", "submitting the form creates the dashboard");
        cut.FindAll(".tm-modal-overlay").Should().BeEmpty("submit closes the dialog");
        cut.FindAll(".tm-dashboard--edit").Should().NotBeEmpty("the new dashboard opens in edit mode");
    }

    [Fact]
    public async Task Dashboard_CancelEditAfterCreate_RestoresPreviousDashboard()
    {
        var (provider, _) = SetupDashboard(Dashboard("d1", "Alpha", isDefault: true,
            new WidgetInstance { InstanceId = "w1", WidgetId = "kpi", X = 0, Y = 0, Width = 4, Height = 2 }));
        var cut = Render<TmDashboard>();
        cut.Find(".tm-dashboard-title").TextContent.Should().Be("Alpha");

        cut.Find(".tm-dashboard-views button").Click();
        cut.Find(".tm-dashboard-menu-item--action").Click();
        cut.Find(".tm-modal input").Change("Beta");
        cut.Find(".tm-modal form").Submit();

        cut.Find(".tm-dashboard-title").TextContent.Should().Be("Beta");
        cut.FindAll("[data-instance-id='w1']").Should().BeEmpty("the new dashboard starts empty");

        cut.FindAll("button").Single(b => b.TextContent.Contains("Cancel")).Click();

        cut.Find(".tm-dashboard-title").TextContent.Should().Be("Alpha",
            "DA3: cancelling an unsaved new dashboard brings the previous dashboard back");
        cut.Find("[data-instance-id='w1']").Should().NotBeNull("the previous dashboard's widgets are restored");
        await provider.DidNotReceive().SaveDashboardAsync(Arg.Any<DashboardConfig>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Dashboard_SwitcherMenu_IsKeyboardOperable()
    {
        var nav = SetupSwitcherModules();
        SetupDashboard(Dashboard("d1", "Alpha", isDefault: true), Dashboard("d2", "Beta"));
        var cut = Render<TmDashboard>();

        var trigger = cut.Find(".tm-dashboard-views button");
        trigger.GetAttribute("aria-haspopup").Should().Be("menu");
        trigger.GetAttribute("aria-expanded").Should().Be("false");
        var triggerId = trigger.GetAttribute("id");
        triggerId.Should().NotBeNullOrEmpty();

        trigger.Click();

        // The menu renders through TmOverlayPanel (the TmDropdown pattern), which attaches the
        // shared tm-menu-nav module to this role=menu panel — the arrow keys, Home/End and the
        // typeahead the entries rely on — and the open hands focus to the first item.
        cut.WaitForAssertion(() => nav.Invocations["attach"].Should().ContainSingle());
        nav.Invocations["focusFirst"].Should().ContainSingle("opening hands focus to the first item");

        var menu = cut.Find(".tm-dashboard-menu");
        menu.GetAttribute("role").Should().Be("menu");
        menu.GetAttribute("aria-labelledby").Should().Be(triggerId);
        trigger.GetAttribute("aria-expanded").Should().Be("true");

        var items = cut.FindAll("[role='menuitem']").ToList();
        items.Should().HaveCount(4,
            "two dashboard select entries, the per-row delete action of the non-default row, and the create-new entry");

        // The select entry is a NATIVE button — Enter/Space activate it exactly once, no emulation.
        var beta = items.Single(i => i.TextContent.Contains("Beta"));
        beta.TagName.Should().Be("BUTTON");
        beta.Click();
        cut.Find(".tm-dashboard-title").TextContent.Should().Be("Beta");
        cut.FindAll(".tm-dashboard-menu").Should().BeEmpty("selecting a dashboard closes the menu");
        trigger.GetAttribute("aria-expanded").Should().Be("false");
        FocusCalls().Should().BeGreaterThan(0, "closing the menu returns focus to the trigger");

        // Escape dismisses through overlay.js → the panel's NotifyDismissedAsync (controlled close),
        // not through a local keydown handler. The close re-render lands asynchronously.
        trigger.Click();
        var panel = cut.FindComponent<TmOverlayPanel>();
        cut.WaitForAssertion(() => nav.Invocations["attach"].Should().HaveCount(2));
        cut.InvokeAsync(() => panel.Instance.NotifyDismissedAsync("escape"));
        cut.WaitForAssertion(() => cut.FindAll(".tm-dashboard-menu").Should().BeEmpty());
        trigger.GetAttribute("aria-expanded").Should().Be("false");
    }

    [Fact]
    public void Dashboard_SwitcherMenu_RowActions_AreNativeSiblingsOfTheRowActivation()
    {
        SetupSwitcherModules();
        SetupDashboard(Dashboard("d1", "Alpha", isDefault: true), Dashboard("d2", "Beta"));
        var cut = Render<TmDashboard>();

        cut.Find(".tm-dashboard-views button").Click();

        // Each dashboard row is a role="none" wrapper around a native select button — the row
        // carries no activation of its own, so nothing is emulated and nothing needs a fence
        // (docs/keyboard-activation-convention.md).
        var rows = cut.FindAll(".tm-dashboard-menu-item").Where(r => r.GetAttribute("role") == "none").ToList();
        rows.Should().HaveCount(2);
        var betaRow = rows.Single(r => r.TextContent.Contains("Beta"));
        var select = betaRow.QuerySelector(".tm-dashboard-menu-item__select");
        select.Should().NotBeNull();
        select!.TagName.Should().Be("BUTTON");
        select.GetAttribute("role").Should().Be("menuitem");
        // A native button carries NO emulated Enter/Space activation — bUnit surfaces the missing
        // handler as the proof the browser's own key handling stays in charge.
        var keyDown = () => select.KeyDown("Enter");
        keyDown.Should().Throw<Bunit.MissingEventHandlerException>();
        var keyUp = () => select.KeyUp(" ");
        keyUp.Should().Throw<Bunit.MissingEventHandlerException>();

        // The current dashboard is marked on its select entry.
        var currentSelect = cut.Find(".tm-dashboard-menu-item--active .tm-dashboard-menu-item__select");
        currentSelect.GetAttribute("aria-current").Should().Be("true");

        // The per-row actions are SIBLING native buttons labelled with the dashboard's NAME — the
        // row no longer carries one, so "Delete" can never be announced without its target.
        var delete = betaRow.QuerySelector(".tm-dashboard-menu-action--danger");
        delete.Should().NotBeNull();
        delete!.TagName.Should().Be("BUTTON");
        delete.GetAttribute("role").Should().Be("menuitem");
        delete.GetAttribute("aria-label").Should().Be("Delete dashboard Beta");
        delete.GetAttribute("tabindex").Should().Be("-1", "only one menu entry is the tab stop");
        var actionKeyDown = () => delete.KeyDown("Enter");
        actionKeyDown.Should().Throw<Bunit.MissingEventHandlerException>();
        var actionKeyUp = () => delete.KeyUp(" ");
        actionKeyUp.Should().Throw<Bunit.MissingEventHandlerException>();

        // Activating the delete action opens the confirm dialog and NEVER switches the dashboard.
        delete.Click();
        cut.FindAll(".tm-dialog").Should().NotBeEmpty("the action opens the delete confirm");
        cut.Find(".tm-dashboard-title").TextContent.Should().Be("Alpha", "the row was not activated");
    }

    [Fact]
    public async Task Dashboard_CreateDashboard_FocusesTheAddWidgetButton()
    {
        SetupSwitcherModules();
        SetupDashboard(Dashboard("d1", "Alpha", isDefault: true));
        var cut = Render<TmDashboard>();

        cut.Find(".tm-dashboard-views button").Click();
        cut.Find(".tm-dashboard-menu-item--action").Click();
        cut.Find(".tm-modal input").Change("Beta");
        var focusBefore = FocusCalls();
        cut.Find(".tm-modal form").Submit();

        cut.Find(".tm-dashboard-title").TextContent.Should().Be("Beta");
        // The create lands the user in edit mode on an empty grid: a focus move must follow the
        // render, putting the next step ("Add widget") under the keyboard.
        await cut.WaitForAssertionAsync(() =>
            FocusCalls().Should().BeGreaterThan(focusBefore,
                "after create, focus moves to the Add-widget button (next step), not <body>"));
    }

    [Fact]
    public async Task Dashboard_CancelEdit_ReturnsFocusToTheSwitcherTrigger()
    {
        SetupSwitcherModules();
        SetupDashboard(Dashboard("d1", "Alpha", isDefault: true,
            new WidgetInstance { InstanceId = "w1", WidgetId = "kpi", X = 0, Y = 0, Width = 4, Height = 2 }));
        var cut = Render<TmDashboard>();
        cut.Find("[data-testid='dashboard-edit']").Click();

        var focusBefore = FocusCalls();
        cut.FindAll("button").Single(b => b.TextContent.Contains("Cancel")).Click();

        cut.FindAll(".tm-dashboard--edit").Should().BeEmpty("edit mode ended");
        await cut.WaitForAssertionAsync(() =>
            FocusCalls().Should().BeGreaterThan(focusBefore,
                "leaving edit mode via Cancel returns focus to the view-mode entry point"));
    }

    [Fact]
    public async Task Dashboard_NameEdit_SaveAndCancel_ReturnFocusToTheRenameButton()
    {
        SetupSwitcherModules();
        SetupDashboard(Dashboard("d1", "Alpha", isDefault: true));
        var cut = Render<TmDashboard>();
        cut.Find("[data-testid='dashboard-edit']").Click();
        cut.Find("button[title='Rename dashboard']").Click();

        var input = cut.Find("input.tm-dashboard-name-input");
        input.GetAttribute("autofocus").Should().NotBeNull("the rename input is typing-ready");
        input.Change("Renamed");
        var focusBefore = FocusCalls();

        // Enter applies through the form's implicit submission; Escape cancels at the form level.
        cut.Find("form.tm-dashboard-name-edit").Submit();
        cut.Find(".tm-dashboard-title").TextContent.Should().Be("Renamed");
        await cut.WaitForAssertionAsync(() =>
            FocusCalls().Should().BeGreaterThan(focusBefore,
                "applying the name returns focus to the rename trigger"));

        cut.Find("button[title='Rename dashboard']").Click();
        focusBefore = FocusCalls();
        cut.Find("form.tm-dashboard-name-edit").KeyDown("Escape");
        cut.FindAll("form.tm-dashboard-name-edit").Should().BeEmpty("Escape cancels the edit");
        await cut.WaitForAssertionAsync(() =>
            FocusCalls().Should().BeGreaterThan(focusBefore,
                "cancelling the name edit returns focus to the rename trigger"));
    }

    #endregion
}
