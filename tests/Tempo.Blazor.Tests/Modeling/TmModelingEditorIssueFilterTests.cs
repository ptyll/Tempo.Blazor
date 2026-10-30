using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.Components.Modeling;
using Tempo.Blazor.Configuration;
using Tempo.Blazor.Modeling;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Modeling;

/// <summary>
/// Fáze 11: the issue filter/sort state belongs to the <see cref="TmModelingEditor"/> (not the
/// panel instance) — the editor turns the filter bar on and the state survives every band
/// switch that unmounts the panel: a desktop dock collapse/expand and a mobile tab switch
/// (the TmTabs strip renders only the active body, M26). The test lives in its OWN file to
/// keep <c>TmModelingEditorTests.cs</c> under the 1200-line source ceiling (N270/F13).
/// </summary>
public sealed class TmModelingEditorIssueFilterTests : LocalizationTestBase
{
    public TmModelingEditorIssueFilterTests()
    {
        Services.AddTempoBlazorModeling();
        Services.TryAddEnumerable(ServiceDescriptor.Singleton<IModelingModelProvider>(new IssueModelingModelProvider()));
    }

    [Fact]
    public void Issue_filter_state_survives_dock_collapse_and_mobile_tab_switch()
    {
        // ── Desktop: the editor owns the filter state, so collapsing the dock keeps it ──────
        using var desktop = Render<TmModelingEditor>(parameters => parameters
            .Add(p => p.LayoutMode, TmLayoutMode.Desktop)
            .Add(p => p.ProviderKey, IssueModelingModelProvider.Key));
        desktop.WaitForAssertion(() =>
            desktop.Find("[data-testid='modeling-editor']").GetAttribute("data-state").Should().Be("loaded"));

        // The editor turns the bar on for its docked panel.
        desktop.Find("[data-testid='modeling-issue-filterbar']").Should().NotBeNull(
            "the editor passes ShowFilterBar=true to its issue panel");

        desktop.Find("[data-testid='modeling-issue-filter-input']").Input("approv");
        var desktopFilteredCount = 0;
        desktop.WaitForAssertion(() =>
        {
            // The display list concatenates the model's issues with the GENERATOR's issues, so
            // the exact filtered count is not stable — the contract is "filtered" plus the
            // row-count and filter text SURVIVING the dock collapse below.
            var visible = int.Parse(desktop.Find("[data-testid='modeling-issue-panel']")
                .GetAttribute("data-visible-issue-count"), System.Globalization.CultureInfo.InvariantCulture);
            var total = int.Parse(desktop.Find("[data-testid='modeling-issue-panel']")
                .GetAttribute("data-total-issue-count"), System.Globalization.CultureInfo.InvariantCulture);
            visible.Should().BeGreaterThan(0).And.BeLessThan(total,
                "the source-name search narrows the list");
            desktopFilteredCount = visible;
        });

        desktop.Find("[data-testid='modeling-issues-toggle']").Click();
        desktop.WaitForAssertion(() =>
            desktop.Find("[data-testid='modeling-issues-toggle']").GetAttribute("aria-expanded").Should().Be("false"));
        desktop.Find("[data-testid='modeling-issues-toggle']").Click();
        desktop.WaitForAssertion(() =>
        {
            desktop.Find("[data-testid='modeling-issue-filter-input']").GetAttribute("value")
                .Should().Be("approv", "the filter text lives in the EDITOR, not in the panel instance");
            desktop.Find("[data-testid='modeling-issue-panel']").GetAttribute("data-visible-issue-count")
                .Should().Be(desktopFilteredCount.ToString(System.Globalization.CultureInfo.InvariantCulture));
        });

        // ── Mobile: the tab strip unmounts the Issues body; the editor state re-seeds it ────
        using var mobile = Render<TmModelingEditor>(parameters => parameters
            .Add(p => p.LayoutMode, TmLayoutMode.Mobile)
            .Add(p => p.ProviderKey, IssueModelingModelProvider.Key));
        mobile.WaitForAssertion(() =>
            mobile.Find("[data-testid='modeling-editor']").GetAttribute("data-state").Should().Be("loaded"));

        mobile.Find("[data-testid='modeling-panel-tab-issues']").Click();
        mobile.WaitForAssertion(() =>
            mobile.Find("[data-testid='modeling-issue-filter-input']").Should().NotBeNull());
        mobile.Find("[data-testid='modeling-issue-filter-input']").Input("approv");
        var mobileFilteredCount = 0;
        mobile.WaitForAssertion(() =>
        {
            mobileFilteredCount = int.Parse(mobile.Find("[data-testid='modeling-issue-panel']")
                .GetAttribute("data-visible-issue-count"), System.Globalization.CultureInfo.InvariantCulture);
            mobileFilteredCount.Should().BeGreaterThan(0);
        });

        mobile.Find("[data-testid='modeling-panel-tab-preview']").Click();
        mobile.WaitForAssertion(() =>
            mobile.FindAll("[data-testid='modeling-issue-filter-input']").Should().BeEmpty(
                "only the active tab body mounts (M26)"));

        mobile.Find("[data-testid='modeling-panel-tab-issues']").Click();
        mobile.WaitForAssertion(() =>
        {
            mobile.Find("[data-testid='modeling-issue-filter-input']").GetAttribute("value")
                .Should().Be("approv", "the filter survives a tab switch through the editor's state");
            mobile.Find("[data-testid='modeling-issue-panel']").GetAttribute("data-visible-issue-count")
                .Should().Be(mobileFilteredCount.ToString(System.Globalization.CultureInfo.InvariantCulture));
        });
    }
}
