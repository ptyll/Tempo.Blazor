using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// E2E for TmLedgerGrid + TmMoneyDisplay on the /ledger-grid demo page (WASM demo at 7106).
/// Covers the movement book with footer totals, the matching workflow (balanced pair →
/// Matched, unbalanced → PartiallyMatched, unmatch back), state/currency filters, paging,
/// the money display showcase, and the empty-search edge case. Screenshots land in
/// <c>__screenshots__/ledger-grid/</c>.
/// </summary>
[TestClass]
public class LedgerGridE2ETests : WasmTestBase
{
    private const string DemoPage = "/ledger-grid";

    private sealed record DemoPageHandle(IPage Page, List<string> Errors);

    private async Task<DemoPageHandle> OpenPageAsync()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(1440, 1000);

        var errors = new List<string>();
        page.PageError += (_, message) => errors.Add(message);
        page.Console += (_, msg) =>
        {
            if (msg.Type == "error" && msg.Text.Contains("Unhandled exception"))
            {
                errors.Add(msg.Text);
            }
        };

        await GotoAppReadyAsync(page, $"{BaseUrl}{DemoPage}");

        return new DemoPageHandle(page, errors);
    }

    private static void AssertNoBlazorErrors(DemoPageHandle handle)
        => Assert.AreEqual(0, handle.Errors.Count,
            "The page raised unhandled exceptions: " + string.Join(" | ", handle.Errors));

    private static async Task<ILocator> WaitForGridAsync(IPage page)
    {
        var main = page.Locator("[data-testid='ledger-demo-main']");
        await main.ScrollIntoViewIfNeededAsync();
        await main.Locator("[data-testid='ledger-row']").First
            .WaitForAsync(new LocatorWaitForOptions { Timeout = 60000 });
        return main;
    }

    // ── Book + footer totals ─────────────────────────────────────────────────

    [TestMethod]
    [TestCategory("WASM")]
    public async Task Ledger_RendersBookWithFooterTotals()
    {
        var handle = await OpenPageAsync();
        var page = handle.Page;
        var main = await WaitForGridAsync(page);

        var rows = await main.Locator("[data-testid='ledger-row']").CountAsync();
        Assert.IsTrue(rows > 5, $"Expected a page of ledger rows, found {rows}.");

        foreach (var cell in new[] { "ledger-footer-debit", "ledger-footer-credit", "ledger-footer-balance" })
        {
            var amount = await main.Locator($"[data-testid='{cell}'] .tm-money").GetAttributeAsync("data-amount");
            Assert.IsFalse(string.IsNullOrEmpty(amount), $"{cell} must carry an invariant amount.");
        }

        // Running balances are present on every row.
        var balances = await main.Locator("[data-testid='ledger-balance'] .tm-money").CountAsync();
        Assert.AreEqual(rows, balances, "Every row should render a running balance.");
        await SaveScreenshotAsync(page, "movement-book");
        AssertNoBlazorErrors(handle);
    }

    // ── Matching workflow ────────────────────────────────────────────────────

    [TestMethod]
    [TestCategory("WASM")]
    public async Task Ledger_MatchBalancedPair_ThenUnmatch()
    {
        var handle = await OpenPageAsync();
        var page = handle.Page;
        var main = await WaitForGridAsync(page);

        // Isolate the deterministic balanced pair.
        await main.Locator("[data-testid='ledger-search']").FillAsync("INV-FIXED-1");
        await main.Locator("[data-testid='ledger-search']").PressAsync("Enter");
        await Assertions.Expect(main.Locator("[data-testid='ledger-row']"))
            .ToHaveCountAsync(2, new LocatorAssertionsToHaveCountOptions { Timeout = 15000 });

        await main.Locator("[data-testid='ledger-select']").Nth(0).CheckAsync();
        await main.Locator("[data-testid='ledger-select']").Nth(1).CheckAsync();
        await SaveScreenshotAsync(page, "matching-selected");
        await main.Locator("[data-testid='ledger-match']").ClickAsync();

        var badges = main.Locator("[data-testid='ledger-match-badge']");
        await Assertions.Expect(badges.Nth(0)).ToHaveClassAsync(
            new System.Text.RegularExpressions.Regex("tm-ledger__badge--matched"),
            new LocatorAssertionsToHaveClassOptions { Timeout = 15000 });
        await Assertions.Expect(badges.Nth(1)).ToHaveClassAsync(
            new System.Text.RegularExpressions.Regex("tm-ledger__badge--matched"),
            new LocatorAssertionsToHaveClassOptions { Timeout = 15000 });
        await SaveScreenshotAsync(page, "matching-matched");

        // Unmatch dissolves the group again.
        await main.Locator("[data-testid='ledger-select']").Nth(0).CheckAsync();
        await main.Locator("[data-testid='ledger-unmatch']").ClickAsync();
        await Assertions.Expect(badges.Nth(0)).ToHaveClassAsync(
            new System.Text.RegularExpressions.Regex("tm-ledger__badge--unmatched"),
            new LocatorAssertionsToHaveClassOptions { Timeout = 15000 });
        AssertNoBlazorErrors(handle);
    }

    [TestMethod]
    [TestCategory("WASM")]
    public async Task Ledger_MatchUnbalancedPair_BecomesPartiallyMatched()
    {
        var handle = await OpenPageAsync();
        var page = handle.Page;
        var main = await WaitForGridAsync(page);

        await main.Locator("[data-testid='ledger-search']").FillAsync("INV-FIXED-2");
        await main.Locator("[data-testid='ledger-search']").PressAsync("Enter");
        await Assertions.Expect(main.Locator("[data-testid='ledger-row']"))
            .ToHaveCountAsync(2, new LocatorAssertionsToHaveCountOptions { Timeout = 15000 });

        await main.Locator("[data-testid='ledger-select']").Nth(0).CheckAsync();
        await main.Locator("[data-testid='ledger-select']").Nth(1).CheckAsync();
        await main.Locator("[data-testid='ledger-match']").ClickAsync();

        await Assertions.Expect(main.Locator("[data-testid='ledger-match-badge']").First)
            .ToHaveClassAsync(new System.Text.RegularExpressions.Regex("tm-ledger__badge--partiallymatched"),
                new LocatorAssertionsToHaveClassOptions { Timeout = 15000 });
        await SaveScreenshotAsync(page, "edge-partial-match");
        AssertNoBlazorErrors(handle);
    }

    // ── Filters + paging ─────────────────────────────────────────────────────

    [TestMethod]
    [TestCategory("WASM")]
    public async Task Ledger_StateAndCurrencyFilters_AndPaging()
    {
        var handle = await OpenPageAsync();
        var page = handle.Page;
        var main = await WaitForGridAsync(page);

        // Match-state filter: every visible badge is Matched.
        await main.Locator("[data-testid='ledger-filter-state']")
            .SelectOptionAsync(new SelectOptionValue { Value = "Matched" });
        await Assertions.Expect(main.Locator("[data-testid='ledger-match-badge']").First)
            .ToHaveClassAsync(new System.Text.RegularExpressions.Regex("tm-ledger__badge--matched"),
                new LocatorAssertionsToHaveClassOptions { Timeout = 15000 });
        await SaveScreenshotAsync(page, "filtered-matched");

        // Reset state filter, apply currency filter.
        await main.Locator("[data-testid='ledger-filter-state']")
            .SelectOptionAsync(new SelectOptionValue { Value = "" });
        await main.Locator("[data-testid='ledger-filter-currency']")
            .SelectOptionAsync(new SelectOptionValue { Value = "EUR" });
        await Assertions.Expect(main.Locator("[data-testid='ledger-footer-debit'] .tm-money"))
            .ToHaveAttributeAsync("data-currency", "EUR",
                new LocatorAssertionsToHaveAttributeOptions { Timeout = 15000 });

        // Paging through the full book.
        await main.Locator("[data-testid='ledger-filter-currency']")
            .SelectOptionAsync(new SelectOptionValue { Value = "" });
        var infoBefore = await main.Locator("[data-testid='ledger-page-info']").InnerTextAsync();
        await main.Locator("[data-testid='ledger-next']").ClickAsync();
        await Assertions.Expect(main.Locator("[data-testid='ledger-page-info']"))
            .Not.ToHaveTextAsync(infoBefore, new LocatorAssertionsToHaveTextOptions { Timeout = 15000 });
        AssertNoBlazorErrors(handle);
    }

    // ── Money display + empty state (edge cases) ─────────────────────────────

    [TestMethod]
    [TestCategory("WASM")]
    public async Task Ledger_MoneyDisplayShowcase_AndEmptySearch()
    {
        var handle = await OpenPageAsync();
        var page = handle.Page;
        var main = await WaitForGridAsync(page);

        var money = page.Locator("[data-testid='ledger-demo-money']");
        await money.ScrollIntoViewIfNeededAsync();

        // JPY has no minor units; the invariant attribute proves the rounding.
        var jpy = money.Locator(".tm-money[data-currency='JPY']");
        await Assertions.Expect(jpy).ToHaveAttributeAsync("data-amount", "125000",
            new LocatorAssertionsToHaveAttributeOptions { Timeout = 15000 });

        // Negative EUR amount is tinted, null renders the em-dash placeholder.
        await Assertions.Expect(money.Locator(".tm-money--negative").First).ToBeVisibleAsync();
        await Assertions.Expect(money.Locator(".tm-money--empty").First).ToContainTextAsync("—");
        await SaveScreenshotAsync(page, "money-display-showcase");

        // Edge: a search with no hits shows the empty state; clearing restores rows.
        await main.ScrollIntoViewIfNeededAsync();
        await main.Locator("[data-testid='ledger-search']").FillAsync("xyz-nothing");
        await main.Locator("[data-testid='ledger-search']").PressAsync("Enter");
        await main.Locator("[data-testid='ledger-empty']")
            .WaitForAsync(new LocatorWaitForOptions { Timeout = 15000 });
        await SaveScreenshotAsync(page, "edge-empty-search");

        await main.Locator("[data-testid='ledger-search']").FillAsync("");
        await main.Locator("[data-testid='ledger-search']").PressAsync("Enter");
        await main.Locator("[data-testid='ledger-row']").First
            .WaitForAsync(new LocatorWaitForOptions { Timeout = 15000 });
        AssertNoBlazorErrors(handle);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static async Task SaveScreenshotAsync(IPage page, string fileName)
    {
        var dir = Path.Combine(FindRepoRoot().FullName,
            "tests", "Tempo.Blazor.E2E", "__screenshots__", "ledger-grid");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"{fileName}.png");
        await page.ScreenshotAsync(new PageScreenshotOptions { Path = path, FullPage = true });
    }

    private static DirectoryInfo FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "TempoBlazor.slnx")))
            {
                return directory;
            }

            directory = directory.Parent!;
        }

        throw new InvalidOperationException("Repository root was not found.");
    }
}
