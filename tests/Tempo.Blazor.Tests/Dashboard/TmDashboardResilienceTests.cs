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
/// F7b: the remaining tasks of the dashboard redesign plan — the loading skeleton (UX §5.7),
/// the provider error alert with "Try again" (UX §5.8), the mobile rename entry and the CSS
/// guards for the G-6 token, the category-tile ink mix and the narrow-card header wrap.
/// </summary>
public class TmDashboardResilienceTests : LocalizationTestBase
{
    private static DashboardConfig Dashboard(string id, string name, params WidgetInstance[] widgets) =>
        new() { Id = id, Name = name, IsDefault = true, Widgets = [.. widgets] };

    private IDashboardProvider NewProvider(params DashboardConfig[] known)
    {
        var provider = Substitute.For<IDashboardProvider>();
        var all = known.ToList();
        provider.GetDashboardsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(ci => Task.FromResult<IEnumerable<DashboardConfig>>(all));
        provider.GetDefaultDashboardAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(known.FirstOrDefault(d => d.IsDefault)));
        provider.GetDashboardAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(ci => Task.FromResult(all.FirstOrDefault(d => d.Id == ci.Arg<string>())));
        provider.SaveDashboardAsync(Arg.Any<DashboardConfig>(), Arg.Any<CancellationToken>())
                .Returns(ci => Task.FromResult(ci.Arg<DashboardConfig>()));
        Services.AddSingleton(provider);
        var registry = Substitute.For<IWidgetRegistry>();
        registry.GetWidget(Arg.Any<string>()).Returns((WidgetDefinition?)null);
        Services.AddSingleton(registry);
        return provider;
    }

    // ── Loading skeleton (UX §5.7) ──────────────────────────────────

    [Fact]
    public void Dashboard_Loading_ShowsSkeletons_AriaBusy_AndNeverTheEmptyState()
    {
        var dashboard = Dashboard("d1", "Pilot",
            new WidgetInstance { InstanceId = "w1", WidgetId = "kpi", X = 0, Y = 0, Width = 4, Height = 2 });
        var provider = NewProvider(dashboard);
        var gate = new TaskCompletionSource<DashboardConfig?>(TaskCreationOptions.RunContinuationsAsynchronously);
        provider.GetDefaultDashboardAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(gate.Task);

        var cut = Render<TmDashboard>();

        // The pending provider must show placeholders, not the (lying) empty state.
        cut.FindAll(".tm-dashboard-skeleton-card").Should().HaveCount(2);
        cut.Find(".tm-dashboard-grid-container").GetAttribute("aria-busy").Should().Be("true");
        cut.FindAll(".tm-dashboard-empty").Should().BeEmpty();
        cut.Find(".tm-dashboard-grid-container").TextContent.Should().Contain("Loading the dashboard…",
            "a sr-only status names the loading state for screen readers");
    }

    [Fact]
    public void Dashboard_Loading_Completes_ShowsWidgets_AndClearsAriaBusy()
    {
        var dashboard = Dashboard("d1", "Pilot",
            new WidgetInstance { InstanceId = "w1", WidgetId = "kpi", X = 0, Y = 0, Width = 4, Height = 2 });
        var provider = NewProvider(dashboard);
        var gate = new TaskCompletionSource<DashboardConfig?>(TaskCreationOptions.RunContinuationsAsynchronously);
        provider.GetDefaultDashboardAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(gate.Task);
        var cut = Render<TmDashboard>();

        gate.SetResult(Dashboard("d1", "Pilot",
            new WidgetInstance { InstanceId = "w1", WidgetId = "kpi", X = 0, Y = 0, Width = 4, Height = 2 }));

        cut.WaitForState(() => cut.FindAll(".tm-widget").Count == 1);
        cut.Find(".tm-dashboard-grid-container").GetAttribute("aria-busy").Should().BeNull();
        cut.FindAll(".tm-dashboard-skeleton-card").Should().BeEmpty();
        cut.FindAll(".tm-dashboard-empty").Should().BeEmpty();
    }

    [Fact]
    public void Dashboard_EmptyProvider_StillShowsTheEmptyState_AfterLoad()
    {
        // The skeletons must not swallow the genuine empty state: a provider that answers
        // "no default dashboard" has no widgets, and the CTA has to appear.
        NewProvider();
        var cut = Render<TmDashboard>();

        cut.WaitForState(() => cut.FindAll(".tm-dashboard-empty").Count == 1);
        cut.FindAll(".tm-dashboard-skeleton-card").Should().BeEmpty();
        cut.Find(".tm-dashboard-grid-container").GetAttribute("aria-busy").Should().BeNull();
    }

    // ── Error alert + Try again (UX §5.8) ───────────────────────────

    [Fact]
    public async Task Dashboard_LoadError_ShowsAlert_AndRetryRecovers()
    {
        var dashboard = Dashboard("d1", "Pilot",
            new WidgetInstance { InstanceId = "w1", WidgetId = "kpi", X = 0, Y = 0, Width = 4, Height = 2 });
        var provider = NewProvider(dashboard);
        provider.GetDefaultDashboardAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ => throw new InvalidOperationException("provider down"),
                     _ => Task.FromResult<DashboardConfig?>(dashboard));

        var cut = Render<TmDashboard>();

        var alert = cut.Find(".tm-dashboard-error");
        alert.GetAttribute("role").Should().Be("alert");
        alert.TextContent.Should().Contain("The dashboard could not be loaded.");
        alert.TextContent.Should().Contain("Try again");
        cut.FindAll(".tm-widget").Should().BeEmpty("the dashboard is empty, not crashed");

        // Retry re-runs the failed load against the (now healthy) provider.
        await cut.InvokeAsync(() => cut.Find(".tm-dashboard-error button").Click());

        cut.WaitForAssertion(() => cut.FindAll(".tm-widget").Should().ContainSingle());
        cut.FindAll(".tm-dashboard-error").Should().BeEmpty("a successful retry clears the alert");
    }

    [Fact]
    public async Task Dashboard_LoadError_RetryFailsAgain_KeepsAlert_AndSecondRetryAlsoReloadsTheList()
    {
        // The first load failed before any dashboard existed, so a retry re-enters the DEFAULT
        // path: a second provider failure must still be contained by the shared guard (no host
        // crash) and the retry must also refresh the switcher list, so a recovered provider
        // leaves neither an empty list nor a stuck alert behind.
        var dashboard = Dashboard("d1", "Pilot",
            new WidgetInstance { InstanceId = "w1", WidgetId = "kpi", X = 0, Y = 0, Width = 4, Height = 2 });
        var provider = NewProvider(dashboard);
        provider.GetDefaultDashboardAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ => throw new InvalidOperationException("provider still down"),
                     _ => throw new InvalidOperationException("provider still down"),
                     _ => Task.FromResult<DashboardConfig?>(dashboard));

        var cut = Render<TmDashboard>();
        cut.Find(".tm-dashboard-error").TextContent.Should().Contain("The dashboard could not be loaded.");

        // First retry: the provider fails AGAIN — the guard keeps the alert up, nothing throws.
        await cut.InvokeAsync(() => cut.Find(".tm-dashboard-error button").Click());
        cut.FindAll(".tm-dashboard-error").Should().ContainSingle("the second failure is contained too");

        // Second retry: the provider recovered — the alert clears and the switcher (whose list
        // the same retry sequence reloads) lists the dashboards.
        await cut.InvokeAsync(() => cut.Find(".tm-dashboard-error button").Click());

        cut.WaitForAssertion(() => cut.FindAll(".tm-dashboard-error").Should().BeEmpty());
        cut.Find(".tm-dashboard-switcher").Click();
        cut.FindAll(".tm-dashboard-menu-item__select")
           .Should().ContainSingle("the retry re-ran the switcher list load as well");
    }

    [Fact]
    public async Task Dashboard_SaveError_ShowsAlert_KeepsEditMode_AndRetrySaves()
    {
        var provider = NewProvider(Dashboard("d1", "Pilot",
            new WidgetInstance { InstanceId = "w1", WidgetId = "kpi", X = 0, Y = 0, Width = 4, Height = 2 }));
        var saveFailed = true;
        provider.SaveDashboardAsync(Arg.Any<DashboardConfig>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                if (saveFailed) throw new InvalidOperationException("save failed");
                return Task.FromResult(ci.Arg<DashboardConfig>());
            });
        var cut = Render<TmDashboard>();

        cut.Find("[data-testid='dashboard-edit']").Click();
        cut.FindAll("button").Single(b => b.TextContent.Contains("Save")).Click();

        cut.Find(".tm-dashboard-error").TextContent.Should().Contain("The changes could not be saved.");
        cut.FindAll(".tm-dashboard--edit").Should().NotBeEmpty("a failed save keeps the user in edit mode");

        // The provider recovers: "Try again" re-runs the save and leaves edit mode.
        saveFailed = false;
        await cut.InvokeAsync(() => cut.Find(".tm-dashboard-error button").Click());

        cut.WaitForAssertion(() => cut.FindAll(".tm-dashboard--edit").Should().BeEmpty());
        cut.FindAll(".tm-dashboard-error").Should().BeEmpty();
        await provider.Received(2).SaveDashboardAsync(Arg.Any<DashboardConfig>(), Arg.Any<CancellationToken>());
    }

    // ── Mobile rename (action bar entry + dialog) ───────────────────

    [Fact]
    public void Dashboard_MobileEditMode_RenameIsReachable_AndAppliesThroughTheDialog()
    {
        var provider = NewProvider(Dashboard("d1", "Pilot"));
        var cut = Render<TmDashboard>(p => p.Add(x => x.LayoutMode, TmLayoutMode.Mobile));
        cut.Find("[data-testid='dashboard-edit']").Click();

        var bar = cut.Find(".tm-mobile-action-bar__bar");
        var actions = bar.QuerySelectorAll(".tm-mobile-action-bar__action");
        // MaxVisible=3: Add, Save and Rename stay on the bar; Cancel (lowest priority)
        // overflows into the "More" sheet.
        actions.Count.Should().Be(3);
        bar.TextContent.Should().Contain("Rename dashboard",
            "F7b: rename is reachable on a mobile layout, where the desktop toolbar is hidden");

        bar.QuerySelectorAll(".tm-mobile-action-bar__action")
           .Single(a => a.TextContent.Contains("Rename dashboard")).Click();

        var input = cut.Find(".tm-dashboard-rename-modal input");
        input.GetAttribute("autofocus").Should().NotBeNull("the dialog opens typing-ready");
        input.Change("Renamed on mobile");
        cut.Find(".tm-dashboard-rename-modal form").Submit();

        cut.Find(".tm-dashboard-title").TextContent.Should().Be("Renamed on mobile");
        cut.FindAll(".tm-dashboard-rename-modal").Should().BeEmpty("submit closes the dialog");
    }

    // ── CSS guards (G-6 token, ink mix, narrow-card wrap, skeletons) ─

    private static string DashboardCss() =>
        File.ReadAllText(Path.Combine(RepoRoot(), "src", "Tempo.Blazor", "wwwroot", "css", "components", "_dashboard.css"));

    private static string TokensCss() =>
        File.ReadAllText(Path.Combine(RepoRoot(), "src", "Tempo.Blazor", "wwwroot", "css", "tokens.css"));

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TempoBlazor.slnx")))
            directory = directory.Parent;
        directory.Should().NotBeNull("the repository root should be discoverable from the test output directory");
        return directory!.FullName;
    }

    [Fact]
    public void Dashboard_Css_MinHeightUsesTheToken_NoLiteralLeft()
    {
        DashboardCss().Should().Contain("min-height: var(--tm-dashboard-min-height");
        DashboardCss().Should().NotContain("min-height: 600px",
            "G-6: the workspace floor lives in the --tm-dashboard-min-height token");
        TokensCss().Should().Contain("--tm-dashboard-min-height: 37.5rem");
    }

    [Fact]
    public void Dashboard_Css_CategoryTileInk_IsMixedTowardsTheTextColour()
    {
        var css = DashboardCss();
        css.Should().MatchRegex(
            @"\.tm-widget-icon\s*\{[^}]*color:\s*color-mix\(in srgb,\s*var\(--category-color,\s*var\(--tm-category-fallback\)\)\s*75%,\s*var\(--tm-text-primary\)\)",
            "UX §5.11: the decorative tile ink survives the 12% amber tint in light mode");
    }

    [Fact]
    public void Dashboard_Css_NarrowCard_WrapsTheMoveGroup_ThroughTheDashboardContainerQuery()
    {
        var css = DashboardCss();
        // The responsive convention reserves width literals for TmBreakpoints, so the wrap rides
        // the EXISTING tm-dashboard container and its allowed 768 constant: a narrow dashboard
        // (390px viewport, an embedded narrow host) wraps the move group onto its own row.
        css.Should().Contain("@container tm-dashboard (width < 768px)");
        css.Should().MatchRegex(
            @"@container tm-dashboard \(width < 768px\)[\s\S]*?\.tm-dashboard--compact-edit \.tm-widget-move-group\s*\{[^}]*flex-basis:\s*100%",
            "the move group takes its own header row so the title regains full width");
    }

    [Fact]
    public void Dashboard_Css_HasSkeletonCardRule()
    {
        DashboardCss().Should().Contain(".tm-dashboard-skeleton-card");
    }
}
