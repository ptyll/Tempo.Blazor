using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Fáze 25b: the Scheduler and the Gantt read the circuit's <c>ITmUserTimeZone</c> when the host set
/// no explicit zone. On the Server demo (no opt-in anywhere — the 2.9.1 default) a browser context
/// reporting "Pacific/Auckland" (UTC+12/+13) must see "Today" navigate to the AUCKLAND date and the
/// Gantt's today marker render. Expectations are computed with Intl inside the page, so the tests
/// are deterministic at any hour (including the hours where Auckland's date differs from the
/// server's UTC date).
/// </summary>
[TestClass]
public class UserTimeZoneSchedulerGanttServerE2ETests : ServerTestBase
{
    private const string Auckland = "Pacific/Auckland";

    private async Task<IPage> OpenAsync(string path)
    {
        var context = await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = 1440, Height = 1100 },
            Locale = "en-US",
            TimezoneId = Auckland,
            IgnoreHTTPSErrors = true,
        });
        RegisterContext(context);
        await context.AddCookiesAsync(new[]
        {
            new Cookie { Name = "tm-demo-culture", Value = "en", Url = BaseUrl },
        });
        var page = await context.NewPageAsync();
        await page.GotoAsync($"{BaseUrl}{path}", new PageGotoOptions { WaitUntil = WaitUntilState.Load, Timeout = 60000 });
        await WaitForAppReadyAsync(page);
        return page;
    }

    [TestMethod]
    [TestCategory("Server")]
    public async Task Scheduler_Today_GoesToTheBrowserZoneDate()
    {
        var page = await OpenAsync("/scheduler");

        // The SECOND scheduler on the page is the plain month view WITHOUT a TimeZoneId (the first
        // one carries the demo's explicit Europe/Prague TimeZoneId) — its display zone, and
        // therefore its "Today", must come from the browser.
        var scheduler = page.Locator(".tm-scheduler").Nth(1);
        await scheduler.ScrollIntoViewIfNeededAsync();
        var todayButton = scheduler.Locator("[data-testid='scheduler-today']");
        await todayButton.WaitForAsync(new LocatorWaitForOptions { Timeout = 30000 });

        // Give the zone probe (tm-user-zone.js) a moment to resolve; the click itself re-reads the
        // resolved zone, and the title below is asserted against the Intl expectation.
        await page.WaitForTimeoutAsync(1500);
        await todayButton.ClickAsync();
        await page.WaitForTimeoutAsync(600);

        var title = await scheduler.Locator(".tm-scheduler-toolbar-title").TextContentAsync();

        var expected = await page.EvaluateAsync<string>(
            @"() => new Intl.DateTimeFormat('en-US', { timeZone: 'Pacific/Auckland', month: 'long', year: 'numeric' }).format(new Date())");

        Assert.IsFalse(string.IsNullOrWhiteSpace(title));
        StringAssert.Contains(title, expected);
    }

    [TestMethod]
    [TestCategory("Server")]
    public async Task Gantt_TodayMarker_RendersWithoutFaultingTheCircuit()
    {
        // The probe is now triggered from the Gantt too; a Server circuit whose browser sits in a
        // far zone must still render the today marker (the offset math is covered by unit tests).
        var page = await OpenAsync("/gantt");

        var marker = page.Locator("[data-testid='today-marker']").First;
        await marker.WaitForAsync(new LocatorWaitForOptions { Timeout = 30000 });

        Assert.IsTrue(await marker.IsVisibleAsync(), "the Gantt today marker must render in a far-zone browser context");
    }
}
