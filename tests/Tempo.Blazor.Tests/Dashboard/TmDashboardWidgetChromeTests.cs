using Bunit;
using AngleSharp.Dom;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.Components.Dashboard;
using Tempo.Blazor.Interfaces;
using Tempo.Blazor.Models;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Dashboard;

/// <summary>
/// TG-009 (plan 92e96b25, phase 8): the new <see cref="WidgetChrome"/> parameter decides whether a
/// widget card renders its header (icon tile, title, collapse chevron, drag handle) outside edit
/// mode. <see cref="WidgetChrome.Always"/> (default) is today's behaviour; <see cref="WidgetChrome.EditOnly"/>
/// renders the full header only in edit mode; <see cref="WidgetChrome.Never"/> renders no decorative
/// chrome at all — edit mode keeps a minimal functional strip (drag handle / compact move group +
/// remove) so widgets stay movable. Without chrome every widget stays a region named by its title
/// through <c>aria-labelledby</c> (the title renders visually hidden).
/// </summary>
public class TmDashboardWidgetChromeTests : LocalizationTestBase
{
    private static WidgetDefinition Definition(
        string id,
        string category = WidgetCategories.Analytics,
        string? description = null,
        bool isCollapsible = true) =>
        new()
        {
            Id = id,
            Name = $"{id}-name",
            Category = category,
            Icon = "bar-chart-2",
            Description = description,
            IsCollapsible = isCollapsible,
        };

    private IRenderedComponent<TmDashboard> SetupAndRender(
        WidgetChrome chrome,
        Action<WidgetInstance>? configureWidget = null,
        Action<ComponentParameterCollectionBuilder<TmDashboard>>? extraParams = null)
    {
        var widget = new WidgetInstance { InstanceId = "w1", WidgetId = "kpi", X = 0, Y = 0, Width = 4, Height = 2 };
        configureWidget?.Invoke(widget);
        var dashboard = new DashboardConfig
        {
            Id = "d1",
            Name = "Aurora",
            IsDefault = true,
            Widgets = [widget],
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
        registry.GetWidget("kpi").Returns(Definition("kpi", description: "Key metrics"));
        registry.GetCategories().Returns(new List<WidgetCategory>
        {
            new() { Id = WidgetCategories.Analytics, Name = "Analytics & KPIs", Icon = "bar-chart-2", Order = 1, Color = "#3b82f6" },
        });
        Services.AddSingleton(registry);

        return Render<TmDashboard>(p =>
        {
            p.Add(x => x.DashboardId, "d1");
            p.Add(x => x.WidgetChrome, chrome);
            extraParams?.Invoke(p);
        });
    }

    // ── Always (default) ──────────────────────────────────────────

    [Fact]
    public void Always_RendersFullHeader_InViewAndEditMode()
    {
        var cut = SetupAndRender(WidgetChrome.Always);

        cut.Find(".tm-widget-header").Should().NotBeNull();
        cut.Find(".tm-widget-icon").Should().NotBeNull("the icon tile is part of the full header");
        cut.Find(".tm-widget-title").ClassList.Should().NotContain("tm-sr-only",
            "with chrome the title is visible");
        cut.Find(".tm-widget-btn").Should().NotBeNull("the collapse chevron renders in view mode");

        cut.Find("[data-testid='dashboard-edit']").Click();
        cut.Find(".tm-widget-drag-handle").Should().NotBeNull("edit mode keeps the drag handle");
        cut.Find(".tm-widget-btn--danger").Should().NotBeNull("edit mode keeps the remove control");
    }

    [Fact]
    public void Always_DefaultValue_KeepsThePreTg009Dom()
    {
        // The parameter is opt-in: without it nothing moves (no region role, visible title).
        var cut = SetupAndRender(WidgetChrome.Always);

        var widget = cut.Find(".tm-widget");
        widget.GetAttribute("role").Should().BeNull("chrome-ful widgets keep the pre-TG-009 a11y tree");
        widget.GetAttribute("aria-labelledby").Should().BeNull();
        widget.GetAttribute("data-widget-chrome").Should().BeNull(
            "the marker renders only for the opt-in values, so the default DOM stays byte-identical");
    }

    [Fact]
    public void EditOnly_EmitsTheChromeMarker_EditOnlyValue()
    {
        var cut = SetupAndRender(WidgetChrome.EditOnly);

        cut.Find(".tm-widget").GetAttribute("data-widget-chrome").Should().Be("editonly",
            "the marker names the opt-in chrome mode for tests and hosts");
    }

    // ── EditOnly ──────────────────────────────────────────────────

    [Fact]
    public void EditOnly_ViewMode_RendersNoHeaderChrome()
    {
        var cut = SetupAndRender(WidgetChrome.EditOnly);

        cut.FindAll(".tm-widget-header").Should().BeEmpty(
            "outside edit mode the header (icon, title, collapse, drag handle) does not render");
        cut.FindAll(".tm-widget-icon").Should().BeEmpty();
        cut.FindAll(".tm-widget-btn").Should().BeEmpty("no collapse chevron without chrome");
        cut.FindAll(".tm-widget-host-actions").Should().BeEmpty("the host header slot is chrome too");
        cut.Find(".tm-widget-content").Should().NotBeNull("the content stays");
    }

    [Fact]
    public void EditOnly_ViewMode_WidgetStaysARegion_NamedByTheTitle()
    {
        var cut = SetupAndRender(WidgetChrome.EditOnly);

        var widget = cut.Find(".tm-widget");
        widget.GetAttribute("role").Should().Be("region");
        var labelledBy = widget.GetAttribute("aria-labelledby");
        labelledBy.Should().NotBeNullOrEmpty();

        var title = cut.Find(".tm-widget-title");
        title.GetAttribute("id").Should().Be(labelledBy);
        title.TextContent.Should().Be("kpi-name");
        title.ClassList.Should().Contain("tm-sr-only",
            "without chrome the title stays in the DOM for the region name, visually hidden");
    }

    [Fact]
    public void EditOnly_EditMode_RendersTheFullHeader_Unchanged()
    {
        var cut = SetupAndRender(WidgetChrome.EditOnly);

        cut.Find("[data-testid='dashboard-edit']").Click();

        cut.Find(".tm-widget-header").Should().NotBeNull();
        cut.Find(".tm-widget-icon").Should().NotBeNull();
        cut.Find(".tm-widget-title").ClassList.Should().NotContain("tm-sr-only");
        cut.Find(".tm-widget-drag-handle").Should().NotBeNull();
        cut.Find(".tm-widget-btn--danger").Should().NotBeNull();
        cut.Find(".tm-widget").GetAttribute("role").Should().BeNull(
            "edit mode with the full header keeps the pre-TG-009 a11y tree");
    }

    [Fact]
    public void EditOnly_HostHeaderActionsSlot_NotRenderedOutsideEditMode()
    {
        var cut = SetupAndRender(
            WidgetChrome.EditOnly,
            extraParams: p => p.Add(
                x => x.WidgetHeaderActions,
                (RenderFragment<WidgetInstance>)(w => builder =>
                {
                    builder.OpenElement(0, "span");
                    builder.AddContent(1, $"More about {w.WidgetId}");
                    builder.CloseElement();
                })));

        cut.FindAll(".tm-widget-host-actions").Should().BeEmpty(
            "the slot lives in the widget header — no header, no slot; the template owns its own actions");

        cut.Find("[data-testid='dashboard-edit']").Click();

        cut.FindAll(".tm-widget-host-actions").Should().BeEmpty(
            "edit mode owns the header (the remove control) — the host slot stays a view-mode affordance");
    }

    [Fact]
    public void Always_HostHeaderActionsSlot_StillRendersInViewMode()
    {
        var cut = SetupAndRender(
            WidgetChrome.Always,
            extraParams: p => p.Add(
                x => x.WidgetHeaderActions,
                (RenderFragment<WidgetInstance>)(w => builder =>
                {
                    builder.OpenElement(0, "span");
                    builder.AddContent(1, $"More about {w.WidgetId}");
                    builder.CloseElement();
                })));

        cut.Find(".tm-widget-host-actions").TextContent.Should().Contain("More about kpi");
    }

    [Fact]
    public void EditOnly_CollapsedWidget_StillRendersContentOutsideEditMode()
    {
        // Edge case: a widget persisted in the collapsed state has no collapse control without
        // chrome — rendering an (almost) empty card would look broken, so chromeless widgets always
        // show their content.
        var cut = SetupAndRender(WidgetChrome.EditOnly, w => w.IsCollapsed = true);

        cut.Find(".tm-widget-content").Should().NotBeNull("no chromeless card may render empty");
        cut.Find(".tm-widget").ClassList.Should().NotContain("tm-widget--collapsed",
            "the collapsed sizing rule would hide the content that has no chrome to reveal it");
    }

    // ── Never ─────────────────────────────────────────────────────

    [Fact]
    public void Never_ViewMode_RendersNoChrome_AndStaysARegion()
    {
        var cut = SetupAndRender(WidgetChrome.Never);

        cut.FindAll(".tm-widget-header").Should().BeEmpty();
        var widget = cut.Find(".tm-widget");
        widget.GetAttribute("role").Should().Be("region");
        widget.GetAttribute("aria-labelledby").Should().Be(
            cut.Find(".tm-widget-title").GetAttribute("id"));
    }

    [Fact]
    public void Never_EditMode_KeepsAFunctionalStrip_WithoutDecorativeChrome()
    {
        var cut = SetupAndRender(WidgetChrome.Never);

        cut.Find("[data-testid='dashboard-edit']").Click();

        // Edit mode must keep working (move/remove) — the functional controls survive…
        cut.Find(".tm-widget-drag-handle").Should().NotBeNull();
        cut.Find(".tm-widget-btn--danger").Should().NotBeNull();
        // …without the decorative chrome.
        cut.FindAll(".tm-widget-icon").Should().BeEmpty("no category tile in the strip");
        var title = cut.Find(".tm-widget-title");
        title.ClassList.Should().Contain("tm-sr-only", "the strip names controls through the sr-only title");
        cut.FindAll(".tm-widget-description").Should().BeEmpty();
    }

    [Fact]
    public void Never_CompactEditMode_KeepsTheMoveButtons()
    {
        var cut = SetupAndRender(
            WidgetChrome.Never,
            extraParams: p => p.Add(x => x.LayoutMode, TmLayoutMode.Mobile));

        cut.Find("[data-testid='dashboard-edit']").Click();

        cut.Find(".tm-widget-move-btn--up").Should().NotBeNull("compact edit keeps the non-drag move");
        cut.Find(".tm-widget-move-btn--down").Should().NotBeNull();
        cut.Find(".tm-widget-btn--danger").Should().NotBeNull();
    }

    // ── Mode switching keeps layout and order ─────────────────────

    [Fact]
    public void EnteringAndLeavingEditMode_DoesNotChangeWidgetLayoutOrOrder()
    {
        var cut = SetupAndRender(WidgetChrome.EditOnly);
        var before = cut.Find(".tm-widget").GetAttribute("style");

        cut.Find("[data-testid='dashboard-edit']").Click();
        cut.FindAll(".tm-widget--grabbed").Should().BeEmpty();
        var during = cut.Find(".tm-widget").GetAttribute("style");

        cut.Find(".tm-dashboard-toolbar-right .tm-btn-ghost").Click(); // Cancel
        var after = cut.Find(".tm-widget").GetAttribute("style");

        during.Should().Be(before, "the grid geometry is a view-mode concern; edit chrome must not move it");
        after.Should().Be(before, "leaving edit mode restores the exact pre-edit layout");
        cut.FindAll(".tm-widget-header").Should().BeEmpty("back in view mode the chrome stays off");
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

    [Fact]
    public void Css_ChromelessContent_GetsItsOwnTopPaddingThroughAToken()
    {
        var css = DashboardCss();

        var start = css.IndexOf(".tm-widget--chromeless .tm-widget-content", StringComparison.Ordinal);
        start.Should().BeGreaterThan(-1,
            "chromeless cards have no header to supply the content's top padding");
        var block = css[start..css.IndexOf('}', start)];
        block.Should().Contain("padding-top: var(--tm-space-5)");
        block.Should().NotMatchRegex(@"#[0-9a-fA-F]{3,8}", "only --tm-* tokens, never literal colours");
    }
}
