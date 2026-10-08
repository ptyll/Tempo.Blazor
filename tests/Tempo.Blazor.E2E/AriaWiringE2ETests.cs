using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Axe-core accessibility scans for the CF18-5 ARIA wiring: TmFilterableDropdown's open
/// combobox (aria-controls + aria-activedescendant on the listbox) and TmMultiViewList's
/// list view (role=listbox/option + aria-selected) must produce no critical or serious
/// WCAG violations on their demo pages.
/// </summary>
[TestClass]
public class AriaWiringE2ETests : WasmTestBase
{
    private const string AxeCdn = "https://cdnjs.cloudflare.com/ajax/libs/axe-core/4.10.2/axe.min.js";

    private static async Task<string[]> RunAxeAsync(IPage page, string hostSelector)
    {
        await page.AddScriptTagAsync(new PageAddScriptTagOptions { Url = AxeCdn });
        return await page.EvaluateAsync<string[]>(
            """
            async (hostSelector) => {
                const host = document.querySelector(hostSelector) || document.body;
                const result = await axe.run(host, {
                    runOnly: { type: 'tag', values: ['wcag2a', 'wcag2aa'] },
                    resultTypes: ['violations']
                });
                return result.violations
                    .filter(v => v.impact === 'critical' || v.impact === 'serious')
                    .map(v => `${v.impact}: ${v.id} - ${v.help} (${v.nodes.map(n => n.target.join(' ')).join('; ')})`);
            }
            """, hostSelector);
    }

    [TestMethod]
    public async Task FilterableDropdown_OpenCombobox_NoCriticalOrSeriousViolations()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        await page.GotoAsync("https://localhost:7106/overlay");
        await WaitForAppReadyAsync(page);

        // The ARIA wiring only exists once the listbox is open — open it first.
        var trigger = page.Locator(
            "[data-testid='overlay-constrain-height-dropdown'] .tm-filterable-dropdown-trigger");
        await trigger.ClickAsync();
        await page.Locator(".tm-filterable-dropdown-menu.tm-overlay-panel--open")
            .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        // Highlight an option so aria-activedescendant is populated for the scan.
        await page.Keyboard.PressAsync("ArrowDown");

        var violations = await RunAxeAsync(page, ".tm-filterable-dropdown");
        Assert.AreEqual(0, violations.Length,
            $"open TmFilterableDropdown must have no critical/serious a11y violations: {string.Join(" | ", violations)}");
    }

    [TestMethod]
    public async Task MultiViewList_ListView_NoCriticalOrSeriousViolations()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        await page.GotoAsync("https://localhost:7106/multi-view-list");
        await WaitForAppReadyAsync(page);

        // Switch to the list view — that is the view with listbox/option semantics.
        var listSwitch = page.Locator(".tm-mvl-switch-list").First;
        await listSwitch.ClickAsync();
        await page.Locator("ul.tm-mvl-list").First
            .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

        var violations = await RunAxeAsync(page, ".tm-mvl-list");
        Assert.AreEqual(0, violations.Length,
            $"TmMultiViewList list view must have no critical/serious a11y violations: {string.Join(" | ", violations)}");
    }
}
