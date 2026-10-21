using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Grid keyboard on the data-table demo: one activation per Enter, Space selects without
/// activating, and the roving cell keeps a visible focus ring. Real bubbling — the thing
/// bUnit cannot see — is what this spec exists for.
/// </summary>
[TestClass]
public class TmDataTableGridKeyboardE2ETests : WasmTestBase
{
    private const string DataTablePage = "/data-table";

    [TestMethod]
    [TestCategory("WASM")]
    public async Task Grid_EnterActivatesOnce_SpaceSelects_FocusRing()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        await page.GotoAsync($"{BaseUrl}{DataTablePage}", new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });

        var section = page.Locator("[data-testid='dt-grid-keyboard-section']");
        await section.ScrollIntoViewIfNeededAsync();
        var cell = section.Locator("td[tabindex='0']");
        await cell.FocusAsync();
        await Assertions.Expect(cell).ToHaveAttributeAsync("aria-colindex", "2");

        await page.Keyboard.PressAsync("Enter");
        var activated = section.Locator("[data-testid='dt-grid-activated']");
        await Assertions.Expect(activated).Not.ToContainTextAsync("none");
        var afterEnter = await activated.InnerTextAsync();

        await page.Keyboard.PressAsync(" ");
        await Assertions.Expect(section.Locator("tbody tr.tm-row-selected")).ToHaveCountAsync(1);
        Assert.AreEqual(afterEnter, await activated.InnerTextAsync(),
            "Space must toggle selection and must not activate the row.");

        await page.Keyboard.PressAsync("ArrowRight");
        var moved = section.Locator("tbody tr").First.Locator("td[tabindex='0']");
        await Assertions.Expect(moved).ToHaveAttributeAsync("aria-colindex", "3");
        await Assertions.Expect(moved).ToBeFocusedAsync();

        var dir = Path.Combine(FindRepoRoot().FullName, "tests", "Tempo.Blazor.E2E", "__screenshots__", "data-table");
        Directory.CreateDirectory(dir);
        await moved.ScreenshotAsync(new LocatorScreenshotOptions
        {
            Path = Path.Combine(dir, "grid-cell-focus-light.png"),
        });

        await page.EvaluateAsync(
            """
            () => {
                const root = document.querySelector('[data-theme]');
                root.setAttribute('data-theme', 'dark');
                root.classList.toggle('dark', true);
            }
            """);
        await page.WaitForTimeoutAsync(400);
        await moved.ScreenshotAsync(new LocatorScreenshotOptions
        {
            Path = Path.Combine(dir, "grid-cell-focus-dark.png"),
        });

        // Edge: an empty grid has no cell to land on, so the single stop moves to the first header
        // and an arrow walks it to the next one. A second stop would be a broken roving contract.
        var empty = page.Locator("[data-testid='dt-grid-empty']");
        await empty.ScrollIntoViewIfNeededAsync();
        var header = empty.Locator("th[tabindex='0']");
        await Assertions.Expect(header).ToHaveCountAsync(1);
        await header.FocusAsync();
        await page.Keyboard.PressAsync("ArrowRight");
        await Assertions.Expect(empty.Locator("th[tabindex='0']")).ToHaveAttributeAsync("aria-colindex", "2");
        await Assertions.Expect(empty.Locator("[tabindex='0']")).ToHaveCountAsync(1);
    }

    [TestMethod]
    [TestCategory("WASM")]
    public async Task Grid_ArrowDown_AcrossGroupHeader_FocusesHeader()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        await page.GotoAsync($"{BaseUrl}{DataTablePage}", new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });

        var grouped = page.Locator("[data-testid='dt-grid-grouped']");
        await grouped.ScrollIntoViewIfNeededAsync();
        await Assertions.Expect(grouped.Locator("tr.tm-data-table-group-row")).ToHaveCountAsync(2);

        // The first stop is the first group header (column 0). Step onto its first item, then
        // across to a later column, so the next ArrowDown crosses the following group header
        // from a column that header does not render.
        var headerStop = grouped.Locator("tr.tm-data-table-group-row").First.Locator("td[tabindex='0']");
        await headerStop.FocusAsync();
        await page.Keyboard.PressAsync("ArrowDown");
        await page.Keyboard.PressAsync("ArrowRight");
        await page.Keyboard.PressAsync("ArrowDown");

        var header = grouped.Locator("tr.tm-data-table-group-row").Nth(1).Locator("td[data-grid-cell]");
        await Assertions.Expect(header).ToBeFocusedAsync();
        await Assertions.Expect(grouped.Locator("[tabindex='0']")).ToHaveCountAsync(1);
    }

    [TestMethod]
    [TestCategory("WASM")]
    public async Task Grid_Virtualized_CtrlEnd_FocusesLastRenderedCell()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        await page.GotoAsync($"{BaseUrl}{DataTablePage}", new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });

        var virtualized = page.Locator("[data-testid='dt-grid-virtualized']");
        await virtualized.ScrollIntoViewIfNeededAsync();

        await virtualized.Locator("td[tabindex='0']").FocusAsync();
        await page.Keyboard.PressAsync("Control+End");

        // The last row is far outside the rendered window: the scroll must wait for <Virtualize>
        // to paint it before focus lands, otherwise the stop is on a row with no DOM cell.
        await Assertions.Expect(virtualized.Locator("td[tabindex='0']")).ToHaveAttributeAsync("data-grid-cell", "499:1");
        await Assertions.Expect(virtualized.Locator("td[tabindex='0']")).ToBeFocusedAsync();
    }

    [TestMethod]
    [TestCategory("WASM")]
    public async Task Grid_EnterOnEditActionsCell_StartsEdit()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        await page.GotoAsync($"{BaseUrl}{DataTablePage}", new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });

        var section = page.Locator("[data-testid='dt-grid-edit']");
        await section.ScrollIntoViewIfNeededAsync();

        // The edit button inside the cell is tabindex=-1, so the keyboard user is on the cell
        // itself: Enter there must start the edit, not activate the row.
        var actionsCell = section.Locator("tbody tr").First.Locator("td.tm-data-table__edit-actions");
        await actionsCell.FocusAsync();
        await page.Keyboard.PressAsync("Enter");

        await Assertions.Expect(section.Locator("tr.tm-row-editing")).ToHaveCountAsync(1);
        await Assertions.Expect(section.Locator("[data-testid='grid-edit-note-input']")).ToHaveCountAsync(1);

        await page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(section.Locator("tr.tm-row-editing")).ToHaveCountAsync(0);
    }

    [TestMethod]
    [TestCategory("WASM")]
    public async Task Grid_ClickCell_MoveStop_FocusReturnsToClickedCell()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        await page.GotoAsync($"{BaseUrl}{DataTablePage}", new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });

        var section = page.Locator("[data-testid='dt-grid-keyboard-section']");
        await section.ScrollIntoViewIfNeededAsync();

        var clicked = section.Locator("tbody tr").Nth(1).Locator("td").Nth(1);
        await clicked.ClickAsync();
        await Assertions.Expect(clicked).ToHaveAttributeAsync("tabindex", "0");

        // Tab out and back: the roving stop must resume from the clicked cell, not the old one.
        await page.Keyboard.PressAsync("Shift+Tab");
        await page.Keyboard.PressAsync("Tab");
        await Assertions.Expect(clicked).ToBeFocusedAsync();
    }

    private static DirectoryInfo FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "TempoBlazor.slnx")))
                return directory;
            directory = directory.Parent;
        }

        throw new InvalidOperationException("Repository root was not found.");
    }
}
