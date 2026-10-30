using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// The core overflow ("More") panel must stay INSIDE the viewport on the spreadsheet's compact chrome.
/// <para>
/// Since redesign phase 6 the spreadsheet resolves its chrome from its own CONTAINER, so a 1024 px
/// viewport renders the two compact rows and most Data / Name commands live in a row's "More" menu. The
/// tablet primary row puts ~18 commands there (six labelled group sections), i.e. a panel TALLER than the
/// tablet's viewport — and the core <c>ActionOverflowMenu</c> panel only flipped, it never capped its
/// height, so the rows below the fold were painted outside the viewport with nothing to scroll them in:
/// a real tablet user could not reach them (and the phase-3 tablet dialog spec timed out clicking
/// "Custom sort…"). The fix makes the shared overflow panel clamp to the viewport and scroll internally
/// (<c>TmOverlayPanel.ConstrainHeight</c>) — verified here, never through a test-side scroll workaround.
/// </para>
/// <para>
/// Screenshots land in <c>__screenshots__/spreadsheet/</c> as
/// <c>phase8-more-panel--tablet-1024</c> and <c>phase8-more-panel--tablet-1024-short</c>.
/// </para>
/// </summary>
public partial class SpreadsheetE2ETests
{
    /// <summary>The "More" panel the row opened (the controlled TmOverlayPanel popover of that row).</summary>
    private static ILocator MorePanel(IPage page) => page.Locator(".tm-toolbar-more__panel").First;

    /// <summary>A row's "More" trigger.</summary>
    private static ILocator MoreTrigger(IPage page, string row)
        => DemoComponent(page).Locator($"[data-toolbar-row='{row}'] .tm-toolbar-more").First;

    private static async Task AssertBoxInsideViewportAsync(IPage page, ILocator locator, string what)
    {
        var viewport = page.ViewportSize!;
        var box = await locator.BoundingBoxAsync();
        Assert.IsNotNull(box, $"{what}: not rendered");
        Assert.IsTrue(box!.Y >= -0.5 && box.Y + box.Height <= viewport.Height + 0.5,
            $"{what} must stay inside the {viewport.Height}px viewport, was {box.Y:0.#}..{box.Y + box.Height:0.#} "
            + $"(height {box.Height:0.#})");
        Assert.IsTrue(box.X >= -0.5 && box.X + box.Width <= viewport.Width + 0.5,
            $"{what} must stay inside the {viewport.Width}px viewport horizontally, was {box.X:0.#}..{box.X + box.Width:0.#}");
    }

    /// <summary>Opens one row's "More" menu and waits for its panel.</summary>
    private static async Task<ILocator> OpenMoreAsync(IPage page, string row)
    {
        var trigger = MoreTrigger(page, row);
        await trigger.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 30_000 });
        await trigger.ClickAsync(new LocatorClickOptions { Timeout = 30_000 });
        var panel = MorePanel(page);
        await panel.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 30_000 });
        await page.WaitForTimeoutAsync(300);
        return panel;
    }

    /// <summary>
    /// The entries of the open panel, in menu order. Group sections wrap their entries, so the flat
    /// menu-item selector is the only stable one.
    /// </summary>
    private static ILocator MoreEntries(ILocator panel)
        => panel.Locator("[role='menuitem'], [role='menuitemcheckbox'], [role='menuitemradio']");

    /// <summary>Scrolls the panel to its end — the gesture a tablet user makes — and returns the last entry's box.</summary>
    private static async Task<Phase8Box?> ScrollPanelToEndAndMeasureLastEntryAsync(IPage page, ILocator panel)
    {
        await panel.EvaluateAsync("el => { el.scrollTop = el.scrollHeight; }");
        await page.WaitForTimeoutAsync(250);
        var last = MoreEntries(panel).Last;
        return await last.BoundingBoxAsync() is { } box ? new Phase8Box(box.X, box.Y, box.Width, box.Height) : null;
    }

    private sealed record Phase8Box(float X, float Y, float Width, float Height);

    [TestMethod]
    [TestCategory("WASM")]
    public async Task Phase8_Tablet1024_MorePanel_StaysInsideTheViewport_AndScrollsItsTallContent()
    {
        var page = await OpenPhase2DemoAsync(1024, 768, touch: true);
        try
        {
            Assert.AreEqual("tablet", await ResolvedChromeAsync(page),
                "a 1024px viewport renders the spreadsheet's two compact rows");

            var triggerBox = await MoreTrigger(page, "primary").BoundingBoxAsync();
            Assert.IsNotNull(triggerBox, "the primary row has a More trigger");

            var panel = await OpenMoreAsync(page, "primary");

            // 1) The whole panel is inside the viewport. Before the fix a panel taller than the viewport
            //    started at the margin and ran past the bottom edge, painting its tail off-screen.
            await AssertBoxInsideViewportAsync(page, panel, "the tablet More panel");

            // 2) Its content is taller than the panel and the panel itself scrolls (no page scroll needed).
            var metrics = await panel.EvaluateAsync<float[]>(
                "el => [el.scrollHeight, el.clientHeight]");
            Assert.IsTrue(metrics[0] > metrics[1] + 1,
                $"the primary row's More menu has more entries than fit, so the panel must scroll internally "
                + $"(scrollHeight {metrics[0]}, clientHeight {metrics[1]})");

            // 3) The LAST entry is reachable: after the panel's own scroll its box is inside the viewport.
            var lastBox = await ScrollPanelToEndAndMeasureLastEntryAsync(page, panel);
            Assert.IsNotNull(lastBox, "the More panel renders entries");
            var viewport = page.ViewportSize!;
            Assert.IsTrue(lastBox!.Y >= -0.5 && lastBox.Y + lastBox.Height <= viewport.Height + 0.5,
                $"the LAST More entry must be reachable inside the viewport after the panel scrolls, "
                + $"was {lastBox.Y:0.#}..{lastBox.Y + lastBox.Height:0.#} of {viewport.Height}");

            await ShotPhase2Async(page, "phase8-more-panel--tablet-1024");
        }
        finally
        {
            await page.CloseAsync();
        }
    }

    [TestMethod]
    [TestCategory("WASM")]
    public async Task Phase8_Tablet_MoreTriggerNearTheBottom_KeepsThePanelInsideTheViewport()
    {
        // A short landscape tablet window (an on-screen keyboard, a half-screen app) puts the compact
        // rows near the BOTTOM of the viewport: the panel then has to flip above the trigger and/or cap
        // itself instead of running off the bottom edge. The container is still 1024 wide, so the
        // chrome stays the tablet one (the container queries, not the viewport, drive it).
        var page = await OpenPhase2DemoAsync(1024, 768, touch: true);
        try
        {
            await page.SetViewportSizeAsync(1024, 430);
            await page.WaitForTimeoutAsync(600);
            Assert.AreEqual("tablet", await ResolvedChromeAsync(page),
                "narrowing the HEIGHT keeps the tablet container chrome");

            var trigger = MoreTrigger(page, "formatting");
            var triggerBox = await trigger.BoundingBoxAsync();
            Assert.IsNotNull(triggerBox, "the formatting row has a More trigger");
            Assert.IsTrue(triggerBox!.Y > 430 / 2.0,
                $"the trigger must sit in the lower half of the short viewport for this case, was y={triggerBox.Y:0.#}");

            var panel = await OpenMoreAsync(page, "formatting");

            await AssertBoxInsideViewportAsync(page, panel, "the More panel of a trigger near the bottom");

            // The last entry is inside the viewport too (after the panel's own scroll when it caps).
            var lastBox = await ScrollPanelToEndAndMeasureLastEntryAsync(page, panel);
            Assert.IsNotNull(lastBox, "the More panel renders entries");
            Assert.IsTrue(lastBox!.Y >= -0.5 && lastBox.Y + lastBox.Height <= 430.5,
                $"the LAST More entry must be reachable inside the 430px viewport, was "
                + $"{lastBox.Y:0.#}..{lastBox.Y + lastBox.Height:0.#}");

            // And it stays reachable with a real click — the path a tablet user (and the phase-3 dialog
            // spec) takes. "Custom sort…" is the entry the failing tablet dialog spec clicks.
            var customSort = MoreEntries(panel).Filter(new() { HasTextRegex = ExactText("Custom sort\u2026") });
            await customSort.First.ClickAsync(new LocatorClickOptions { Timeout = 30_000 });
            await page.Locator(".tm-modal-overlay.tm-spreadsheet-sort")
                .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 30_000 });

            await ShotPhase2Async(page, "phase8-more-panel--tablet-1024-short");
        }
        finally
        {
            await page.CloseAsync();
        }
    }
}
