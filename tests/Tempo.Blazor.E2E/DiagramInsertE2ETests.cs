using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Text.RegularExpressions;

namespace Tempo.Blazor.E2E;

/// <summary>
/// E2E tests for diagram editor Insert menu (Table, Text, Group).
/// </summary>
[TestClass]
public class DiagramInsertE2ETests : WasmTestBase
{
    private const string DiagramEditorUrl = "/diagram-editor";

    private async Task<IPage> PrepareDiagramPageAsync()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();

        await page.AddInitScriptAsync("() => localStorage.setItem('tm-demo-culture', 'en')");
        await page.GotoAsync($"{BaseUrl}{DiagramEditorUrl}", new PageGotoOptions
        {
            WaitUntil = WaitUntilState.DOMContentLoaded,
            Timeout = 60_000
        });
        await WaitForAppReadyAsync(page);

        await page.WaitForSelectorAsync(".tm-diagram-editor__toolbar", new PageWaitForSelectorOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 15000
        });
        await page.WaitForTimeoutAsync(2000);

        return page;
    }

    /// <summary>
    /// Phase 4 (UX/code review): the command row is a core TmToolbar — a collapsed group's
    /// trigger is inert and its commands live in the "More" menu as plain entries (Table/Text).
    /// Open whichever surface is currently on screen and run the insert command; from "More",
    /// Table must surface the same <c>.tm-diagram-table-inserter</c> picker.
    /// </summary>
    private async Task RunInsertCommandAsync(IPage page, string optionName)
    {
        var pattern = new Regex($"^{Regex.Escape(optionName)}$");

        var trigger = page.Locator(".tm-diagram-insert-dropdown .tm-dropdown-trigger");
        if (await trigger.CountAsync() > 0 && await trigger.IsVisibleAsync() && await trigger.IsEnabledAsync())
        {
            await trigger.ClickAsync();
            await page.WaitForTimeoutAsync(200);
            await ClickInsertOptionAsync(page, optionName);
            return;
        }

        await page.Locator(".tm-toolbar-more").First.ClickAsync();
        await page.WaitForTimeoutAsync(200);
        await page.Locator(".tm-toolbar-more__item").Filter(new() { HasTextRegex = pattern }).First.ClickAsync();
        await page.WaitForTimeoutAsync(200);
    }

    private async Task ClickInsertOptionAsync(IPage page, string optionName)
    {
        var option = page.Locator(".tm-dropdown-item")
            .Filter(new() { HasTextRegex = new Regex($"^{Regex.Escape(optionName)}$") });
        await option.ClickAsync();
        await page.WaitForTimeoutAsync(200);
    }

    [TestMethod]
    public async Task InsertTable_Picker_CreatesTableWithCorrectDimensions()
    {
        var page = await PrepareDiagramPageAsync();

        // Open the insert surface (bar dropdown, or the "More" entry once the group collapsed)
        // and pick Table.
        await RunInsertCommandAsync(page, "Table");

        // Wait for table inserter to appear
        await page.WaitForSelectorAsync(".tm-diagram-table-inserter", new PageWaitForSelectorOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 5000
        });

        // Click on cell at col=3, row=2 (0-based index 23) → creates 3×4 table
        var cells = page.Locator(".tm-diagram-table-inserter__cell");
        await cells.Nth(23).ClickAsync();
        await page.WaitForTimeoutAsync(500);

        // Verify table node was created. After F3.A `.tm-diagram-node` is an
        // SVG <g> group; Playwright's `Visible` state check on <g> is brittle,
        // so we wait for attachment and then query downstream selectors that
        // live on normal HTML inside the node's <foreignObject>.
        var tableGroup = page.Locator("g.tm-diagram-node[data-stencil-id='table.basic']");
        await tableGroup.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Attached, Timeout = 5000 });
        Assert.IsTrue(await tableGroup.CountAsync() >= 1, "Table node should exist in the SVG scene pane");
        var tableNode = page.Locator(".tm-diagram-node__shape[data-stencil-id='table.basic']");
        await tableNode.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });

        // Verify cell count inside the table (3 rows × 4 cols = 12 cells)
        var tableCells = tableNode.Locator(".tm-diagram-node__table-cell");
        var cellCount = await tableCells.CountAsync();
        Assert.AreEqual(12, cellCount, "Table should have 12 cells (3×4)");

        // Verify header cells have bold style (first row)
        var firstRowCells = tableNode.Locator(".tm-diagram-node__table-cell[data-row='0']");
        Assert.AreEqual(4, await firstRowCells.CountAsync(), "First row should have 4 cells");

        var headerCell = firstRowCells.First;
        var fontWeight = await headerCell.EvaluateAsync<string>("el => getComputedStyle(el).fontWeight");
        Assert.IsTrue(
            fontWeight == "bold" || fontWeight == "700",
            $"Header cells should be bold, got font-weight: {fontWeight}");
    }

    [TestMethod]
    public async Task InsertText_CreatesTextNode()
    {
        var page = await PrepareDiagramPageAsync();

        // Open the insert surface and pick Text
        await RunInsertCommandAsync(page, "Text");
        await page.WaitForTimeoutAsync(800);

        // After F3.A `.tm-diagram-node` is an SVG <g> group; Playwright's
        // `Visible` state check on <g> is brittle (SVG groups don't have
        // offsetWidth/offsetHeight), so we assert Attached — the concrete
        // behaviour under test is that the Text stencil got inserted into
        // the scene pane.
        var textNode = page.Locator("g.tm-diagram-node[data-stencil-id='general.text']");
        await textNode.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Attached, Timeout = 5000 });
        Assert.IsTrue(await textNode.CountAsync() >= 1, "Text node should exist in the SVG scene pane");
    }
}
