using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// E2E for the phase-2 review fixes of the modeling model tree: collapsing EVERY group keeps the
/// group treeitems rendered (the old rendered-count dead end showed the empty-filter state and
/// destroyed the headers), keeps <c>data-visible-count</c> on the matching-count contract
/// (M10/M23/M26) while <c>data-rendered-count</c> drops to zero, and the keyboard path can
/// re-expand a collapsed group. See <c>TmModelingModelTreeTests.Single_type_model_…</c> for the
/// bUnit-level regression.
/// </summary>
[TestClass]
[TestCategory("WASM")]
public sealed class ModelingTreeCollapseE2ETests : WasmTestBase
{
    private const string ModelingEditorUrl = "/modeling-editor";

    [TestMethod]
    [Description("Collapsing all groups keeps every header rendered with data-rendered-count=0 and no empty-filter state")]
    public async Task CollapsingAllGroups_KeepsHeadersAndMatchingCount()
    {
        var page = await OpenLoadedModelingPageAsync();
        var tree = page.Locator("[data-testid='modeling-model-tree']");

        var matchingCount = await ReadIntAttributeAsync(page, "data-visible-count");
        Assert.IsTrue(matchingCount > 0, "the demo tree starts populated");

        // Collapse EVERY group through its header row click.
        var headers = page.Locator("[data-testid^='modeling-tree-group-header-']");
        var headerCount = await headers.CountAsync();
        Assert.IsTrue(headerCount > 1, $"the demo model should have multiple groups, found {headerCount}");
        for (var index = 0; index < headerCount; index++)
        {
            await headers.Nth(index).ClickAsync();
        }

        foreach (var item in await page.Locator("[role='tree'] > [role='treeitem']").AllAsync())
        {
            Assert.AreEqual("false", await item.GetAttributeAsync("aria-expanded"), "every group is collapsed");
            Assert.IsNotNull(await item.GetAttributeAsync("data-group-key"), "the header stays a keyboard-reachable group treeitem");
        }

        Assert.AreEqual(0, await page.Locator("[data-testid^='modeling-tree-node-']").CountAsync(),
            "collapsed groups render no nodes");
        Assert.AreEqual("0", await tree.GetAttributeAsync("data-rendered-count"));
        Assert.AreEqual(matchingCount.ToString(System.Globalization.CultureInfo.InvariantCulture), await tree.GetAttributeAsync("data-visible-count"),
            "data-visible-count keeps the matching count even when everything is collapsed");
        Assert.AreEqual(0, await page.Locator("[data-testid='modeling-tree-empty-filter']").CountAsync(),
            "a fully collapsed tree is NOT the empty-filter dead end");

        // The first header can still re-expand the tree (the blocker regression path).
        await headers.First.ClickAsync();
        Assert.AreEqual(1, await page.Locator("[data-testid^='modeling-tree-node-']").CountAsync());
    }

    [TestMethod]
    [Description("ArrowRight on the first collapsed group treeitem re-expands it through the keyboard path")]
    public async Task CollapsedGroup_ReExpandsThroughKeyboard()
    {
        var page = await OpenLoadedModelingPageAsync();

        var firstGroup = page.Locator("[role='tree'] > [role='treeitem']").First;
        // Click the HEADER ROW inside the wrapper — the wrapper's center is a node (selecting,
        // not toggling) while the group is expanded.
        await firstGroup.Locator(".tm-modeling-model-tree__group-header").ClickAsync();
        Assert.AreEqual("false", await firstGroup.GetAttributeAsync("aria-expanded"));

        // The wrapper treeitem is the roving-tabindex focusable: focus it, then ArrowRight.
        await firstGroup.FocusAsync();
        await firstGroup.PressAsync("ArrowRight");
        Assert.AreEqual("true", await firstGroup.GetAttributeAsync("aria-expanded"),
            "ArrowRight on a collapsed group treeitem expands it");
    }

    private async Task<IPage> OpenLoadedModelingPageAsync()
    {
        var context = await CreateContextAsync();
        await context.AddInitScriptAsync("localStorage.setItem('tm-demo-culture', 'en');");
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(1440, 900);
        await page.GotoAsync($"{BaseUrl}{ModelingEditorUrl}", new PageGotoOptions { WaitUntil = WaitUntilState.Load, Timeout = 60000 });
        await WaitForAppReadyAsync(page);
        await page.Locator("[data-testid='modeling-editor'][data-state='loaded']")
            .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15000 });
        await page.Locator("[role='tree'] > [role='treeitem']")
            .First.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });
        return page;
    }

    private static async Task<int> ReadIntAttributeAsync(IPage page, string attribute)
    {
        var raw = await page.Locator("[data-testid='modeling-model-tree']").GetAttributeAsync(attribute);
        Assert.IsTrue(int.TryParse(raw, out var value), $"attribute '{attribute}' should be numeric, got '{raw}'");
        return value;
    }
}
