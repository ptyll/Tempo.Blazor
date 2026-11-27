using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Plan b75365fb Fáze 4h — the tab bars of the spreadsheet's Format Cells and Data Validation
/// dialogs render <c>aria-selected</c> as the STRINGS "true"/"false" in both positions (a bool
/// rendered the invalid empty value on the active tab and dropped the attribute on the inactive
/// ones). Exercised in a real browser on the desktop chrome (both dialogs are G2 desktop-only
/// commands), with a scoped axe scan in both themes; screenshots land in
/// <c>__screenshots__/spreadsheet/</c> for the UX review (the active tab keeps its
/// <c>--active</c> class look — no visual change).
/// </summary>
public partial class SpreadsheetE2ETests
{
    [TestMethod]
    [TestCategory("WASM")]
    public async Task Phase4h_FormatCellsDialog_Tabs_AnnounceTheStringState_InBothStates()
    {
        var page = await OpenPhase2DemoAsync(1440, 900, touch: false);
        await OpenFormatCellsAsync(page);

        var root = page.Locator(".tm-modal-overlay.tm-fcd");
        await root.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15000 });

        var tabs = root.Locator(".tm-fcd__tab");
        Assert.IsTrue(await tabs.CountAsync() >= 5, "the desktop dialog renders the full tab set");
        Assert.AreEqual("true", await tabs.Nth(0).GetAttributeAsync("aria-selected"),
            "the Number tab is active — the string \"true\", never the invalid empty value");
        for (var i = 1; i < await tabs.CountAsync(); i++)
        {
            Assert.AreEqual("false", await tabs.Nth(i).GetAttributeAsync("aria-selected"),
                $"tab {i} is inactive — the string \"false\", never an omitted attribute");
        }

        var fontTab = tabs.Filter(new() { HasTextString = "Font" }).First;
        await fontTab.ClickAsync();
        await Assertions.Expect(fontTab).ToHaveAttributeAsync("aria-selected", "true");
        await Assertions.Expect(tabs.Nth(0)).ToHaveAttributeAsync("aria-selected", "false");

        // The phase contract for these screens is aria-valid-attr-value; the full-tag scan of the
        // Font tab flags pre-existing unlabeled inputs (input[list=tm-fcd-fonts], .tm-fcd__input--sm,
        // .tm-fcd__color-input) — debt outside this phase, reported as remaining work.
        await AssertNoAxeViolationsPhase4hAsync(page, ".tm-modal-overlay.tm-fcd", "format cells dialog", rules: ["aria-valid-attr-value"]);
        await ShotPhase2Async(page, "phase4h-format-cells-tabs--desktop");

        // The modal overlay blocks the header toggle from the pointer — dispatch it in-page.
        await EnsureDemoThemeAsync(page, "dark");
        await AssertNoAxeViolationsPhase4hAsync(page, ".tm-modal-overlay.tm-fcd", "format cells dialog (dark)", rules: ["aria-valid-attr-value"]);
        await ShotPhase2Async(page, "phase4h-format-cells-tabs--dark");

        await page.CloseAsync();
    }

    [TestMethod]
    [TestCategory("WASM")]
    public async Task Phase4h_DataValidationDialog_Tabs_AnnounceTheStringState_InBothStates()
    {
        var page = await OpenPhase2DemoAsync(1440, 900, touch: false);
        await ClickDataTabButtonAsync(page, "Data validation...");

        var root = page.Locator(".tm-modal-overlay.tm-dvd");
        await root.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15000 });

        var tabs = root.Locator(".tm-dvd__tab");
        await Assertions.Expect(tabs).ToHaveCountAsync(3);
        Assert.AreEqual("true", await tabs.Nth(0).GetAttributeAsync("aria-selected"),
            "the Settings tab is active — the string \"true\"");
        Assert.AreEqual("false", await tabs.Nth(1).GetAttributeAsync("aria-selected"));
        Assert.AreEqual("false", await tabs.Nth(2).GetAttributeAsync("aria-selected"));

        await tabs.Nth(2).ClickAsync();
        await Assertions.Expect(tabs.Nth(2)).ToHaveAttributeAsync("aria-selected", "true");
        await Assertions.Expect(tabs.Nth(0)).ToHaveAttributeAsync("aria-selected", "false");
        await Assertions.Expect(tabs.Nth(1)).ToHaveAttributeAsync("aria-selected", "false");

        await AssertNoAxeViolationsPhase4hAsync(page, ".tm-modal-overlay.tm-dvd", "data validation dialog", rules: ["aria-valid-attr-value"]);
        await ShotPhase2Async(page, "phase4h-data-validation-tabs--desktop");

        await EnsureDemoThemeAsync(page, "dark");
        await AssertNoAxeViolationsPhase4hAsync(page, ".tm-modal-overlay.tm-dvd", "data validation dialog (dark)", rules: ["aria-valid-attr-value"]);
        await ShotPhase2Async(page, "phase4h-data-validation-tabs--dark");

        await page.CloseAsync();
    }

    private static async Task AssertNoAxeViolationsPhase4hAsync(IPage page, string selector, string label, string[]? rules = null)
    {
        await page.AddScriptTagAsync(new PageAddScriptTagOptions
        {
            Url = "https://cdnjs.cloudflare.com/ajax/libs/axe-core/4.10.2/axe.min.js",
        });
        var violations = await page.EvaluateAsync<string[]>(
            """
            async ({ selector, rules }) => {
                const host = document.querySelector(selector) || document.body;
                await Promise.all(document.getAnimations()
                    .filter(a => a.effect && a.effect.getComputedTiming().iterations !== Infinity)
                    .map(a => a.finished.catch(() => { })));
                await new Promise(r => requestAnimationFrame(() => requestAnimationFrame(r)));
                const runOnly = rules && rules.length
                    ? { type: 'rule', values: rules }
                    : { type: 'tag', values: ['wcag2a', 'wcag2aa'] };
                const result = await axe.run(host, { runOnly, resultTypes: ['violations'] });
                return result.violations
                    .filter(v => v.impact === 'critical' || v.impact === 'serious')
                    .map(v => `${v.impact}: ${v.id} - ${v.help} (${v.nodes.map(n => n.target.join(' ')).join('; ')})`);
            }
            """,
            new { selector, rules });
        Assert.AreEqual(0, violations.Length,
            $"axe critical/serious violations on the {label}:" + Environment.NewLine
            + string.Join(Environment.NewLine, violations));
    }
}
