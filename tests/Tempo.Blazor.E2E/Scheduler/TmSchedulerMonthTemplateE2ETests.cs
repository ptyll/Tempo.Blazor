using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Text.RegularExpressions;

namespace Tempo.Blazor.E2E;

[TestClass]
[TestCategory("WASM")]
public sealed class TmSchedulerMonthTemplateE2ETests : WasmTestBase
{
    private static string ScreenshotDir
    {
        get
        {
            var dir = Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory,
                "..",
                "..",
                "..",
                "screenshots",
                "scheduler"));
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    [TestMethod]
    public async Task TmScheduler_MonthView_EventTemplate_RendersCustomContent()
    {
        var page = await OpenSchedulerPageAsync();
        var card = page.Locator("[data-testid='scheduler-month-template']");
        var events = card.Locator("[data-testid='scheduler-month-template-event']");

        await Assertions.Expect(events).ToHaveCountAsync(3);
        await Assertions.Expect(events.First).ToContainTextAsync("HV-1023 Supplier outage");
        await Assertions.Expect(events.First).ToContainTextAsync("Critical");
        await Assertions.Expect(events.Nth(1)).ToContainTextAsync("Running");
        await Assertions.Expect(events.Nth(2)).ToContainTextAsync("Resolved");
        await Assertions.Expect(card.Locator(".tm-scheduler-month-event-title")).ToHaveCountAsync(0);

        var firstEvent = card.Locator(".tm-scheduler-month-event").First;
        await Assertions.Expect(firstEvent).ToHaveClassAsync(new Regex("tm-demo-month-event--critical"));
        var style = await firstEvent.GetAttributeAsync("style");
        Assert.IsNotNull(style);
        StringAssert.Contains(style, "--event-color: #dc2626");

        await CaptureSchedulerAsync(card, "tm-scheduler-month-template");

        // The same template reaches the other four views (B24).
        await AssertTemplateInViewAsync(card, "Week", "HV-1023");
        await AssertTemplateInViewAsync(card, "Day", "HV-1044");
        await AssertTemplateInViewAsync(card, "Timeline", "HV-1044");
        // The agenda starts on the current date (2 July), so the 1 July event is outside its range.
        await AssertTemplateInViewAsync(card, "Agenda", "HV-1044");
    }

    private static async Task AssertTemplateInViewAsync(ILocator card, string view, string title)
    {
        await card.Locator($"[role='tab'][data-view='{view}']").ClickAsync();
        await Assertions.Expect(card.Locator("[role='tab'][aria-selected='true']"))
            .ToHaveAttributeAsync("data-view", view);
        var templated = card.Locator("[data-testid='scheduler-month-template-event']",
            new LocatorLocatorOptions { HasText = title });
        await Assertions.Expect(templated.First).ToBeVisibleAsync();
    }

    private async Task<IPage> OpenSchedulerPageAsync()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(1440, 1100);
        await page.GotoAsync($"{BaseUrl}/scheduler", new PageGotoOptions
        {
            WaitUntil = WaitUntilState.DOMContentLoaded,
            Timeout = 60000
        });
        await WaitForAppReadyAsync(page);
        await page.WaitForSelectorAsync("[data-testid='scheduler-month-template'] .tm-scheduler-month-event", new PageWaitForSelectorOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 60000
        });
        return page;
    }

    private static async Task CaptureSchedulerAsync(ILocator locator, string screenshotName)
    {
        await locator.ScrollIntoViewIfNeededAsync();
        await locator.ScreenshotAsync(new LocatorScreenshotOptions
        {
            Path = Path.Combine(ScreenshotDir, $"{screenshotName}.png"),
            Type = ScreenshotType.Png,
            OmitBackground = false
        });
    }
}
