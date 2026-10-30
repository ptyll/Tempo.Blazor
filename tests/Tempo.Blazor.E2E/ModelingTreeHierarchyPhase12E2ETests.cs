using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// E2E for the Fáze 12 model-tree hierarchy (<c>?scenario=hierarchy</c>): the BPMN subprocess
/// parents the order activities through <c>ModelingElementDto.ParentId</c>, the tree renders a
/// nested APG hierarchy (aria-level per depth, disclosure parents, one flat visible list for the
/// keyboard), the filter keeps the ancestors of matches visible and expanded, and the flat
/// SemanticType groups keep working for a model without parents. See
/// <c>TmModelingModelTreeTests</c> for the bUnit-level contract.
/// </summary>
[TestClass]
[TestCategory("WASM")]
public sealed class ModelingTreeHierarchyPhase12E2ETests : WasmTestBase
{
    private const string ModelingEditorUrl = "/modeling-editor";

    [TestMethod]
    [Description("The hierarchy scenario renders nested treeitems: the subprocess at level 1 with aria-expanded, its activities at level 2")]
    public async Task HierarchyScenario_RendersNestedApgTree()
    {
        var page = await OpenModelingPageAsync("?scenario=hierarchy&notation=bpmn");

        var subprocess = page.Locator("[data-testid='modeling-tree-node-bpmn-subprocess-fulfillment']");
        Assert.AreEqual(1, await subprocess.CountAsync(), "the subprocess renders exactly once");
        Assert.AreEqual("1", await subprocess.GetAttributeAsync("aria-level"), "the subprocess is a root");
        Assert.AreEqual("true", await subprocess.GetAttributeAsync("aria-expanded"), "a parent starts expanded");
        Assert.IsNotNull(await subprocess.GetAttributeAsync("style"), "the node carries the --tm-modeling-tree-level indent variable");
        Assert.IsTrue((await subprocess.GetAttributeAsync("style")!).Contains("--tm-modeling-tree-level: 1"),
            "the indent variable is token-driven per level");
        Assert.IsNotNull(await subprocess.Locator(".tm-modeling-model-tree__type-chevron").CountAsync() > 0 ? "chevron" : null,
            "a parent carries a disclosure chevron");

        var validate = page.Locator("[data-testid='modeling-tree-node-bpmn-validate-order']");
        Assert.AreEqual("2", await validate.GetAttributeAsync("aria-level"), "the activity nests one level deep");
        Assert.IsNull(await validate.GetAttributeAsync("aria-expanded"), "a childless node is not a disclosure");
        Assert.AreEqual("bpmn-validate-order", await validate.GetAttributeAsync("data-modeling-drag-element-id"),
            "nested nodes keep the drag bridge id");

        // The flat groups are gone in the hierarchy mode.
        Assert.AreEqual(0, await page.Locator("[data-testid^='modeling-tree-group-header-']").CountAsync(),
            "the hierarchy view renders no SemanticType group headers");
    }

    [TestMethod]
    [Description("ArrowRight re-expands a collapsed parent, ArrowLeft collapses it, and ArrowLeft on a child moves focus to the parent")]
    public async Task HierarchyKeyboard_ExpandsCollapsesAndMovesToParent()
    {
        var page = await OpenModelingPageAsync("?scenario=hierarchy&notation=bpmn");

        var subprocess = page.Locator("[data-testid='modeling-tree-node-bpmn-subprocess-fulfillment']");
        await subprocess.Locator(".tm-modeling-model-tree__type-chevron").ClickAsync();
        Assert.AreEqual("false", await subprocess.GetAttributeAsync("aria-expanded"), "the chevron collapses the parent");

        await subprocess.FocusAsync();
        await subprocess.PressAsync("ArrowRight");
        Assert.AreEqual("true", await subprocess.GetAttributeAsync("aria-expanded"),
            "ArrowRight on a collapsed parent re-expands it");

        // Collapse again through the keyboard, then ArrowLeft on the expanded parent collapses.
        await subprocess.PressAsync("ArrowLeft");
        Assert.AreEqual("false", await subprocess.GetAttributeAsync("aria-expanded"),
            "ArrowLeft on an expanded parent collapses it");

        // Re-expand, focus a child, ArrowLeft moves the focus to the parent.
        await subprocess.PressAsync("ArrowRight");
        var child = page.Locator("[data-testid='modeling-tree-node-bpmn-validate-order']");
        await child.FocusAsync();
        await child.PressAsync("ArrowLeft");
        var activeTestId = await page.EvaluateAsync<string?>(
            "() => document.activeElement?.getAttribute('data-testid')");
        Assert.AreEqual("modeling-tree-node-bpmn-subprocess-fulfillment", activeTestId,
            "ArrowLeft on a child moves the focus to the parent");
    }

    [TestMethod]
    [Description("Filtering a hierarchy keeps the ancestors of matches visible and expanded")]
    public async Task HierarchyFilter_KeepsAncestorsVisibleAndExpanded()
    {
        var page = await OpenModelingPageAsync("?scenario=hierarchy&notation=bpmn");

        // Collapse the subprocess so the filter has to REVEAL it.
        await page.Locator("[data-testid='modeling-tree-node-bpmn-subprocess-fulfillment'] .tm-modeling-model-tree__type-chevron")
            .ClickAsync();
        Assert.AreEqual(0, await page.Locator("[data-testid='modeling-tree-node-bpmn-validate-order']").CountAsync(),
            "the collapsed parent hides its children");

        var search = page.Locator("[data-testid='modeling-tree-search']");
        await search.FillAsync("validate");

        var child = page.Locator("[data-testid='modeling-tree-node-bpmn-validate-order']");
        await child.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });
        var parent = page.Locator("[data-testid='modeling-tree-node-bpmn-subprocess-fulfillment']");
        Assert.AreEqual("true", await parent.GetAttributeAsync("aria-expanded"),
            "the ancestor chain of a match stays expanded while the filter is active");
        Assert.AreEqual("2", await child.GetAttributeAsync("aria-level"),
            "the match keeps its nesting level under the revealed ancestor");
    }

    [TestMethod]
    [Description("The default demo model (no ParentId) still renders the flat SemanticType groups")]
    public async Task FlatModel_KeepsSemanticTypeGroups()
    {
        var page = await OpenModelingPageAsync();

        var groupHeaders = await page.Locator("[data-testid^='modeling-tree-group-header-']").CountAsync();
        Assert.IsTrue(groupHeaders > 1, $"the flat model keeps its type groups, found {groupHeaders}");
        Assert.AreEqual(0, await page.Locator("[role='tree'] [aria-level='3']").CountAsync(),
            "the flat mode never nests deeper than level 2");
        Assert.AreEqual(0, await page.Locator("[role='tree'] [data-testid^='modeling-tree-node-'][aria-expanded]").CountAsync(),
            "flat nodes are never disclosures");
    }

    private async Task<IPage> OpenModelingPageAsync(string query = "")
    {
        var context = await CreateContextAsync();
        await context.AddInitScriptAsync("localStorage.setItem('tm-demo-culture', 'en');");
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(1440, 900);
        await page.GotoAsync($"{BaseUrl}{ModelingEditorUrl}{query}", new PageGotoOptions { WaitUntil = WaitUntilState.Load, Timeout = 60000 });
        await WaitForAppReadyAsync(page);
        await page.Locator("[data-testid='modeling-editor'][data-state='loaded']")
            .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15000 });
        await page.Locator("[role='tree'] [role='treeitem']")
            .First.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });
        return page;
    }
}
