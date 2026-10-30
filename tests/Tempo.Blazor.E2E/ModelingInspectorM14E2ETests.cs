using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>E2E and screenshot checks for modeling inspector phase M14.</summary>
[TestClass]
[TestCategory("WASM")]
public sealed class ModelingInspectorM14E2ETests : WasmTestBase
{
    private const string ModelingEditorUrl = "/modeling-editor";

    [TestMethod]
    [Description("Clicking a model tree element shows name, semantic type and source id")]
    public async Task Inspector_TreeElementShowsCoreFields()
    {
        var page = await OpenLoadedModelingPageAsync();

        await page.Locator("[data-testid='modeling-tree-node-bpmn-validate-order']").ClickAsync();

        await ExpectVisibleAsync(page, "[data-testid='modeling-inspector'][data-kind='element']");
        await ExpectTextContainsAsync(page, "[data-testid='modeling-inspector-name']", "Validate order");
        await ExpectTextContainsAsync(page, "[data-testid='modeling-inspector-type']", "userTask");
        await ExpectTextContainsAsync(page, "[data-testid='modeling-inspector-source-id']", "demo/bpmn/validate-order");
    }

    [TestMethod]
    [Description("Clicking a preview edge shows relationship type and source/target names")]
    public async Task Inspector_PreviewEdgeShowsRelationship()
    {
        var page = await OpenLoadedModelingPageAsync();

        await ClickPreviewEdgeAsync(page, "rel-validate-to-ship");

        await page.Locator("[data-testid='modeling-editor'][data-selected-relationship-id='rel-validate-to-ship']")
            .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });
        await ExpectVisibleAsync(page, "[data-testid='modeling-inspector'][data-kind='relationship']");
        await ExpectTextContainsAsync(page, "[data-testid='modeling-inspector-type']", "sequenceFlow");
        await ExpectTextContainsAsync(page, "[data-testid='modeling-inspector-relationship-flow']", "Validate order");
        await ExpectTextContainsAsync(page, "[data-testid='modeling-inspector-relationship-flow']", "Ship order");
    }

    [TestMethod]
    [Description("Rapidly selecting two elements leaves the inspector on the last one")]
    public async Task Inspector_RapidElementSwitchShowsLastSelection()
    {
        var page = await OpenLoadedModelingPageAsync();

        await page.Locator("[data-testid='modeling-tree-node-bpmn-validate-order']").ClickAsync();
        await page.Locator("[data-testid='modeling-tree-node-bpmn-ship-order']").ClickAsync();

        await page.Locator("[data-testid='modeling-editor'][data-selected-element-id='bpmn-ship-order']")
            .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });
        await ExpectTextContainsAsync(page, "[data-testid='modeling-inspector-name']", "Ship order");
        await ExpectTextContainsAsync(page, "[data-testid='modeling-inspector-type']", "serviceTask");
    }

    [TestMethod]
    [Description("Empty element description renders a fallback instead of stale text")]
    public async Task Inspector_EmptyDescriptionShowsFallback()
    {
        var page = await OpenLoadedModelingPageAsync("?scenario=inspector-empty-description");

        await page.Locator("[data-testid='modeling-tree-node-bpmn-validate-order']").ClickAsync();

        await ExpectVisibleAsync(page, "[data-testid='modeling-inspector-description']");
        await ExpectTextContainsAsync(page, "[data-testid='modeling-inspector-description']", "(No description)");
    }

    [TestMethod]
    [Description("Low trust governance has an immediately visible indicator")]
    public async Task Inspector_LowTrustHasVisualIndicator()
    {
        var page = await OpenLoadedModelingPageAsync("?scenario=inspector-low-trust");

        await page.Locator("[data-testid='modeling-tree-node-bpmn-validate-order']").ClickAsync();

        await ExpectVisibleAsync(page, "[data-testid='modeling-inspector-trust'] .tm-modeling-inspector__value--trust-low");
        await ExpectTextContainsAsync(page, "[data-testid='modeling-inspector-trust']", "Low");
    }

    [TestMethod]
    [Description("Captures required M14 element, relationship and empty screenshots")]
    public async Task Inspector_CapturesM14Screenshots()
    {
        var element = await OpenLoadedModelingPageAsync("?scenario=inspector-many-properties");
        await element.Locator("[data-testid='modeling-tree-node-bpmn-validate-order']").ClickAsync();
        await ExpectVisibleAsync(element, "[data-testid='modeling-inspector'][data-kind='element']");
        await TakeScreenshotAsync(element, "inspector-element");
        await SaveStableScreenshotAsync(element, "inspector-element.png");

        var relationship = await OpenLoadedModelingPageAsync();
        await ClickPreviewEdgeAsync(relationship, "rel-validate-to-ship");
        await ExpectVisibleAsync(relationship, "[data-testid='modeling-inspector'][data-kind='relationship']");
        await TakeScreenshotAsync(relationship, "inspector-relationship");
        await SaveStableScreenshotAsync(relationship, "inspector-relationship.png");

        var empty = await OpenLoadedModelingPageAsync();
        await ExpectVisibleAsync(empty, "[data-testid='modeling-inspector-empty']");
        await TakeScreenshotAsync(empty, "inspector-empty");
        await SaveStableScreenshotAsync(empty, "inspector-empty.png");
    }

    [TestMethod]
    [Description("Fáze 13: the Relations tab lists the selected element's relationships and navigating to a counterpart switches the tree and the inspector")]
    public async Task Inspector_RelationsTab_NavigatesToCounterpart()
    {
        var page = await OpenLoadedModelingPageAsync();

        await page.Locator("[data-testid='modeling-tree-node-bpmn-validate-order']").ClickAsync();
        await page.Locator("[data-testid='modeling-editor'][data-selected-element-id='bpmn-validate-order']")
            .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });

        // The Relations tab sits between Properties and Source and carries the related count.
        var tabs = page.Locator("[data-testid='modeling-inspector-panel'] [role='tab']");
        Assert.AreEqual(3, await tabs.CountAsync(), "the inspector has Properties | Relations | Source");
        StringAssert.Contains(await tabs.Nth(1).TextContentAsync(), "Relations");
        StringAssert.Contains(await tabs.Nth(1).TextContentAsync(), "3", "the badge counts the related rows");

        await tabs.Nth(1).ClickAsync();
        await ExpectVisibleAsync(page, "[data-testid='modeling-related-panel']");
        await ExpectVisibleAsync(page, "[data-testid='modeling-related-outgoing-rel-validate-to-ship']");

        // Navigate to the counterpart: the tree selection AND the inspector follow.
        await page.Locator("[data-testid='modeling-related-outgoing-counterpart-rel-validate-to-ship']").ClickAsync();
        await page.Locator("[data-testid='modeling-editor'][data-selected-element-id='bpmn-ship-order']")
            .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });
        Assert.AreEqual("true", await page.Locator("[data-testid='modeling-tree-node-bpmn-ship-order']")
            .GetAttributeAsync("aria-selected"), "the counterpart is selected in the tree");
        await ExpectTextContainsAsync(page, "[data-testid='modeling-inspector-name']", "Ship order");
    }

    [TestMethod]
    [Description("Fáze 14: without the editing opt-in the inspector stays read-only (MO2 regression)")]
    public async Task Inspector_WithoutEditingOptIn_StaysReadOnly()
    {
        var page = await OpenLoadedModelingPageAsync();

        await page.Locator("[data-testid='modeling-tree-node-bpmn-validate-order']").ClickAsync();

        await page.Locator("[data-testid='modeling-inspector'][data-kind='element']")
            .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });
        Assert.AreEqual(0, await page.Locator("[data-testid='modeling-inspector'] input").CountAsync(),
            "no editing inputs render without AllowModelEditing + a writer/callback");
        Assert.AreEqual(0, await page.Locator("[data-testid='modeling-inspector-undo']").CountAsync(),
            "the undo/redo pair renders only when editing is enabled");
    }

    private async Task<IPage> OpenLoadedModelingPageAsync(string query = "")
    {
        var context = await CreateContextAsync();
        await context.AddInitScriptAsync("localStorage.setItem('tm-demo-culture', 'en');");
        var page = await context.NewPageAsync();
        await page.GotoAsync($"{BaseUrl}{ModelingEditorUrl}{query}", new PageGotoOptions { WaitUntil = WaitUntilState.Load, Timeout = 60000 });
        await WaitForAppReadyAsync(page);
        await page.Locator("[data-testid='modeling-editor'][data-state='loaded']")
            .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });
        await page.Locator("[data-testid='modeling-inspector']")
            .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });
        return page;
    }

    private static async Task ClickPreviewEdgeAsync(IPage page, string relationshipId)
    {
        var edge = page.Locator($"[data-testid='modeling-diagram-preview'] [data-edge-id='{relationshipId}'] .tm-diagram-edge-hit-path").First;
        await edge.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Attached, Timeout = 10000 });
        await edge.ClickAsync(new LocatorClickOptions { Force = true });
    }

    private static async Task ExpectVisibleAsync(IPage page, string selector, int timeout = 5000)
    {
        await page.Locator(selector).WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = timeout });
    }

    private static async Task ExpectTextContainsAsync(IPage page, string selector, string expectedText)
    {
        var text = await page.Locator(selector).TextContentAsync() ?? string.Empty;
        StringAssert.Contains(text, expectedText);
    }

    private static async Task SaveStableScreenshotAsync(IPage page, string fileName)
    {
        var directory = Path.Combine(
            Path.GetDirectoryName(typeof(ModelingInspectorM14E2ETests).Assembly.Location)!,
            "..",
            "..",
            "..",
            "TestResults",
            "modeling-m14");
        Directory.CreateDirectory(directory);

        var bytes = await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Type = ScreenshotType.Png,
            FullPage = true
        });

        Assert.IsTrue(bytes.Length > 20_000, $"{fileName} screenshot should contain the rendered modeling inspector.");
        await File.WriteAllBytesAsync(Path.Combine(directory, fileName), bytes);
    }
}
