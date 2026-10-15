using Bunit;
using AngleSharp.Dom;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Tempo.Blazor.Components.Dashboard;
using Tempo.Blazor.Interfaces;
using Tempo.Blazor.Models;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Dashboard;

/// <summary>
/// F2 redesign of the TmDashboard chrome (header, switcher, widget card) against
/// planning/dashboard-redesign/f2-ux-spec.md: the switcher becomes a select-like trigger showing
/// the current dashboard's name, the header gains Title/Subtitle/HeaderStart/HeaderActions/ShowHeader,
/// the Edit entry turns primary, the widget card grows a category icon tile with optional
/// description and a host actions slot, and the root loses the wrapping card (Appearance=Plain).
/// </summary>
public class TmDashboardChromeTests : LocalizationTestBase
{
    private const string FocusInvocation = "Blazor._internal.domWrapper.focus";

    private static WidgetDefinition Definition(
        string id,
        string category = WidgetCategories.Analytics,
        string icon = "bar-chart-2",
        string? description = null,
        bool isCollapsible = true) =>
        new()
        {
            Id = id,
            Name = $"{id}-name",
            Category = category,
            Icon = icon,
            Description = description,
            IsCollapsible = isCollapsible,
        };

    private (IDashboardProvider Provider, IWidgetRegistry Registry) SetupDashboard(
        params WidgetDefinition[] definitions)
    {
        var dashboard = new DashboardConfig
        {
            Id = "d1",
            Name = "Aurora",
            IsDefault = true,
            Widgets =
            [
                new WidgetInstance { InstanceId = "w1", WidgetId = "kpi", X = 0, Y = 0, Width = 4, Height = 2 },
            ],
        };
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
        registry.GetWidget("kpi").Returns(Definition("kpi", description: "Current state and key metrics"));
        registry.GetCategories().Returns(new List<WidgetCategory>
        {
            new() { Id = WidgetCategories.Analytics, Name = "Analytics & KPIs", Icon = "bar-chart-2", Order = 1, Color = "#3b82f6" },
            new() { Id = CustomCategory, Name = "My Custom", Icon = "box", Order = 2, Color = CustomCategoryColor },
        });
        Services.AddSingleton(registry);
        return (provider, registry);
    }

    private const string CustomCategory = "MyCustom";
    private const string CustomCategoryColor = "#123456";

    private int FocusCalls() => JSInterop.Invocations.Count(i => i.Identifier == FocusInvocation);

    // ── Header ────────────────────────────────────────────────────

    [Fact]
    public void Dashboard_Switcher_ShowsCurrentDashboardName_WithAriaHaspopup()
    {
        SetupDashboard();
        var cut = Render<TmDashboard>();

        var trigger = cut.Find(".tm-dashboard-switcher");
        trigger.GetAttribute("aria-haspopup").Should().Be("menu");
        trigger.GetAttribute("aria-expanded").Should().Be("false");
        cut.Find(".tm-dashboard-switcher__label").TextContent.Should().Be("Aurora",
            "the trigger shows the current dashboard's name, not the word 'Dashboards'");
        trigger.GetAttribute("aria-label").Should().Be("Dashboards: Aurora",
            "G-1: the accessible name must contain the visible text (WCAG 2.5.3 Label in Name)");
        trigger.GetAttribute("title").Should().BeNull("the visible label duplicates a tooltip");
    }

    [Fact]
    public void Dashboard_Title_OverridesDashboardName()
    {
        SetupDashboard();
        var cut = Render<TmDashboard>(p => p.Add(x => x.Title, "Project Aurora"));

        var title = cut.Find(".tm-dashboard-title");
        title.TextContent.Should().Be("Project Aurora");
        title.ClassList.Should().NotContain("tm-sr-only",
            "with an explicit Title the heading is the visible page title");
    }

    [Fact]
    public void Dashboard_TitleNullWithSwitcher_TitleIsVisuallyHidden()
    {
        SetupDashboard();
        var cut = Render<TmDashboard>();

        cut.Find(".tm-dashboard-title").ClassList.Should().Contain("tm-sr-only",
            "without a Title the name is visible in the switcher, so the h2 would duplicate it");
        cut.Find(".tm-dashboard-title").TextContent.Should().Be("Aurora",
            "the heading stays in the DOM for screen readers and existing selectors");
    }

    [Fact]
    public void Dashboard_Subtitle_RendersWhenSet()
    {
        SetupDashboard();
        var cut = Render<TmDashboard>(p => p.Add(x => x.Subtitle, "Current state and key metrics"));

        cut.Find(".tm-dashboard-subtitle").TextContent.Should().Be("Current state and key metrics");

        var without = Render<TmDashboard>();
        cut.FindAll(".tm-dashboard-subtitle").Should().NotBeEmpty();
        without.FindAll(".tm-dashboard-subtitle").Should().BeEmpty("no subtitle, no placeholder");
    }

    [Fact]
    public void Dashboard_HeaderActions_RenderedBeforeEditButton_InViewMode()
    {
        SetupDashboard();
        var cut = Render<TmDashboard>(p => p.Add(
            x => x.HeaderActions,
            (RenderFragment)(builder =>
            {
                builder.OpenElement(0, "span");
                builder.AddContent(1, "host-action");
                builder.CloseElement();
            })));

        var right = cut.Find(".tm-dashboard-toolbar-right");
        right.QuerySelector(".tm-dashboard-header-actions").Should().NotBeNull();
        right.ChildNodes[0].TextContent.Should().Contain("host-action",
            "host actions sit before the Edit button in the DOM (and therefore in tab order)");
        ((IElement)right.ChildNodes[1]).OuterHtml.Should().Contain("dashboard-edit");
    }

    [Fact]
    public void Dashboard_Header_EmitsNoStrayBraceText()
    {
        // UX-review finding (F2): a misplaced Razor brace rendered a literal "}" after the
        // switcher. The toolbar must never render stray block text.
        SetupDashboard();
        var cut = Render<TmDashboard>(p => p.Add(
            x => x.HeaderActions,
            (RenderFragment)(builder =>
            {
                builder.OpenElement(0, "span");
                builder.AddContent(1, "host-action");
                builder.CloseElement();
            })));

        cut.Find(".tm-dashboard-toolbar").TextContent.Should().NotContain("}");
        cut.Find(".tm-dashboard-toolbar").TextContent.Should().NotContain("{");
    }

    [Fact]
    public void Dashboard_ShowHeaderFalse_HidesHeader()
    {
        SetupDashboard();
        var cut = Render<TmDashboard>(p => p.Add(x => x.ShowHeader, false));

        cut.FindAll(".tm-dashboard-toolbar").Should().BeEmpty();
        cut.FindAll(".tm-dashboard-grid").Should().NotBeEmpty("only the header hides, the grid stays");
    }

    [Fact]
    public void Dashboard_EditButton_IsPrimary()
    {
        SetupDashboard();
        var cut = Render<TmDashboard>();

        var edit = cut.Find("[data-testid='dashboard-edit']");
        edit.ClassList.Should().Contain("tm-btn-primary");
        edit.ClassList.Should().NotContain("tm-btn-secondary");
    }

    [Fact]
    public void Dashboard_AllowEditFalse_DoesNotRenderTheEditButton()
    {
        SetupDashboard();
        var cut = Render<TmDashboard>(p => p.Add(x => x.AllowEdit, false));

        cut.FindAll("[data-testid='dashboard-edit']").Should().BeEmpty(
            "with AllowEdit=false the entry point silently did nothing — it must not render");
    }

    [Fact]
    public void Dashboard_EmptyState_CtaFollowsAllowEdit()
    {
        var dashboard = new DashboardConfig { Id = "d1", Name = "Empty", IsDefault = true, Widgets = [] };
        var provider = Substitute.For<IDashboardProvider>();
        provider.GetDashboardsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<IEnumerable<DashboardConfig>>([dashboard]));
        provider.GetDefaultDashboardAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<DashboardConfig?>(dashboard));
        provider.GetDashboardAsync("d1", Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<DashboardConfig?>(dashboard));
        Services.AddSingleton(provider);
        Services.AddSingleton(Substitute.For<IWidgetRegistry>());

        var editable = Render<TmDashboard>();
        editable.Find(".tm-dashboard-empty .tm-btn-primary").Should().NotBeNull(
            "the empty state's Add-widgets CTA is the screen's main task");

        var readOnly = Render<TmDashboard>(p => p.Add(x => x.AllowEdit, false));
        readOnly.FindAll(".tm-dashboard-empty .tm-btn-primary").Should().BeEmpty(
            "without AllowEdit the CTA would enter nothing — it must not render (F2 UX §5.9)");
    }

    [Fact]
    public void Dashboard_Appearance_DefaultPlain_CardAddsFramedClass()
    {
        SetupDashboard();
        var plain = Render<TmDashboard>();
        plain.Find(".tm-dashboard").ClassList.Should().NotContain("tm-dashboard--framed",
            "Plain (default) is the reference: the component paints no wrapping card of its own");

        var framed = Render<TmDashboard>(p => p.Add(x => x.Appearance, DashboardAppearance.Card));
        framed.Find(".tm-dashboard").ClassList.Should().Contain("tm-dashboard--framed");
    }

    [Fact]
    public async Task Dashboard_EnterEditMode_MovesFocusToAddWidget()
    {
        SetupDashboard();
        var cut = Render<TmDashboard>();
        var focusBefore = FocusCalls();

        cut.Find("[data-testid='dashboard-edit']").Click();

        await cut.WaitForAssertionAsync(() =>
            FocusCalls().Should().BeGreaterThan(focusBefore,
                "entering edit mode removes the trigger, so focus must land on the next step, not <body>"));
    }

    // ── Widget card ───────────────────────────────────────────────

    [Fact]
    public void Widget_Header_RendersIconTileFromDefinition_WithCategoryClass()
    {
        SetupDashboard();
        var cut = Render<TmDashboard>();

        var tile = cut.Find(".tm-widget-icon");
        tile.ClassList.Should().Contain("tm-dashboard-category--analytics",
            "the tile borrows the shared category colour class");
        tile.QuerySelector("svg").Should().NotBeNull("the definition's icon renders inside the tile");
        tile.GetAttribute("aria-hidden").Should().Be("true");
    }

    [Fact]
    public void Widget_Header_UnknownWidget_FallsBackToBoxIcon_WithoutCategoryClass()
    {
        // Unknown widget: the registry knows no 'ghost' — the fallback icon renders, no colour class.
        var dashboard = new DashboardConfig
        {
            Id = "d2",
            Name = "Ghost",
            IsDefault = true,
            Widgets = [new WidgetInstance { InstanceId = "g1", WidgetId = "ghost", X = 0, Y = 0, Width = 4, Height = 2 }],
        };
        var provider = Substitute.For<IDashboardProvider>();
        provider.GetDashboardsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<IEnumerable<DashboardConfig>>([dashboard]));
        provider.GetDefaultDashboardAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<DashboardConfig?>(dashboard));
        provider.GetDashboardAsync("d2", Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<DashboardConfig?>(dashboard));
        Services.AddSingleton(provider);
        var registry = Substitute.For<IWidgetRegistry>();
        registry.GetWidget(Arg.Any<string>()).Returns((WidgetDefinition?)null);
        Services.AddSingleton(registry);

        var ghost = Render<TmDashboard>(p => p.Add(x => x.DashboardId, "d2"));
        var ghostTile = ghost.Find(".tm-widget-icon");
        ghostTile.ClassList.Should().NotContain("tm-dashboard-category--",
            "no definition means no category — the tile falls back to --tm-category-fallback");
        ghostTile.QuerySelector("svg").Should().NotBeNull("the fallback Box icon renders");
    }

    [Fact]
    public void Widget_Header_DefinitionWithDefaultIcon_FallsBackToBox()
    {
        // A definition that never set Icon carries the model default "widget", which is neither a
        // built-in icon nor registered — TmIcon alone would render its empty tm-icon-unknown span.
        // The shared resolver must hand the tile the Box glyph instead.
        var dashboard = new DashboardConfig
        {
            Id = "d3",
            Name = "DefaultIcon",
            IsDefault = true,
            Widgets = [new WidgetInstance { InstanceId = "w-default-icon", WidgetId = "kpi", X = 0, Y = 0, Width = 4, Height = 2 }],
        };
        var provider = Substitute.For<IDashboardProvider>();
        provider.GetDashboardsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<IEnumerable<DashboardConfig>>([dashboard]));
        provider.GetDefaultDashboardAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<DashboardConfig?>(dashboard));
        provider.GetDashboardAsync("d3", Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<DashboardConfig?>(dashboard));
        Services.AddSingleton(provider);
        var registry = Substitute.For<IWidgetRegistry>();
        registry.GetWidget("kpi").Returns(Definition("kpi", icon: "widget"));
        Services.AddSingleton(registry);

        var cut = Render<TmDashboard>(p => p.Add(x => x.DashboardId, "d3"));

        var tile = cut.Find(".tm-widget-icon");
        tile.QuerySelector(".tm-icon-unknown").Should().BeNull(
            "the unresolvable model default must never reach TmIcon");
        tile.QuerySelector("svg").Should().NotBeNull("the Box glyph renders instead");
    }

    [Fact]
    public void Widget_Header_IconHidden_WhenShowWidgetIconsFalse()
    {
        SetupDashboard();
        var cut = Render<TmDashboard>(p => p.Add(x => x.ShowWidgetIcons, false));

        cut.FindAll(".tm-widget-icon").Should().BeEmpty();
        cut.Find(".tm-widget-title").TextContent.Should().NotBeEmpty("the title still renders");
    }

    [Fact]
    public void Widget_Header_DescriptionShown_WhenShowWidgetDescriptions()
    {
        SetupDashboard();
        var with = Render<TmDashboard>(p => p.Add(x => x.ShowWidgetDescriptions, true));

        var description = with.Find(".tm-widget-description");
        description.TextContent.Should().Be("Current state and key metrics",
            "the description comes from the widget definition");

        var without = Render<TmDashboard>();
        without.FindAll(".tm-widget-description").Should().BeEmpty(
            "ShowWidgetDescriptions defaults to false");
    }

    [Fact]
    public void Widget_HeaderActions_RenderedBeforeCollapse_HiddenInEditMode()
    {
        SetupDashboard();
        var cut = Render<TmDashboard>(p => p.Add(
            x => x.WidgetHeaderActions,
            (RenderFragment<WidgetInstance>)(w => builder =>
            {
                builder.OpenElement(0, "span");
                builder.AddContent(1, $"More about {w.WidgetId}");
                builder.CloseElement();
            })));

        var actions = cut.Find(".tm-widget-header-actions");
        actions.QuerySelector(".tm-widget-host-actions").Should().NotBeNull();
        actions.ChildNodes[0].TextContent.Should().Contain("More about kpi",
            "host actions precede the collapse chevron in the DOM");
        ((IElement)actions.ChildNodes[1]).ClassList.Should().Contain("tm-widget-btn");

        cut.Find("[data-testid='dashboard-edit']").Click();

        cut.FindAll(".tm-widget-host-actions").Should().BeEmpty(
            "the host slot is a view-mode affordance; edit mode owns the header");
        cut.FindAll(".tm-widget-btn--danger").Should().NotBeEmpty("edit mode keeps the remove control");
    }

    [Fact]
    public void Widget_CollapseButton_DescribedByTitle()
    {
        SetupDashboard();
        var cut = Render<TmDashboard>();

        var title = cut.Find(".tm-widget-title");
        title.GetAttribute("id").Should().NotBeNullOrEmpty();
        var collapse = cut.Find(".tm-widget-btn");
        collapse.GetAttribute("aria-describedby").Should().Be(title.GetAttribute("id"),
            "N identical collapse buttons are distinguished by the widget title they act on");
    }

    [Fact]
    public void Widget_Collapse_RespectsTheDefinition()
    {
        var dashboard = new DashboardConfig
        {
            Id = "d1",
            Name = "Aurora",
            IsDefault = true,
            Widgets = [new WidgetInstance { InstanceId = "w1", WidgetId = "fixed", X = 0, Y = 0, Width = 4, Height = 2 }],
        };
        var provider = Substitute.For<IDashboardProvider>();
        provider.GetDashboardsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<IEnumerable<DashboardConfig>>([dashboard]));
        provider.GetDefaultDashboardAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<DashboardConfig?>(dashboard));
        provider.GetDashboardAsync("d1", Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<DashboardConfig?>(dashboard));
        Services.AddSingleton(provider);
        var registry = Substitute.For<IWidgetRegistry>();
        registry.GetWidget("fixed").Returns(Definition("fixed", isCollapsible: false));
        Services.AddSingleton(registry);

        var cut = Render<TmDashboard>();

        cut.FindAll(".tm-widget-btn").Should().BeEmpty(
            "a widget defined IsCollapsible=false renders no collapse chevron");
    }

    // ── Shared category helper ────────────────────────────────────

    [Fact]
    public void DashboardCategoryStyles_SharedBySelectorAndWidget()
    {
        var dashboard = new DashboardConfig
        {
            Id = "d1",
            Name = "Aurora",
            IsDefault = true,
            Widgets =
            [
                new WidgetInstance { InstanceId = "w-built-in", WidgetId = "w-a", X = 0, Y = 0, Width = 4, Height = 2 },
                new WidgetInstance { InstanceId = "w-custom", WidgetId = "w-c", X = 4, Y = 0, Width = 4, Height = 2 },
            ],
        };
        var provider = Substitute.For<IDashboardProvider>();
        provider.GetDashboardsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<IEnumerable<DashboardConfig>>([dashboard]));
        provider.GetDefaultDashboardAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<DashboardConfig?>(dashboard));
        provider.GetDashboardAsync("d1", Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<DashboardConfig?>(dashboard));
        Services.AddSingleton(provider);

        var registry = Substitute.For<IWidgetRegistry>();
        registry.GetWidget(Arg.Any<string>()).Returns((WidgetDefinition?)null);
        registry.GetWidget("w-a").Returns(Definition("w-a"));
        registry.GetWidget("w-c").Returns(Definition("w-c", category: CustomCategory));
        registry.GetCategories().Returns(new List<WidgetCategory>
        {
            new() { Id = WidgetCategories.Analytics, Name = "Analytics & KPIs", Icon = "bar-chart-2", Order = 1, Color = "#3b82f6" },
            new() { Id = CustomCategory, Name = "My Custom", Icon = "box", Order = 2, Color = CustomCategoryColor },
        });
        registry.GetAllWidgets().Returns(new List<WidgetDefinition>
        {
            Definition("w-a"),
            Definition("w-c", category: CustomCategory),
        });
        registry.GetWidgetsByCategory(Arg.Any<string>()).Returns(Enumerable.Empty<WidgetDefinition>());
        Services.AddSingleton(registry);

        var dashboardCut = Render<TmDashboard>();
        dashboardCut.Find(".tm-widget-icon.tm-dashboard-category--analytics").Should().NotBeNull(
            "the widget tile resolves a built-in category through the shared class");
        var customTile = dashboardCut.Find("[data-instance-id='w-custom'] .tm-widget-icon");
        customTile.ClassList.Should().NotContain("tm-dashboard-category--");
        (customTile.GetAttribute("style") ?? "").Should().Contain($"--category-color: {CustomCategoryColor}",
            "a host category carries its model colour inline, exactly like the selector card");

        var selector = Render<TmWidgetSelector>(p => p.Add(x => x.WidgetRegistry, registry));
        var customCard = selector.FindAll(".tm-widget-card-icon")
            .Single(i => (i.GetAttribute("style") ?? "").Contains(CustomCategoryColor));
        (customCard.GetAttribute("style") ?? "").Should().Contain($"--category-color: {CustomCategoryColor}",
            "one helper serves both the selector card and the widget tile");
    }

    // ── CSS contract ──────────────────────────────────────────────

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

    private static string SelectorBlock(string css, string selector)
    {
        var start = System.Text.RegularExpressions.Regex.Match(
            css, @"(?m)^\s*" + System.Text.RegularExpressions.Regex.Escape(selector) + @"\s*\{");
        start.Success.Should().BeTrue("CSS must declare {0} as its own selector", selector);
        var end = css.IndexOf('}', start.Index);
        return css[start.Index..end];
    }

    [Fact]
    public void Dashboard_Css_WidgetCard_UsesTheCardTokens()
    {
        var block = SelectorBlock(DashboardCss(), ".tm-widget");

        block.Should().Contain("border-radius: var(--tm-radius-card)");
        block.Should().Contain("box-shadow: var(--tm-shadow-card)");
    }

    [Fact]
    public void Dashboard_Css_RootPaintsTheWorkspace()
    {
        var block = SelectorBlock(DashboardCss(), ".tm-dashboard");

        block.Should().Contain("background: var(--tm-bg-workspace)",
            "Plain is the default: the dashboard sits on the workspace, not on a white card");
    }

    [Fact]
    public void Dashboard_Css_ToolbarAndWidgetHeader_HaveNoUnderline()
    {
        var css = DashboardCss();

        SelectorBlock(css, ".tm-dashboard-toolbar").Should().NotContain("border-bottom");
        SelectorBlock(css, ".tm-widget-header").Should().NotContain("border-bottom");
        SelectorBlock(css, ".tm-widget-header").Should().NotContain("--tm-bg-surface-secondary",
            "the widget header is transparent — no grey band");
    }

    [Fact]
    public void Dashboard_Css_EditMode_UsesSubtleHighlight_NoRootDashedFrame()
    {
        var css = DashboardCss();

        css.Should().NotMatchRegex(@"\.tm-dashboard--edit\s*\{[^}]*dashed",
            "the 2px dashed root frame is replaced by the background grid + edit badge");
        css.Should().MatchRegex(
            @"\.tm-dashboard--edit \.tm-dashboard-grid-col\s*\{[^}]*--tm-color-primary-subtle",
            "the edit grid highlights through the primary-subtle token");
        css.Should().NotContain("rgba(", "F2 removed the last rgba literal (hardcoded indigo tint)");
    }

    [Fact]
    public void Dashboard_Css_SwitcherAndTileRules_Exist()
    {
        var css = DashboardCss();

        SelectorBlock(css, ".tm-dashboard-switcher").Should().Contain("var(--tm-input-height-md)");
        SelectorBlock(css, ".tm-widget-icon").Should().Contain("color-mix(");
        SelectorBlock(css, ".tm-widget-icon").Should().Contain(
            "var(--tm-dashboard-category-tint, var(--tm-category-tint))",
            "the tile falls back to the global tint token when the component alias is not in scope");
        SelectorBlock(css, ".tm-dashboard").Should().Contain(
            "--tm-dashboard-category-tint: var(--tm-category-tint)",
            "the tint is a global token; component stylesheets carry no dark blocks");
        var darkTokens = File.ReadAllText(Path.Combine(
            RepoRoot(), "src", "Tempo.Blazor", "wwwroot", "css", "tokens-dark.css"));
        darkTokens.Should().Contain("--tm-category-tint: 20%",
            "the dark theme raises the tint for the 400-tones on the dark surface");
    }
}
