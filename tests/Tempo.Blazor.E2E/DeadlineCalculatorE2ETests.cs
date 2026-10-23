using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// E2E for TmDeadlineCalculator on the /deadline-calculator demo page (WASM demo at 7106).
/// Covers the flagship rule (15 days from Friday 2026-07-03 → Saturday → Monday 2026-07-20),
/// live recalculation with a Czech-holiday shift, the embed mode with a chained rule,
/// leap-year end-of-month clamping, step chaining in the form, and the zero-amount
/// validation edge case. Screenshots land in <c>__screenshots__/deadline-calculator/</c>.
/// </summary>
[TestClass]
public class DeadlineCalculatorE2ETests : WasmTestBase
{
    private const string DemoPage = "/deadline-calculator";

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

    // ── Flagship: 15 days, Saturday → Monday ─────────────────────────────────

    [TestMethod]
    [TestCategory("WASM")]
    public async Task Deadline_FifteenDays_SaturdayShiftsToMonday()
    {
        var handle = await OpenPageAsync();
        var page = handle.Page;

        var main = page.Locator("[data-testid='deadline-demo-main']");
        await main.ScrollIntoViewIfNeededAsync();
        var result = main.Locator("[data-testid='deadline-result-date']");
        await result.WaitForAsync(new LocatorWaitForOptions { Timeout = 60000 });

        await Assertions.Expect(result).ToHaveAttributeAsync("data-date", "2026-07-20",
            new LocatorAssertionsToHaveAttributeOptions { Timeout = 30000 });

        // The protocol explains the weekend shift step by step.
        var protocol = main.Locator("[data-testid='deadline-protocol']");
        var entries = await main.Locator("[data-testid='deadline-protocol-entry']").CountAsync();
        Assert.IsTrue(entries >= 4, $"Expected start/add/shift/final protocol entries, found {entries}.");
        await SaveScreenshotAsync(page, "fifteen-days-sat-to-monday");
        AssertNoBlazorErrors(handle);
    }

    // ── Live recalculation + Czech holiday shift ─────────────────────────────

    [TestMethod]
    [TestCategory("WASM")]
    public async Task Deadline_LiveChange_ShiftsAcrossCzechHoliday()
    {
        var handle = await OpenPageAsync();
        var page = handle.Page;

        var main = page.Locator("[data-testid='deadline-demo-main']");
        await main.ScrollIntoViewIfNeededAsync();
        await main.Locator("[data-testid='deadline-result-date']")
            .WaitForAsync(new LocatorWaitForOptions { Timeout = 60000 });

        // +1 day from Friday 2026-07-03 = Saturday 7/4 → Monday 7/6 is the Jan Hus holiday
        // → Tuesday 2026-07-07.
        await main.Locator("[data-testid='deadline-amount']").First.FillAsync("1");
        await main.Locator("[data-testid='deadline-amount']").First.PressAsync("Tab");

        await Assertions.Expect(main.Locator("[data-testid='deadline-result-date']"))
            .ToHaveAttributeAsync("data-date", "2026-07-07",
                new LocatorAssertionsToHaveAttributeOptions { Timeout = 30000 });
        var protocolText = await main.Locator("[data-testid='deadline-protocol']").InnerTextAsync();
        StringAssert.Contains(protocolText, "Jana Husa");
        await SaveScreenshotAsync(page, "holiday-shift-live");
        AssertNoBlazorErrors(handle);
    }

    // ── Embed mode with a chained rule ───────────────────────────────────────

    [TestMethod]
    [TestCategory("WASM")]
    public async Task Deadline_EmbedMode_ChainedRule()
    {
        var handle = await OpenPageAsync();
        var page = handle.Page;

        var embed = page.Locator("[data-testid='deadline-demo-embed']");
        await embed.ScrollIntoViewIfNeededAsync();
        var result = embed.Locator("[data-testid='deadline-result-date']");
        await result.WaitForAsync(new LocatorWaitForOptions { Timeout = 60000 });

        // 2026-06-30 + 1 month = 2026-07-30 (Thu), + 5 business days = 2026-08-06 (Thu).
        await Assertions.Expect(result).ToHaveAttributeAsync("data-date", "2026-08-06",
            new LocatorAssertionsToHaveAttributeOptions { Timeout = 30000 });

        // Embed mode renders no form controls.
        Assert.AreEqual(0, await embed.Locator("[data-testid='deadline-base']").CountAsync());
        Assert.AreEqual(0, await embed.Locator("[data-testid='deadline-amount']").CountAsync());
        await SaveScreenshotAsync(page, "embed-chained-rule");
        AssertNoBlazorErrors(handle);
    }

    // ── Leap year & end-of-month (edge case) ─────────────────────────────────

    [TestMethod]
    [TestCategory("WASM")]
    public async Task Deadline_LeapYear_EndOfMonthClamps()
    {
        var handle = await OpenPageAsync();
        var page = handle.Page;

        var leap = page.Locator("[data-testid='deadline-demo-leap']");
        await leap.ScrollIntoViewIfNeededAsync();
        var result = leap.Locator("[data-testid='deadline-result-date']");
        await result.WaitForAsync(new LocatorWaitForOptions { Timeout = 60000 });

        // 2028 is a leap year: 2028-01-31 + 1 month = 2028-02-29 (Tue, business day).
        await Assertions.Expect(result).ToHaveAttributeAsync("data-date", "2028-02-29",
            new LocatorAssertionsToHaveAttributeOptions { Timeout = 30000 });
        await SaveScreenshotAsync(page, "leap-year-clamp");

        // Non-leap year: 2027-01-31 + 1 month = 2027-02-28, a Sunday → Monday 2027-03-01.
        await leap.Locator("[data-testid='deadline-base']").FillAsync("2027-01-31");
        await Assertions.Expect(result).ToHaveAttributeAsync("data-date", "2027-03-01",
            new LocatorAssertionsToHaveAttributeOptions { Timeout = 30000 });
        await SaveScreenshotAsync(page, "non-leap-clamp-weekend-shift");
        AssertNoBlazorErrors(handle);
    }

    // ── Chaining + validation edge case ──────────────────────────────────────

    [TestMethod]
    [TestCategory("WASM")]
    public async Task Deadline_AddStep_AndZeroAmountValidation()
    {
        var handle = await OpenPageAsync();
        var page = handle.Page;

        var main = page.Locator("[data-testid='deadline-demo-main']");
        await main.ScrollIntoViewIfNeededAsync();
        await main.Locator("[data-testid='deadline-result-date']")
            .WaitForAsync(new LocatorWaitForOptions { Timeout = 60000 });

        // Add a second step: +1 day after the 15 days → Tuesday 2026-07-21.
        await main.Locator("[data-testid='deadline-add-step']").ClickAsync();
        await Assertions.Expect(main.Locator("[data-testid='deadline-step']"))
            .ToHaveCountAsync(2, new LocatorAssertionsToHaveCountOptions { Timeout = 15000 });
        await Assertions.Expect(main.Locator("[data-testid='deadline-result-date']"))
            .ToHaveAttributeAsync("data-date", "2026-07-21",
                new LocatorAssertionsToHaveAttributeOptions { Timeout = 30000 });
        await SaveScreenshotAsync(page, "chained-step-added");

        // Edge case: zero amount shows a validation error.
        await main.Locator("[data-testid='deadline-amount']").Nth(1).FillAsync("0");
        await main.Locator("[data-testid='deadline-amount']").Nth(1).PressAsync("Tab");
        await main.Locator("[data-testid='deadline-error']")
            .WaitForAsync(new LocatorWaitForOptions { Timeout = 15000 });
        await SaveScreenshotAsync(page, "edge-zero-amount-error");
        AssertNoBlazorErrors(handle);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static async Task SaveScreenshotAsync(IPage page, string fileName)
    {
        var dir = Path.Combine(FindRepoRoot().FullName,
            "tests", "Tempo.Blazor.E2E", "__screenshots__", "deadline-calculator");
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
