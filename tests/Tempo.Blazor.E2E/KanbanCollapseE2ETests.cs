using System.IO;
using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// E2E coverage for the collapsible-column surface of <c>TmKanbanBoard</c> (plan a98e4a71, review fze-2):
/// the collapsed swimlane header renders the classic strip (vertical title, count, WIP) and stretches the
/// sticky header row, and clicking anywhere on a collapsed header expands it exactly once — a real browser
/// bubbles the pointer event into the header's <c>@onclick</c>, so the toggle's
/// <c>@onclick:stopPropagation</c> is what this test actually exercises (bUnit cannot simulate bubbling).
/// </summary>
[TestClass]
[TestCategory("WASM")]
public class KanbanCollapseE2ETests : WasmTestBase
{
    // Written straight into TM_E2E_SHOT_DIR (the review's /tmp/tm-kanban-shots) so the light/dark captures
    // sit next to each other for hands-on inspection.
    private static readonly string ShotDir =
        Environment.GetEnvironmentVariable("TM_E2E_SHOT_DIR") ?? Path.GetTempPath();

    private async Task ShotAsync(IPage page, string name)
    {
        Directory.CreateDirectory(ShotDir);
        await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = Path.Combine(ShotDir, name + ".png"),
            Type = ScreenshotType.Png,
            FullPage = true
        });
        await TakeScreenshotAsync(page, name);
    }

    private async Task<IPage> OpenKanbanAsync()
    {
        var page = await CreatePageAsync();
        await page.GotoAsync($"{BaseUrl}/kanban");
        await WaitForAppReadyAsync(page);
        await page.Locator(".tm-kanban__column-toggle").First.WaitForAsync(
            new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15000 });
        return page;
    }

    private static async Task SetDarkAsync(IPage page, bool dark)
    {
        // The demo shell carries its own data-theme (the ThemeService owns it), so setting the attribute on
        // <html> is overridden by the inner light island. Drive the real toggle instead.
        var toggle = page.Locator("aside [data-testid='theme-toggle']");
        var isDark = string.Equals(
            await toggle.GetAttributeAsync("aria-label"), "Switch to light mode", StringComparison.Ordinal);
        if (isDark != dark)
        {
            await toggle.ClickAsync();
        }

        await page.WaitForTimeoutAsync(250);
    }

    [TestMethod]
    [Description("E2E-FZE2.1: the collapsed classic strip keeps its summary label and tip; a header click expands it and the toggle never double-fires")]
    public async Task ClassicCollapsedColumn_HeaderClick_Expands_Once_And_ScreenshotsBothThemes()
    {
        var page = await OpenKanbanAsync();
        var board = page.Locator(".tm-kanban:not(.tm-kanban--swimlanes)").First;
        var column = board.Locator("[data-testid='board-column-todo']");
        var toggle = column.Locator("[data-testid='column-toggle-todo']");

        await toggle.ClickAsync();
        await page.WaitForTimeoutAsync(200);

        StringAssert.Contains(await column.GetAttributeAsync("class"), "tm-kanban__column--collapsed",
            "the classic column collapses");
        Assert.AreEqual("To Do", await column.GetAttributeAsync("title"),
            "the collapsed strip keeps the full column name as a title tip");
        var summary = await column.GetAttributeAsync("aria-label");
        StringAssert.Contains(summary, "To Do, 2 cards", "the strip summarises its title and count");
        StringAssert.Contains(summary, "limit 5", "the demo's To Do column is a WIP-limited column");

        await ShotAsync(page, "kanban-fze2-classic-collapsed-light");
        await SetDarkAsync(page, true);
        await ShotAsync(page, "kanban-fze2-classic-collapsed-dark");
        await SetDarkAsync(page, false);

        // The vertical title is a non-button part of the header: a pointer click there expands the column.
        await column.Locator(".tm-kanban__header-title").ClickAsync();
        await page.WaitForTimeoutAsync(200);
        Assert.IsFalse((await column.GetAttributeAsync("class") ?? string.Empty)
                .Contains("tm-kanban__column--collapsed", StringComparison.Ordinal),
            "clicking anywhere on the collapsed header expands it");
        Assert.AreEqual("true", await toggle.GetAttributeAsync("aria-expanded"));

        // A real browser bubbles the toggle's click into the header handler; the fence must stop it, so this
        // sequence ends EXPANDED — without @onclick:stopPropagation it would collapse again and end collapsed.
        await toggle.ClickAsync();
        await page.WaitForTimeoutAsync(200);
        StringAssert.Contains(await column.GetAttributeAsync("class"), "tm-kanban__column--collapsed",
            "the toggle collapses exactly once");
        await toggle.ClickAsync();
        await page.WaitForTimeoutAsync(200);
        Assert.IsFalse((await column.GetAttributeAsync("class") ?? string.Empty)
                .Contains("tm-kanban__column--collapsed", StringComparison.Ordinal),
            "the toggle expands exactly once — its click is fenced from the header's pointer handler");
    }

    [TestMethod]
    [Description("E2E-FZE2.2: the collapsed swimlane header-only column renders the classic vertical strip and stretches the sticky header row")]
    public async Task SwimlaneCollapsedColumn_RendersTheStrip_And_ScreenshotsBothThemes()
    {
        var page = await OpenKanbanAsync();
        var board = page.Locator(".tm-kanban--swimlanes").First;
        var colhead = board.Locator("[data-testid='colhead-todo']");

        await colhead.Locator("[data-testid='column-toggle-todo']").ClickAsync();
        await page.WaitForTimeoutAsync(200);

        StringAssert.Contains(await colhead.GetAttributeAsync("class"), "tm-kanban__column--collapsed");
        StringAssert.Contains(await colhead.GetAttributeAsync("aria-label"), "To Do, 3 cards, limit 5",
            "the header-only strip exposes the same named summary as the classic strip");

        var title = colhead.Locator(".tm-kanban__header-title");
        Assert.IsTrue(await title.IsVisibleAsync(), "the vertical title is rendered, not display:none");
        Assert.AreEqual("To Do", (await title.InnerTextAsync()).Trim());
        Assert.AreEqual("vertical-rl", await title.EvaluateAsync<string>("el => getComputedStyle(el).writingMode"),
            "the title uses the classic strip's vertical writing mode");

        Assert.IsTrue(await colhead.Locator(".tm-kanban__header-color").IsVisibleAsync(),
            "the colour dot is rendered");
        Assert.AreEqual("3", (await colhead.Locator(".tm-kanban__count").InnerTextAsync()).Trim());
        Assert.AreEqual("/ 5", (await colhead.Locator(".tm-kanban__wip-limit").InnerTextAsync()).Trim());

        var headerHeight = await colhead.Locator(".tm-kanban__header")
            .EvaluateAsync<double>("el => el.getBoundingClientRect().height");
        Assert.IsTrue(headerHeight >= 120, $"the sticky header row stretches to the vertical strip (was {headerHeight}px)");

        await ShotAsync(page, "kanban-fze2-swimlane-collapsed-light");
        await SetDarkAsync(page, true);
        await ShotAsync(page, "kanban-fze2-swimlane-collapsed-dark");
        await SetDarkAsync(page, false);

        // A header click on the swimlane header expands the column too.
        await title.ClickAsync();
        await page.WaitForTimeoutAsync(200);
        Assert.IsFalse((await colhead.GetAttributeAsync("class") ?? string.Empty)
                .Contains("tm-kanban__column--collapsed", StringComparison.Ordinal),
            "clicking the swimlane column header expands it");
    }
}
