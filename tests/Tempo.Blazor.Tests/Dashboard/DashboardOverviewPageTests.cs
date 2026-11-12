using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Tempo.Blazor.Components.Dashboard;
using Tempo.Blazor.Demo.SharedUI.Pages;
using Tempo.Blazor.Interfaces;
using Tempo.Blazor.Services;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Dashboard;

/// <summary>
/// TG-009 (plan 92e96b25, phase 8): the <c>/dashboard-overview</c> demo page — the "post-login
/// overview" — drives <see cref="TmDashboard"/> with <c>WidgetChrome=EditOnly</c>: outside edit
/// mode the widgets render no header chrome and the <c>WidgetTemplate</c> owns each card's title
/// and "view all" link; the standard Edit button switches into the full edit mode.
/// </summary>
public class DashboardOverviewPageTests : DemoPageLocalizationTestBase
{
    public DashboardOverviewPageTests()
    {
        Services.AddSingleton<IWidgetRegistry, InMemoryWidgetRegistry>();
        Services.AddScoped<IDashboardProvider, InMemoryDashboardProvider>();
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    private IRenderedComponent<DashboardOverviewPage> RenderPage() => Render<DashboardOverviewPage>();

    [Fact]
    public void Page_UsesEditOnlyWidgetChrome()
    {
        var cut = RenderPage();

        cut.FindComponent<TmDashboard>().Instance.WidgetChrome.Should().Be(WidgetChrome.EditOnly);
    }

    [Fact]
    public void ViewMode_WidgetsRenderChromeless_WithTemplateTitlesAndLinks()
    {
        var cut = RenderPage();

        cut.FindAll(".tm-widget-header").Should().BeEmpty(
            "the overview never renders widget header chrome outside edit mode");
        var widgets = cut.FindAll(".tm-widget");
        widgets.Should().HaveCount(5, "the seeded demo dashboard carries five widgets");
        foreach (var widget in widgets)
        {
            widget.GetAttribute("role").Should().Be("region");
            widget.GetAttribute("aria-labelledby").Should().NotBeNullOrEmpty();
        }

        cut.FindAll(".tm-widget-title.tm-sr-only").Should().HaveCount(5,
            "every chromeless widget keeps its sr-only region title");
        cut.FindAll(".tm-demo-ow__title").Should().HaveCount(5,
            "the template renders its own visible title per card");
        cut.FindAll(".tm-demo-ow__link").Should().HaveCount(5,
            "and its own link per card");
    }

    [Fact]
    public void Page_LocalizedStrings_ResolveWithoutRawKeys()
    {
        var cut = RenderPage();

        cut.Markup.Should().NotContain("TmDashboardOverviewDemo_",
            "every demo string resolves through the DemoResources tables");
    }

    [Fact]
    public void EditMode_SwitchesToTheFullChrome()
    {
        var cut = RenderPage();

        cut.Find("[data-testid='dashboard-edit']").Click();

        cut.FindAll(".tm-widget-header").Should().HaveCount(5,
            "edit mode restores the full widget header");
        cut.Find(".tm-widget-drag-handle").Should().NotBeNull();
        cut.FindAll(".tm-widget-btn--danger").Should().HaveCount(5);
    }

    // ── CSS contract ──────────────────────────────────────────────

    private static string PageCss() =>
        File.ReadAllText(Path.Combine(
            RepoRoot(), "src", "Tempo.Blazor.Demo.SharedUI", "Pages", "DashboardOverviewPage.razor.css"));

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TempoBlazor.slnx")))
            directory = directory.Parent;
        directory.Should().NotBeNull("the repository root should be discoverable from the test output directory");
        return directory!.FullName;
    }

    [Fact]
    public void Css_EditMode_HidesTheTemplateTitle_DuplicateOfTheHeaderTitle()
    {
        var css = PageCss();

        // Edit mode brings the widget header (with its own visible title) back; the template title
        // would read twice and wrap narrow cards, so the page hides it for the edit state only.
        var start = css.IndexOf(".tm-dashboard--edit .tm-demo-ow__title", StringComparison.Ordinal);
        start.Should().BeGreaterThan(-1,
            "the template title hides while the dashboard renders the header title");
        var block = css[start..css.IndexOf('}', start)];
        block.Should().Contain("display: none");
    }
}
