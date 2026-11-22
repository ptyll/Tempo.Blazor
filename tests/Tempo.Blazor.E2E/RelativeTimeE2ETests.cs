using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// F7 lane: relative time on the REAL TmTimeline (Feedback demo) through the shared
/// ITmRelativeTimeFormatter, in English, Czech and French. The demo seeds entries 1, 3 and 5 hours old.
/// The browser locale picks the demo culture; the assertions read the rendered text.
/// </summary>
[TestClass]
public class RelativeTimeE2ETests : WasmTestBase
{
    private string ShotDir
        => Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "TestResults", "relative-time");

    private async Task<IPage> OpenFeedbackAsync(string locale)
    {
        // The formatter says "5 h ago" only inside the SAME calendar day (older = "yesterday at ..."), and the demo
        // seeds entries 1/3/5 h before "now". Run after midnight (< 05:00) the 5 h entry would read "yesterday", so the
        // browser clock is pinned to a midday instant of a pinned zone: the stamps no longer depend on the time of day
        // the suite runs at. SetFixedTime keeps timers running (Blazor boot is unaffected).
        var page = await OpenAppPageAsync(
            $"{BaseUrl}/feedback",
            new BrowserNewContextOptions
            {
                ViewportSize = new ViewportSize { Width = 1280, Height = 900 },
                Locale = locale,
                IgnoreHTTPSErrors = true,
                TimezoneId = "UTC",
            },
            beforeNavigate: context => context.Clock.SetFixedTimeAsync(new DateTime(2026, 6, 15, 12, 0, 0, DateTimeKind.Utc)));
        await page.Locator(".tm-timeline").First.ScrollIntoViewIfNeededAsync();
        await page.WaitForSelectorAsync(".tm-timeline-timestamp");
        return page;
    }

    private static async Task<string[]> StampsAsync(IPage page)
        => await page.EvaluateAsync<string[]>("() => [...document.querySelectorAll('.tm-timeline-timestamp')].map(e => e.textContent.trim())");

    [TestMethod]
    [TestCategory("WASM")]
    public async Task Timeline_English_ShowsHoursAgo_NotARawKey()
    {
        var page = await OpenFeedbackAsync("en-US");
        var stamps = await StampsAsync(page);

        CollectionAssert.IsSubsetOf(new[] { "5 h ago", "3 h ago" }, stamps);
        Assert.IsFalse(stamps.Any(s => s.Contains("Tm_Time") || s.Contains("TmTimeline")), string.Join("|", stamps));
        Directory.CreateDirectory(ShotDir);
        await page.Mouse.MoveAsync(0, 0);
        await page.ScreenshotAsync(new PageScreenshotOptions { Path = Path.Combine(ShotDir, "timeline-en.png"), FullPage = false });
    }

    [TestMethod]
    [TestCategory("WASM")]
    public async Task Timeline_Czech_And_French_UseTheirOwnWords()
    {
        foreach (var (locale, expected) in new[] { ("cs-CZ", "před 5 h"), ("fr-FR", "il y a 5 h") })
        {
            var page = await OpenFeedbackAsync(locale);
            var stamps = await StampsAsync(page);
            // The demo only switches culture through its own selector; when the browser locale is not
            // honoured the English text is shown - then use the demo culture switch.
            if (!stamps.Contains(expected))
            {
                var code = locale[..2].ToUpperInvariant();
                await page.GetByRole(AriaRole.Button, new() { Name = code, Exact = true }).First.ClickAsync();
                await page.WaitForTimeoutAsync(800);
                await page.WaitForSelectorAsync(".tm-timeline-timestamp");
                stamps = await StampsAsync(page);
            }

            CollectionAssert.Contains(stamps, expected, $"{locale}: {string.Join("|", stamps)}");
            Directory.CreateDirectory(ShotDir);
            await page.Mouse.MoveAsync(0, 0);
            await page.ScreenshotAsync(new PageScreenshotOptions { Path = Path.Combine(ShotDir, $"timeline-{locale[..2]}.png"), FullPage = false });
        }
    }
}