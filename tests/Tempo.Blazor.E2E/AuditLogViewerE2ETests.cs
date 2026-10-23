using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// E2E for TmAuditLogViewer on the /audit-log demo page (WASM demo at 7106, API at 5100).
/// The main section virtualizes 100 000 synthetic events sealed with a hash chain; the edge
/// section shows a tampered chain. Covers smooth virtualized scrolling, filters, timeline
/// zoom, detail with change diff, CSV export of the filtered set (blob-content assert),
/// integrity badges, and the empty-filter edge case. Screenshots land in
/// <c>__screenshots__/audit-log/</c> for UX review.
/// </summary>
[TestClass]
public class AuditLogViewerE2ETests : WasmTestBase
{
    private const string AuditPage = "/audit-log";

    private sealed record AuditPageHandle(IPage Page, List<string> Errors);

    private async Task<AuditPageHandle> OpenAuditPageAsync()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(1600, 1000);

        var errors = new List<string>();
        page.PageError += (_, message) => errors.Add(message);
        page.Console += (_, msg) =>
        {
            if (msg.Type == "error" && msg.Text.Contains("Unhandled exception"))
            {
                errors.Add(msg.Text);
            }
        };

        await GotoAppReadyAsync(page, $"{BaseUrl}{AuditPage}");

        return new AuditPageHandle(page, errors);
    }

    private static void AssertNoBlazorErrors(AuditPageHandle handle)
        => Assert.AreEqual(0, handle.Errors.Count,
            "The page raised unhandled exceptions: " + string.Join(" | ", handle.Errors));

    private static async Task<ILocator> WaitForMainViewerAsync(IPage page)
    {
        var main = page.Locator("[data-testid='audit-log-demo-main']");
        await main.ScrollIntoViewIfNeededAsync();
        // Generating and hash-chaining 100k entries takes a moment on WASM startup.
        await main.Locator("[data-testid='audit-log-row']").First
            .WaitForAsync(new LocatorWaitForOptions { Timeout = 90000 });
        return main;
    }

    // ── Virtualization over 100k events ──────────────────────────────────────

    [TestMethod]
    [TestCategory("WASM")]
    public async Task AuditLog_100kEvents_VirtualizesAndScrolls()
    {
        var handle = await OpenAuditPageAsync();
        var page = handle.Page;
        var main = await WaitForMainViewerAsync(page);

        // The count reports the full set while only a screenful of rows is in the DOM.
        var count = await main.Locator("[data-testid='audit-log-count']").InnerTextAsync();
        StringAssert.Contains(count, "100000");
        var domRows = await main.Locator("[data-testid='audit-log-row']").CountAsync();
        Assert.IsTrue(domRows < 200, $"Virtualization should keep the DOM small; found {domRows} rows.");

        var firstBefore = await main.Locator("[data-testid='audit-log-row']").First.InnerTextAsync();
        await SaveScreenshotAsync(page, "virtualized-top");

        // Deep-scroll the list; new rows must materialize.
        var list = main.Locator(".tm-audit-log__list");
        await list.EvaluateAsync("el => { el.scrollTop = el.scrollHeight / 2; }");
        await page.WaitForTimeoutAsync(1500);

        await Assertions.Expect(main.Locator("[data-testid='audit-log-row']").First)
            .Not.ToHaveTextAsync(firstBefore, new LocatorAssertionsToHaveTextOptions { Timeout = 15000 });
        var domRowsAfter = await main.Locator("[data-testid='audit-log-row']").CountAsync();
        Assert.IsTrue(domRowsAfter < 200, $"DOM must stay small after scrolling; found {domRowsAfter} rows.");
        await SaveScreenshotAsync(page, "virtualized-middle");
        AssertNoBlazorErrors(handle);
    }

    // ── Filters + detail ─────────────────────────────────────────────────────

    [TestMethod]
    [TestCategory("WASM")]
    public async Task AuditLog_FilterAndDetailWithChangeDiff()
    {
        var handle = await OpenAuditPageAsync();
        var page = handle.Page;
        var main = await WaitForMainViewerAsync(page);

        await main.Locator("[data-testid='audit-log-filter-action']")
            .SelectOptionAsync(new SelectOptionValue { Value = "document.updated" });

        await Assertions.Expect(main.Locator("[data-testid='audit-log-count']"))
            .Not.ToContainTextAsync("100000", new LocatorAssertionsToContainTextOptions { Timeout = 30000 });
        await SaveScreenshotAsync(page, "filtered-by-action");

        // Open the first row: document.updated events carry property changes.
        await main.Locator("[data-testid='audit-log-row']").First.ClickAsync();
        var detail = main.Locator("[data-testid='audit-log-detail']");
        await detail.WaitForAsync(new LocatorWaitForOptions { Timeout = 15000 });
        var diffRows = await detail.Locator(".tm-change-diff-row").CountAsync();
        Assert.IsTrue(diffRows > 0, "The detail should render the property change diff.");
        await SaveScreenshotAsync(page, "detail-change-diff");

        // Actor filter narrows further and the query stays consistent.
        await main.Locator("[data-testid='audit-log-filter-actor']")
            .SelectOptionAsync(new SelectOptionValue { Index = 1 });
        await Assertions.Expect(main.Locator("[data-testid='audit-log-count']"))
            .Not.ToContainTextAsync("100000", new LocatorAssertionsToContainTextOptions { Timeout = 30000 });
        await SaveScreenshotAsync(page, "filtered-by-actor");
        AssertNoBlazorErrors(handle);
    }

    // ── Timeline zoom ────────────────────────────────────────────────────────

    [TestMethod]
    [TestCategory("WASM")]
    public async Task AuditLog_TimelineBucketClick_NarrowsPeriod()
    {
        var handle = await OpenAuditPageAsync();
        var page = handle.Page;
        var main = await WaitForMainViewerAsync(page);

        await main.Locator("[data-testid='audit-log-timeline-bucket']").First.ClickAsync();

        await Assertions.Expect(main.Locator("[data-testid='audit-log-count']"))
            .Not.ToContainTextAsync("100000", new LocatorAssertionsToContainTextOptions { Timeout = 30000 });
        await SaveScreenshotAsync(page, "timeline-zoomed");

        // Clear filters restores the full set.
        await main.Locator("[data-testid='audit-log-clear-filters']").ClickAsync();
        await Assertions.Expect(main.Locator("[data-testid='audit-log-count']"))
            .ToContainTextAsync("100000", new LocatorAssertionsToContainTextOptions { Timeout = 30000 });
        AssertNoBlazorErrors(handle);
    }

    // ── CSV export ───────────────────────────────────────────────────────────

    [TestMethod]
    [TestCategory("WASM")]
    public async Task AuditLog_CsvExport_DownloadsFilteredSet()
    {
        var handle = await OpenAuditPageAsync();
        var page = handle.Page;
        var main = await WaitForMainViewerAsync(page);

        await main.Locator("[data-testid='audit-log-filter-action']")
            .SelectOptionAsync(new SelectOptionValue { Value = "user.role-changed" });
        await Assertions.Expect(main.Locator("[data-testid='audit-log-count']"))
            .Not.ToContainTextAsync("100000", new LocatorAssertionsToContainTextOptions { Timeout = 30000 });

        var download = await page.RunAndWaitForDownloadAsync(
            () => main.Locator("[data-testid='audit-log-export']").ClickAsync(),
            new PageRunAndWaitForDownloadOptions { Timeout = 90000 });
        Assert.AreEqual("audit-log-demo.csv", download.SuggestedFilename);

        // Assert the exported content through the blob URL: the artifact itself is flaky
        // under the .NET runner, while the blob stays readable until the 60s revoke.
        var info = await page.EvaluateAsync<System.Text.Json.JsonElement>(
            """
            async (url) => {
                const response = await fetch(url);
                const text = await response.text();
                const lines = text.split('\n').filter(l => l.trim().length > 0);
                return { firstLine: lines[0], lineCount: lines.length, hasAction: text.includes('user.role-changed'), hasOtherAction: text.includes('document.created') };
            }
            """,
            download.Url);

        StringAssert.Contains(info.GetProperty("firstLine").GetString(), "Timestamp");
        Assert.IsTrue(info.GetProperty("lineCount").GetInt32() > 100, "The filtered export should contain the matching rows.");
        Assert.IsTrue(info.GetProperty("hasAction").GetBoolean(), "The export must contain the filtered action.");
        Assert.IsFalse(info.GetProperty("hasOtherAction").GetBoolean(), "The export must not contain other actions.");
        await SaveScreenshotAsync(page, "csv-exported");
        AssertNoBlazorErrors(handle);
    }

    // ── Integrity badges + empty state (edge cases) ──────────────────────────

    [TestMethod]
    [TestCategory("WASM")]
    public async Task AuditLog_IntegrityBadges_VerifiedAndTampered()
    {
        var handle = await OpenAuditPageAsync();
        var page = handle.Page;
        await WaitForMainViewerAsync(page);

        var sealedSection = page.Locator("[data-testid='audit-log-demo-sealed']");
        await sealedSection.ScrollIntoViewIfNeededAsync();
        var verified = sealedSection.Locator("[data-testid='audit-log-integrity']");
        await verified.WaitForAsync(new LocatorWaitForOptions { Timeout = 60000 });
        await Assertions.Expect(verified).ToHaveClassAsync(
            new System.Text.RegularExpressions.Regex("tm-audit-log__integrity--verified"),
            new LocatorAssertionsToHaveClassOptions { Timeout = 60000 });

        var tampered = page.Locator("[data-testid='audit-log-demo-tampered']");
        await tampered.ScrollIntoViewIfNeededAsync();
        var failed = tampered.Locator("[data-testid='audit-log-integrity']");
        await Assertions.Expect(failed).ToHaveClassAsync(
            new System.Text.RegularExpressions.Regex("tm-audit-log__integrity--failed"),
            new LocatorAssertionsToHaveClassOptions { Timeout = 30000 });

        await SaveScreenshotAsync(page, "integrity-badges");
        AssertNoBlazorErrors(handle);
    }

    [TestMethod]
    [TestCategory("WASM")]
    public async Task AuditLog_SearchWithoutMatches_ShowsEmptyState()
    {
        var handle = await OpenAuditPageAsync();
        var page = handle.Page;
        var main = await WaitForMainViewerAsync(page);

        await main.Locator("[data-testid='audit-log-search']").FillAsync("xyz-nothing-matches-this");
        await main.Locator("[data-testid='audit-log-search']").PressAsync("Enter");

        var empty = main.Locator("[data-testid='audit-log-empty']");
        await empty.WaitForAsync(new LocatorWaitForOptions { Timeout = 30000 });
        await SaveScreenshotAsync(page, "edge-empty-search");

        // Clearing the filters brings the events back.
        await main.Locator("[data-testid='audit-log-clear-filters']").ClickAsync();
        await main.Locator("[data-testid='audit-log-row']").First
            .WaitForAsync(new LocatorWaitForOptions { Timeout = 30000 });
        AssertNoBlazorErrors(handle);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static async Task SaveScreenshotAsync(IPage page, string fileName)
    {
        var dir = Path.Combine(FindRepoRoot().FullName,
            "tests", "Tempo.Blazor.E2E", "__screenshots__", "audit-log");
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
