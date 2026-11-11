using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Plan 7ba95ed6, Fáze 17 — the fan-out DATASET switch on <c>/ai-agents</c> in a real browser. The live sample is
/// eighteen units, which is exactly why the 0 / 1 / 200 shapes the phase-3 review asked for could never be reached
/// from a spec: an empty grid, a one-tile grid and a grid wider than <c>VisibleTaskLimit</c> were only coverable at
/// the bUnit layer. The page now offers them as FROZEN fixtures, and these facts walk what a reader does — pick 0,
/// pick 1 and open its detail, pick 200 and jump to the last unit — on all three lanes, because the base is
/// lane-agnostic (<c>AiAgentsE2ELanes.cs</c>).
/// </summary>
public abstract partial class AiAgentsE2EBase
{
    private const string FanOutSizeSwitch = "[data-testid='ai-agents-fan-out-size']";

    /// <summary>Picks a dataset through the segmented control the way a reader does.</summary>
    private static async Task SelectFanOutDatasetAsync(IPage page, string dataset)
    {
        await page.Locator($"{FanOutSizeSwitch} .tm-segmented__option[data-value='{dataset}']").ClickAsync();
    }

    private static ILocator FanOutTiles(IPage page)
        => page.Locator("[data-testid='ai-agents-fan-out'] [data-testid='fan-out-tile']");

    private static Task<int> FanOutTileCountAsync(IPage page) => FanOutTiles(page).CountAsync();

    /// <summary>
    /// The three fixtures, one after another: 0 units is the localized empty state, 1 unit is a single tile whose
    /// detail the reader can open, and 200 units stays a BOUNDED window — the grid keeps declaring the real total in
    /// <c>aria-rowcount</c> while mounting far fewer rows, and <c>Ctrl+End</c> still lands on the last unit. Picking
    /// Live again brings the page's own eighteen-unit run back, so the fixture is not a one-way door.
    /// </summary>
    [TestMethod]
    public async Task TheFanOutDatasetSwitch_WalksTheZeroOneAndTwoHundredFixtures()
    {
        var page = await OpenAsync();
        await ScrollToSection(page, FanOutSection);

        Assert.AreEqual(18, await FanOutTileCountAsync(page), "the page opens on the LIVE run");
        Assert.AreEqual(4, await page.Locator($"{FanOutSizeSwitch} .tm-segmented__option").CountAsync(),
            "Live / 0 / 1 / 200");

        // 0 units: the empty state, no tile at all, and the banner names the paused fixture.
        await SelectFanOutDatasetAsync(page, "Zero");
        await Assertions.Expect(page.Locator("[data-testid='ai-agents-fan-out'] [data-testid='fan-out-empty']"))
            .ToBeVisibleAsync();
        Assert.AreEqual(0, await FanOutTileCountAsync(page), "an empty fixture mounts no tile");
        StringAssert.Contains(
            await TextAsync(page, "ai-agents-fan-out-fixture-note"), "0 units",
            "the page says which frozen fixture is on screen");

        // 1 unit: exactly one tile, and its detail opens on the reader's click.
        await SelectFanOutDatasetAsync(page, "One");
        await Assertions.Expect(FanOutTiles(page)).ToHaveCountAsync(
            1, new LocatorAssertionsToHaveCountOptions { Timeout = 10000 });
        Assert.AreEqual("fixture-001",
            await FanOutTiles(page).First.GetAttributeAsync("data-task-id"),
            "the single fixture unit is the deterministic first batch");

        await FanOutTiles(page).First.ClickAsync();
        await Assertions.Expect(page.Locator("[data-testid='ai-agents-fan-out'] [data-testid='fan-out-detail']"))
            .ToHaveCountAsync(1, new LocatorAssertionsToHaveCountOptions { Timeout = 10000 });

        await ScrollToSection(page, FanOutSection);
        await ShootAsync(page, "ai-agents-fan-out-fixture-one-light-1440");

        // 200 units: a bounded window of whole rows over a declared total of 200. aria-rowcount counts ROWS (200 /
        // the measured column count), so the total the reader is told about is the sr-only window line.
        await SelectFanOutDatasetAsync(page, "TwoHundred");
        await Assertions.Expect(page.Locator("[data-testid='ai-agents-fan-out'] [data-testid='fan-out-window']"))
            .ToContainTextAsync("of 200",
                new LocatorAssertionsToContainTextOptions { Timeout = 10000 });

        var mounted = await FanOutTileCountAsync(page);
        Assert.IsTrue(mounted is > 0 and < 200,
            $"the 200-unit fixture mounts a window of whole rows (mounted {mounted})");

        await FanOutTiles(page).First.FocusAsync();
        await page.Keyboard.PressAsync("Control+End");
        await page.WaitForFunctionAsync(
            "() => document.activeElement?.dataset.taskId === 'fixture-200'",
            new PageWaitForFunctionOptions { Timeout = 10000 });
        Assert.IsTrue(await FanOutTileCountAsync(page) < 200,
            "jumping to the end of a 200-unit fan-out still mounts a window, not the whole list");

        await ScrollToSection(page, FanOutSection);
        await ShootAsync(page, "ai-agents-fan-out-fixture-200-light-1440");

        // Back to Live: the page's own eighteen-unit run returns.
        await SelectFanOutDatasetAsync(page, "Live");
        await Assertions.Expect(FanOutTiles(page)).ToHaveCountAsync(
            18, new LocatorAssertionsToHaveCountOptions { Timeout = 10000 });
    }

    /// <summary>
    /// The 200-unit fixture on a 375 px phone. The grid measures its own container (<c>ColumnsMode.Auto</c>), so the
    /// widest dataset in the demo still fits the narrowest demo layout: the grid never scrolls sideways even while it
    /// mounts its window, which is the regression the phase-3 follow-up was really about.
    /// </summary>
    [TestMethod]
    public async Task TheFanOutDatasetSwitch_TwoHundredUnitsFitAPhoneWithoutLateralScroll()
    {
        var page = await OpenAsync(375, 844, coarsePointer: true);
        await ScrollToSection(page, FanOutSection);

        await SelectFanOutDatasetAsync(page, "TwoHundred");
        await Assertions.Expect(page.Locator("[data-testid='ai-agents-fan-out'] [data-testid='fan-out-window']"))
            .ToContainTextAsync("of 200",
                new LocatorAssertionsToContainTextOptions { Timeout = 10000 });

        var geometry = await page.Locator("[data-testid='ai-agents-fan-out']").EvaluateAsync<int[]>(
            """
            (el) => {
                const grid = el.querySelector("[data-testid='fan-out-grid-body']");
                return [el.scrollWidth, el.clientWidth, grid.scrollWidth, grid.clientWidth];
            }
            """);
        Assert.IsTrue(geometry[0] <= geometry[1] + 1,
            $"the fan-out never scrolls sideways on a phone (scrollWidth {geometry[0]} / clientWidth {geometry[1]})");
        Assert.IsTrue(geometry[2] <= geometry[3] + 1,
            $"the grid body never scrolls sideways on a phone (scrollWidth {geometry[2]} / clientWidth {geometry[3]})");

        await ShootAsync(page, "ai-agents-fan-out-fixture-200-phone-375");
    }
}
