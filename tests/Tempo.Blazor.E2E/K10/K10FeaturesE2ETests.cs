using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E.K10;

/// <summary>
/// K10 headline-feature E2E (WASM @ 7106). Two focused, real-browser flows that mirror the K8/K9
/// harness pattern (force English via <c>tm-demo-culture</c> init script, fixed viewport, full-page
/// screenshot into the repo's <c>screenshots/</c> dir, and zero unhandled client errors):
/// <list type="number">
///   <item><b>TmChart interactive legend + tooltip</b> on <c>/charts</c>: toggling a legend button hides
///   a whole series (fewer <c>rect.tm-chart__bar</c>), updates the toggle-status text and flips
///   <c>aria-pressed</c>; hovering a bar surfaces a <c>.tm-chart__tooltip</c> with a label + value. The two
///   interactive demo charts carry <c>TestIdPrefix="k10-bar"</c>/<c>"k10-donut"</c> (additive demo tweak,
///   K9 convention) so their legend/tooltip ids don't collide.</item>
///   <item><b>TmMultiColumnComboBox multi-select</b> on <c>/multi-column-combo-box</c>: opening the
///   dropdown and picking two rows renders two removable chips (dropdown stays open in multi-select).
///   The demo's multi-select <c>&lt;section&gt;</c> carries <c>data-testid="mccb-multiselect-section"</c>
///   (additive demo tweak) so the three comboboxes on the page can be told apart.</item>
/// </list>
/// </summary>
[TestClass]
[TestCategory("WASM")]
public sealed class K10FeaturesE2ETests : WasmTestBase
{
    private readonly List<string> _clientErrors = [];

    private async Task<IPage> OpenAsync(string route, string readySelector)
    {
        var context = await CreateContextAsync();
        await context.AddInitScriptAsync("window.localStorage.setItem('tm-demo-culture','en');");
        var page = await context.NewPageAsync();
        page.PageError += (_, e) => { lock (_clientErrors) _clientErrors.Add("PAGEERROR: " + e); };
        page.Console += (_, m) =>
        {
            if (m.Type == "error" && m.Text.Contains("Unhandled exception"))
                lock (_clientErrors) _clientErrors.Add("CONSOLE: " + m.Text);
        };
        await page.SetViewportSizeAsync(1440, 1000);
        await GotoAppReadyAsync(page, $"{BaseUrl}{route}");

        var ready = page.Locator(readySelector).First;
        await ready.ScrollIntoViewIfNeededAsync();
        await ready.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 60000 });
        return page;
    }

    // (a) TmChart interactive legend hides a series + tooltip shows a label and a value.
    [TestMethod]
    public async Task TmChart_InteractiveLegend_TogglesSeries_AndTooltipShowsLabelAndValue()
    {
        var page = await OpenAsync("/charts", "[data-testid='k10-bar-tm-chart'] svg");

        // Scope strictly to the FIRST interactive chart (grouped bar) via its K9 TestIdPrefix so the
        // second interactive chart (donut, k10-donut-*) never satisfies these selectors.
        var barChart = page.Locator("[data-testid='k10-bar-tm-chart']");
        var bars = barChart.Locator("rect.tm-chart__bar");

        // _groupedBarData = 2 datasets ("2024","2025") x 4 labels (Q1-Q4) => 8 bars before any toggle.
        await Assertions.Expect(bars).ToHaveCountAsync(8, new LocatorAssertionsToHaveCountOptions { Timeout = 30000 });

        // Legend entry 0 is dataset "2024" (per-dataset legend). It is a real <button>.
        var legend0 = page.Locator("[data-testid='k10-bar-chart-legend-0']");
        await Assertions.Expect(legend0).ToHaveCountAsync(1);
        await Assertions.Expect(legend0).ToHaveAttributeAsync("aria-pressed", "true");

        await legend0.ClickAsync();

        // Series hidden => the four "2024" bars drop; only the four "2025" bars remain.
        await Assertions.Expect(bars).ToHaveCountAsync(4, new LocatorAssertionsToHaveCountOptions { Timeout = 15000 });
        await Assertions.Expect(legend0).ToHaveAttributeAsync("aria-pressed", "false");

        // The demo's toggle-status line reflects the last toggle ("Toggled 2024 -> hidden").
        var status = page.Locator("[data-testid='charts-toggle-status']");
        await Assertions.Expect(status).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 15000 });
        await Assertions.Expect(status).ToContainTextAsync("2024");
        await Assertions.Expect(status).ToContainTextAsync("hidden");

        // Hover a (remaining) bar => the in-SVG tooltip appears with a non-empty label + a numeric value.
        // Force skips actionability stability checks (bars animate in) while still dispatching mouseover.
        var firstBar = bars.First;
        await firstBar.ScrollIntoViewIfNeededAsync();
        await firstBar.HoverAsync(new LocatorHoverOptions { Force = true });

        var tooltip = barChart.Locator(".tm-chart__tooltip");
        await Assertions.Expect(tooltip).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10000 });

        var label = (await barChart.Locator(".tm-chart__tooltip-label").InnerTextAsync()).Trim();
        var value = (await barChart.Locator(".tm-chart__tooltip-value").InnerTextAsync()).Trim();
        Assert.IsFalse(string.IsNullOrWhiteSpace(label), "Tooltip should show a non-empty category label.");
        StringAssert.Matches(value, new Regex(@"\d"), $"Tooltip value should contain a number but was '{value}'.");

        await SaveScreenshotAsync(page, "charts-interactive-legend");
        AssertNoClientErrors();
    }

    // (b) TmMultiColumnComboBox multi-select: pick two rows => two removable chips.
    [TestMethod]
    public async Task TmMultiColumnComboBox_MultiSelect_PicksTwoRows_RendersTwoRemovableChips()
    {
        var page = await OpenAsync(
            "/multi-column-combo-box",
            "[data-testid='mccb-multiselect-section'] .tm-multi-column-combo-box__trigger");

        // Scope to the multi-select demo section so the page's other two comboboxes are excluded.
        var section = page.Locator("[data-testid='mccb-multiselect-section']");
        var combo = section.Locator(".tm-multi-column-combo-box");
        var trigger = combo.Locator(".tm-multi-column-combo-box__trigger");

        // Open the grid dropdown.
        await trigger.ClickAsync();
        var dropdown = combo.Locator(".tm-multi-column-combo-box__dropdown");
        await Assertions.Expect(dropdown).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 15000 });

        // _products has 6 rows.
        var rows = dropdown.Locator("tr.tm-multi-column-combo-box__tr");
        await Assertions.Expect(rows).ToHaveCountAsync(6, new LocatorAssertionsToHaveCountOptions { Timeout = 15000 });

        // Multi-select keeps the dropdown open, so both rows can be toggled in one pass.
        await rows.Nth(0).ClickAsync();
        await rows.Nth(1).ClickAsync();

        // Two selections => two removable chips in the trigger.
        var chips = combo.Locator(".tm-multi-column-combo-box__chip");
        await Assertions.Expect(chips).ToHaveCountAsync(2, new LocatorAssertionsToHaveCountOptions { Timeout = 15000 });
        await Assertions.Expect(combo.Locator(".tm-multi-column-combo-box__chip-remove"))
            .ToHaveCountAsync(2);

        // The demo's "Selected IDs" summary confirms the bound SelectedValues updated.
        await Assertions.Expect(section.GetByText("Selected IDs"))
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 15000 });

        await SaveScreenshotAsync(page, "combobox-multiselect");
        AssertNoClientErrors();
    }

    private void AssertNoClientErrors()
    {
        lock (_clientErrors)
        {
            Assert.IsTrue(_clientErrors.Count == 0,
                "Unhandled client-side errors occurred:\n" + string.Join("\n", _clientErrors));
        }
    }

    private static async Task SaveScreenshotAsync(IPage page, string fileName)
    {
        var dir = Path.Combine(FindRepoRoot().FullName, "tests", "Tempo.Blazor.E2E", "screenshots", "k10");
        Directory.CreateDirectory(dir);
        await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = Path.Combine(dir, $"{fileName}.png"),
            Type = ScreenshotType.Png,
            FullPage = true
        });
    }

    private static DirectoryInfo FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "TempoBlazor.slnx"))) return directory;
            directory = directory.Parent;
        }
        throw new InvalidOperationException("Repository root was not found.");
    }
}
