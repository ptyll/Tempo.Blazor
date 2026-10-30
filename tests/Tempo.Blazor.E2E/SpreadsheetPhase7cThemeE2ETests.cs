using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Fáze 7c — the canvas follows a LIVE theme flip (finding 18f08937). The palette is a snapshot of the
/// resolved tokens and used to be rebuilt only by the <c>ResizeObserver</c>, so flipping the theme on a
/// live page recoloured the chrome while the canvas (headers, selection) kept the OLD colours until a
/// resize happened — the phase-2 dark spec worked around it with a 2 px viewport nudge.
/// <para>
/// This spec flips dark and samples the header/selection canvases WITHOUT touching the viewport: the
/// painted active D/5 strip must equal the DARK composite of the tokens and the inactive F/7 must equal
/// the dark header background. The engine's <c>MutationObserver</c> on <c>documentElement</c> is what
/// makes that true; the node lane pins the wiring
/// (<c>themeAttributeChange_RebuildsThePalette</c>, <c>npm run test:spreadsheet</c>).
/// </para>
/// <para>Screenshot: <c>__screenshots__/spreadsheet/phase7c-theme-flip-dark--desktop.png</c>.</para>
/// </summary>
public partial class SpreadsheetE2ETests
{
    [TestMethod]
    [TestCategory("WASM")]
    public async Task Phase7c_LiveThemeFlip_RepaintsTheCanvasWithoutResize()
    {
        var page = await OpenPhase2DemoAsync(1440, 900, touch: false);
        var grid = DemoGrid(page);

        await ClickCanvasCellAsync(page, grid, "D5");
        await PressCanvasKeyAsync(page, grid, "Enter");

        var light = await SamplePhase2ChromeAsync(grid);
        CollectionAssert.AreEqual(light.ExpectedActiveHeader, light.ColumnD,
            "baseline (light): the active column header paints the light active token");

        // The LIVE flip: no viewport change, no resize, no nudge — the attribute flip alone must repaint.
        await ToggleDarkModeAsync(page);
        await page.WaitForSelectorAsync("[data-theme='dark']", new PageWaitForSelectorOptions { Timeout = 15_000 });
        // One paint past the attribute mutation (the watcher schedules a single full redraw on rAF).
        await page.WaitForTimeoutAsync(500);

        var dark = await SamplePhase2ChromeAsync(grid);

        Assert.AreNotEqual(Describe(light.ColumnD), Describe(dark.ColumnD),
            "the canvas must have been repainted with the NEW palette by the theme flip alone "
            + "(an unchanged pixel means the watcher did not rebuild the palette)");
        Assert.AreEqual(1.0, dark.HeaderActiveAlpha,
            "the dark active header token still resolves OPAQUE (the phase-7b dark mix) after the live flip");
        CollectionAssert.AreEqual(dark.ExpectedActiveHeader, dark.ColumnD,
            "the active COLUMN header paints the dark active token without any resize");
        CollectionAssert.AreEqual(dark.ExpectedActiveHeader, dark.Row5,
            "the active ROW header paints the dark active token without any resize");
        CollectionAssert.AreEqual(dark.ExpectedHeader, dark.ColumnF,
            "an inactive column header paints the dark header background");
        CollectionAssert.AreEqual(dark.ExpectedHeader, dark.Row7,
            "an inactive row header paints the dark header background");
        CollectionAssert.AreEqual(dark.ExpectedSelectionFill, dark.SelectionD5,
            "the selection wash follows the dark tokens too");

        await ShotPhase2Async(page, "phase7c-theme-flip-dark--desktop");
        await page.CloseAsync();
    }
}
