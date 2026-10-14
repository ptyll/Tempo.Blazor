using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Fáze 25b (default on): the Server demo no longer calls AddTempoUserTimeZone() — the obsolete
/// alias is gone from its Program.cs, because `AddTempoBlazor()` itself registers the relative-time
/// formatter over the user's zone since 2.9.1. The relative-time stamps on the /feedback TmTimeline
/// must therefore be judged in the BROWSER's zone, not the server's, with no opt-in anywhere. The
/// browser context reports "Pacific/Auckland" (UTC+12/+13, ahead of the UTC server) — the expected
/// values are computed with Intl inside the page itself, so the test is deterministic at any hour.
/// </summary>
[TestClass]
public class UserTimeZoneServerE2ETests : ServerTestBase
{
    private async Task<IPage> OpenFeedbackInZoneAsync(string timeZoneId)
    {
        var context = await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = 1280, Height = 900 },
            Locale = "en-US",
            TimezoneId = timeZoneId,
            IgnoreHTTPSErrors = true,
        });
        RegisterContext(context);
        // The Server demo localizes from the tm-demo-culture cookie (its middleware sets the process
        // defaults); without it the stamps would come out in the host OS locale and the English
        // assertions below would not hold.
        await context.AddCookiesAsync(new[]
        {
            new Cookie { Name = "tm-demo-culture", Value = "en", Url = BaseUrl },
        });
        var page = await context.NewPageAsync();
        await page.GotoAsync($"{BaseUrl}/feedback", new PageGotoOptions { WaitUntil = WaitUntilState.Load, Timeout = 60000 });
        await WaitForAppReadyAsync(page);
        await page.Locator(".tm-timeline").First.ScrollIntoViewIfNeededAsync();
        await page.WaitForSelectorAsync(".tm-timeline-timestamp");
        return page;
    }

    [TestMethod]
    [TestCategory("Server")]
    public async Task UserTimeZone_Server_FormatsInTheBrowserZone()
    {
        var page = await OpenFeedbackInZoneAsync("Pacific/Auckland");

        // The first demo entry ("Changed status to In Progress") is ~5 h old. The prerender is UTC;
        // once the circuit probes tm-user-zone.js the stamp and its absolute tooltip must reflect
        // Pacific/Auckland — computed with Intl in the page so the assertion holds at any hour.
        await page.WaitForFunctionAsync(
            @"() => {
                const el = document.querySelector('.tm-timeline-timestamp');
                if (!el) return false;
                const created = new Date(el.getAttribute('datetime'));
                const clock = new Intl.DateTimeFormat('en-US', { timeZone: 'Pacific/Auckland', hour: 'numeric', minute: '2-digit', hour12: true }).format(created);
                const titleOk = el.getAttribute('title')?.includes(clock) ?? false;
                const day = d => new Intl.DateTimeFormat('en-CA', { timeZone: 'Pacific/Auckland', dateStyle: 'short' }).format(d);
                const sameDay = day(created) === day(new Date());
                const stamp = el.textContent.trim();
                const stampOk = sameDay ? /\d+ h ago/.test(stamp) : stamp.startsWith('yesterday at');
                return titleOk && stampOk;
            }",
            null,
            new PageWaitForFunctionOptions { Timeout = 30000 });

        var evidence = await page.EvaluateAsync<string>(
            "() => document.querySelector('.tm-timeline-timestamp').getAttribute('title')");
        Assert.IsFalse(string.IsNullOrWhiteSpace(evidence));
    }
}
