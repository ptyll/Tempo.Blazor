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
