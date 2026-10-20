using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// E2E for the phase-12 demo sections. bUnit covers the controls; this proves the same sections
/// render in a real browser and that turning the card appearance on adds the card surface, with no
/// unhandled console error on either changed page.
/// </summary>
[TestClass]
public class DataTableRedesignDemoE2ETests : WasmTestBase
{
    [TestMethod]
    [TestCategory("WASM")]
    public async Task RedesignSections_Render_AndCardAppearanceAddsTheSurface()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        var errors = new List<string>();
        page.Console += (_, message) =>
        {
            // The legacy view provider probes /default and gets 404 when no default exists - expected, not a defect.
            if (message.Type == "error" && !message.Text.Contains("status of 404"))
                errors.Add(message.Text);
        };
        page.PageError += (_, error) => errors.Add(error);
        

        await page.SetViewportSizeAsync(1440, 1100);
        await page.GotoAsync($"{BaseUrl}/data-table",
            new PageGotoOptions { WaitUntil = WaitUntilState.Load, Timeout = 60000 });
        await WaitForAppReadyAsync(page);

        var section = page.Locator("[data-testid='dt-redesign-section']");
        await section.WaitForAsync(new LocatorWaitForOptions { Timeout = 30000 });
        await Assertions.Expect(page.Locator("[data-testid='dt-redesign-appearance']")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("[data-testid='dt-row-activations']")).ToContainTextAsync("0");
        await Assertions.Expect(page.Locator("[data-testid='dt-views-demo']")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("[data-testid='dt-filter-popover'] .tm-filter-builder")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("[data-testid='dt-filter-auto'] .tm-filter-builder")).ToBeVisibleAsync();

        var table = page.Locator("[data-testid='dt-redesign-table'] .tm-data-table-wrapper");
        Assert.IsFalse((await table.GetAttributeAsync("class"))!.Contains("tm-data-table-wrapper--card-surface"),
            "the card surface stays off until the demo opts in");

        await page.Locator("[data-testid='dt-redesign-appearance'] select").SelectOptionAsync("Card");
        await Assertions.Expect(table).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("tm-data-table-wrapper--card-surface"));

        await page.GotoAsync($"{BaseUrl}/multi-view-list",
            new PageGotoOptions { WaitUntil = WaitUntilState.Load, Timeout = 60000 });
        await WaitForAppReadyAsync(page);
        await Assertions.Expect(page.Locator("[data-testid='mvl-redesign-section']")).ToBeVisibleAsync(new() { Timeout = 30000 });
        await Assertions.Expect(page.Locator("[data-testid='mvl-redesign-cards'] .tm-mvl-card").First).ToBeVisibleAsync();

        Assert.AreEqual(0, errors.Count, "console errors: " + string.Join(" | ", errors));
    }

    private async Task<IPage> OpenDataTableAsync(int width, int height)
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(width, height);
        await page.GotoAsync($"{BaseUrl}/data-table",
            new PageGotoOptions { WaitUntil = WaitUntilState.Load, Timeout = 60000 });
        await WaitForAppReadyAsync(page);
        return page;
    }

    [TestMethod]
    [TestCategory("WASM")]
    public async Task MobileCardsSection_At24rem_RendersCards_WithScreenshot()
    {
        var page = await OpenDataTableAsync(1440, 1100);
        var section = page.Locator("[data-testid='dt-redesign-mobile-cards']");
        await section.WaitForAsync(new LocatorWaitForOptions { Timeout = 30000 });
        await section.ScrollIntoViewIfNeededAsync();

        var width = (await section.BoundingBoxAsync())!.Width;
        Assert.IsTrue(width <= 24 * 16 + 2, $"the section is constrained to 24rem, was {width}px");
        await Assertions.Expect(section.Locator(".tm-data-table-wrapper--cards, .tm-data-table-card").First)
            .ToBeVisibleAsync(new() { Timeout = 15000 });
        await Assertions.Expect(section.Locator("tbody tr").First).ToContainTextAsync("Ada Lovelace");
        await SaveElementScreenshotAsync(section, "redesign-mobile-cards-24rem");
    }

    [TestMethod]
    [TestCategory("WASM")]
    public async Task ExportSelected_WithEmptySelection_IsAriaDisabled_AndExplainsWhy()
    {
        var page = await OpenDataTableAsync(1440, 1100);
        var table = page.Locator("[data-testid='dt-redesign-table']");
        await table.WaitForAsync(new LocatorWaitForOptions { Timeout = 30000 });
        await table.ScrollIntoViewIfNeededAsync();

        // The scope switch is opt-in: turn "Export selected rows" on, leave the selection empty.
        await page.Locator("[data-testid='dt-redesign-export-selected'] .tm-toggle-track").ClickAsync();
        await table.Locator(".tm-data-table__export .tm-dropdown-trigger").ClickAsync();

        var selected = page.Locator("[data-export-scope='selected']");
        await Assertions.Expect(selected).ToBeVisibleAsync(new() { Timeout = 10000 });
        await Assertions.Expect(selected).ToHaveAttributeAsync("aria-disabled", "true");
        await Assertions.Expect(selected).ToContainTextAsync(new System.Text.RegularExpressions.Regex("\\S"));
        var describedBy = await selected.GetAttributeAsync("aria-describedby");
        Assert.IsFalse(string.IsNullOrEmpty(describedBy), "the disabled scope points at its explanation");
        await Assertions.Expect(page.Locator($"[id='{describedBy}']")).ToBeVisibleAsync();

        await selected.ClickAsync(new LocatorClickOptions { Force = true });
        await Assertions.Expect(page.Locator("[data-export-scope='all']"))
            .ToHaveClassAsync(new System.Text.RegularExpressions.Regex("tm-data-table__export-scope--selected"));
        await SaveScreenshotAsync(page, "redesign-export-selected-empty", fullPage: false);
    }

    [TestMethod]
    [TestCategory("WASM")]
    public async Task ViewPanel_Open_ShowsViewsAndSetDefault_WithScreenshot()
    {
        var page = await OpenDataTableAsync(1440, 1100);
        var section = page.Locator("[data-testid='dt-views-demo']");
        await section.WaitForAsync(new LocatorWaitForOptions { Timeout = 30000 });
        await section.ScrollIntoViewIfNeededAsync();

        var toggle = section.Locator(".tm-view-manager-toggle");
        await Assertions.Expect(toggle).ToHaveAttributeAsync("aria-expanded", "false");
        await toggle.ClickAsync();
        await Assertions.Expect(toggle).ToHaveAttributeAsync("aria-expanded", "true");

        var panel = page.Locator(".tm-view-manager-panel").First;
        await Assertions.Expect(panel).ToBeVisibleAsync(new() { Timeout = 10000 });
        await Assertions.Expect(panel).ToContainTextAsync("Engineering");
        await SaveScreenshotAsync(page, "redesign-view-panel-open", fullPage: false);
    }

    [TestMethod]
    [TestCategory("WASM")]
    public async Task PinnedColumns_At1024_DoNotOverlap_AndFirstPinnedStaysPutWhileScrolling()
    {
        var page = await OpenDataTableAsync(1024, 900);
        var section = page.Locator("[data-testid='dt-ergonomics-section']");
        await section.WaitForAsync(new LocatorWaitForOptions { Timeout = 30000 });
        await section.ScrollIntoViewIfNeededAsync();

        var left = section.Locator("thead th.tm-col-pinned-left").First;
        var right = section.Locator("thead th.tm-col-pinned-right").First;
        await left.WaitForAsync(new LocatorWaitForOptions { Timeout = 30000 });
        await right.WaitForAsync(new LocatorWaitForOptions { Timeout = 30000 });

        // Force a horizontal overflow: 640px still fits both pinned columns (180 + 220 + expander) but is narrower than the 5-column table.
        var scroller = section.Locator(".tm-data-table-scroll").First;
        await scroller.EvaluateAsync("el => { el.style.maxWidth = '640px'; el.style.overflowX = 'auto'; }");
        await page.WaitForTimeoutAsync(300); // the pin script re-measures 100ms after a resize

        var leftBox = (await left.BoundingBoxAsync())!;
        var rightBox = (await right.BoundingBoxAsync())!;
        Assert.IsTrue(leftBox.X + leftBox.Width <= rightBox.X + 0.5,
            $"pinned columns overlap: left ends at {leftBox.X + leftBox.Width}, right starts at {rightBox.X}");

        // A pinned body cell sits under its header, not at the sum of every pinned header.
        var leftCell = section.Locator("tbody td.tm-col-pinned-left").First;
        var cellBox = (await leftCell.BoundingBoxAsync())!;
        Assert.AreEqual(leftBox.X, cellBox.X, 1.0, "body cell aligns with its pinned header");

        var scrollWidth = await scroller.EvaluateAsync<int>("el => el.scrollWidth - el.clientWidth");
        Assert.IsTrue(scrollWidth > 0, "the table must overflow horizontally for the scroll assertion to mean anything");

        // Scroll far enough for the column to stick (an unpinned expander column precedes it, so its
        // natural position is a little right of the edge), then scroll further: a pinned column must
        // not move between two scrolled positions.
        await scroller.EvaluateAsync("el => { el.scrollLeft = 120; }");
        await page.WaitForTimeoutAsync(200);
        var before = (await left.BoundingBoxAsync())!.X;
        await scroller.EvaluateAsync("el => { el.scrollLeft = el.scrollWidth; }");
        await page.WaitForTimeoutAsync(200);
        var scrolled = await scroller.EvaluateAsync<double>("el => el.scrollLeft");
        Assert.IsTrue(scrolled > 120, "the wrapper scrolled further horizontally");

        var after = (await left.BoundingBoxAsync())!.X;
        Assert.AreEqual(before, after, 1.0, "the pinned column keeps its left edge while the wrapper scrolls");
        var scrollerBox = (await scroller.BoundingBoxAsync())!;
        Assert.IsTrue(after >= scrollerBox.X - 1 && after <= scrollerBox.X + 60,
            $"the pinned column is stuck at the scroller's left edge ({after} vs {scrollerBox.X})");

        var rightAfter = (await right.BoundingBoxAsync())!;
        Assert.IsTrue(after + (await left.BoundingBoxAsync())!.Width <= rightAfter.X + 0.5,
            "pinned columns still do not overlap after scrolling");
        await SaveElementScreenshotAsync(section, "redesign-pinned-columns-1024-scrolled");
    }

    [TestMethod]
    [TestCategory("WASM")]
    public async Task ScrollLayout_FirstColumn_StaysPinnedWhileScrollingHorizontally()
    {
        var page = await OpenDataTableAsync(1024, 900);
        var host = page.Locator("[data-testid='dt-export-responsive']");
        await host.WaitForAsync(new LocatorWaitForOptions { Timeout = 30000 });
        await host.ScrollIntoViewIfNeededAsync();

        // The Scroll layout pins its first column below the 640px container breakpoint.
        await host.EvaluateAsync("el => { el.style.maxWidth = '360px'; }");
        await page.WaitForTimeoutAsync(300);

        var scroller = host.Locator(".tm-data-table-scroll").First;
        var first = host.Locator("tbody tr").First.Locator("td.data-mobile-pinned").First;
        await first.WaitForAsync(new LocatorWaitForOptions { Timeout = 15000 });

        var overflow = await scroller.EvaluateAsync<int>("el => el.scrollWidth - el.clientWidth");
        Assert.IsTrue(overflow > 0, "the Scroll layout overflows horizontally in a narrow container");

        var before = (await first.BoundingBoxAsync())!.X;
        await scroller.EvaluateAsync("el => { el.scrollLeft = el.scrollWidth; }");
        await page.WaitForTimeoutAsync(200);
        Assert.IsTrue(await scroller.EvaluateAsync<double>("el => el.scrollLeft") > 0, "the wrapper scrolled");

        var after = (await first.BoundingBoxAsync())!.X;
        Assert.AreEqual(before, after, 1.0, "the first Scroll column keeps its left edge while scrolling");
    }

    private static async Task SaveScreenshotAsync(IPage page, string fileName, bool fullPage = true)
    {
        var dir = ScreenshotDirectory();
        await page.ScreenshotAsync(new PageScreenshotOptions { Path = Path.Combine(dir, $"{fileName}.png"), FullPage = fullPage });
    }

    private static async Task SaveElementScreenshotAsync(ILocator locator, string fileName)
    {
        var dir = ScreenshotDirectory();
        await locator.ScreenshotAsync(new LocatorScreenshotOptions { Path = Path.Combine(dir, $"{fileName}.png") });
    }

    private static string ScreenshotDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TempoBlazor.slnx")))
            directory = directory.Parent;
        var dir = Path.Combine(directory?.FullName ?? throw new InvalidOperationException("Repository root was not found."),
            "tests", "Tempo.Blazor.E2E", "__screenshots__", "data-table");
        Directory.CreateDirectory(dir);
        return dir;
    }}

/// <summary>Server host smoke for the same sections: the demos share Demo.SharedUI, so the Server host must render them too.</summary>
[TestClass]
public class DataTableRedesignDemoServerE2ETests : ServerTestBase
{
    [TestMethod]
    [TestCategory("Server")]
    public async Task RedesignSections_RenderOnTheServerHost_WithoutConsoleErrors()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        var errors = new List<string>();
        page.Console += (_, message) =>
        {
            // The legacy view provider probes /default and gets 404 when no default exists - expected.
            if (message.Type == "error" && !message.Text.Contains("status of 404"))
                errors.Add(message.Text);
        };
        page.PageError += (_, error) => errors.Add(error);

        await page.SetViewportSizeAsync(1440, 1100);
        await page.GotoAsync($"{BaseUrl}/data-table",
            new PageGotoOptions { WaitUntil = WaitUntilState.Load, Timeout = 60000 });
        await WaitForAppReadyAsync(page);
        await Assertions.Expect(page.Locator("[data-testid='dt-redesign-section']")).ToBeVisibleAsync(new() { Timeout = 30000 });
        await Assertions.Expect(page.Locator("[data-testid='dt-redesign-mobile-cards']")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("[data-testid='dt-views-demo']")).ToBeVisibleAsync();

        await page.GotoAsync($"{BaseUrl}/multi-view-list",
            new PageGotoOptions { WaitUntil = WaitUntilState.Load, Timeout = 60000 });
        await WaitForAppReadyAsync(page);
        await Assertions.Expect(page.Locator("[data-testid='mvl-redesign-section']")).ToBeVisibleAsync(new() { Timeout = 30000 });
        await Assertions.Expect(page.Locator("[data-testid='mvl-redesign-cards'] .tm-mvl-card").First).ToBeVisibleAsync();

        Assert.AreEqual(0, errors.Count, "console errors: " + string.Join(" | ", errors));
    }
}
