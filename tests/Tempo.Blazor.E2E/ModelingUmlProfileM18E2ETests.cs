using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>E2E and screenshot checks for modeling UML notation profile phase M18.</summary>
[TestClass]
[TestCategory("WASM")]
public sealed class ModelingUmlProfileM18E2ETests : WasmTestBase
{
    private const string ModelingEditorUrl = "/modeling-editor";

    [TestMethod]
    [Description("UML ClassDiagram renders UML class stencils with visible compartments")]
    public async Task UmlClassDiagramUsesClassStencilsWithCompartments()
    {
        var page = await OpenLoadedModelingPageAsync("?notation=uml25&viewpoint=ClassDiagram&scenario=uml25-class");

        await Assertions.Expect(page.Locator("[data-testid='modeling-view-selector']")).ToHaveAttributeAsync("data-notation", "uml25");
        await Assertions.Expect(page.Locator("[data-testid='modeling-view-selector']")).ToHaveAttributeAsync("data-viewpoint", "ClassDiagram");
        await ExpectVisibleAsync(page, "[data-testid='modeling-diagram-preview'] g.tm-diagram-node[data-model-element-id='uml-order'][data-stencil-id='uml25.class']");
        await Assertions.Expect(page.Locator("[data-testid='modeling-diagram-preview'] g.tm-diagram-node[data-model-element-id='uml-order'] .tm-diagram-node__list-item"))
            .ToContainTextAsync(["- id: Guid", "+ Submit(): void"]);
        await ExpectVisibleAsync(page, "[data-testid='modeling-diagram-preview'] [data-edge-id='uml-generalization-order-aggregate']");

        await TakeScreenshotAsync(page, "uml-class-diagram");
        await SaveStableScreenshotAsync(page, "uml-class-diagram.png");
    }

    [TestMethod]
    [Description("UML UseCaseDiagram renders Actor and UseCase stencils")]
    public async Task UmlUseCaseDiagramUsesActorAndUseCaseStencils()
    {
        var page = await OpenLoadedModelingPageAsync("?notation=uml25&viewpoint=UseCaseDiagram&scenario=uml25-usecase");

        await ExpectVisibleAsync(page, "[data-testid='modeling-diagram-preview'] g.tm-diagram-node[data-model-element-id='uml-actor-customer'][data-stencil-id='uml25.actor']");
        await ExpectVisibleAsync(page, "[data-testid='modeling-diagram-preview'] g.tm-diagram-node[data-model-element-id='uml-usecase-place-order'][data-stencil-id='uml25.use-case']");
        await ExpectVisibleAsync(page, "[data-testid='modeling-diagram-preview'] [data-edge-id='uml-include-order-pay']");
        await Assertions.Expect(page.Locator("[data-testid='modeling-diagram-preview']")).ToContainTextAsync("Customer");
        await Assertions.Expect(page.Locator("[data-testid='modeling-diagram-preview']")).ToContainTextAsync("Place order");

        await TakeScreenshotAsync(page, "uml-usecase-diagram");
        await SaveStableScreenshotAsync(page, "uml-usecase-diagram.png");
    }

    [TestMethod]
    [Description("UML Include relationship in ClassDiagram reports an issue and is skipped")]
    public async Task UmlIncludeInClassDiagramShowsIssueAndSkipsRelationship()
    {
        var page = await OpenLoadedModelingPageAsync("?notation=uml25&viewpoint=ClassDiagram&scenario=uml25-include-invalid");

        await WaitForIssueCountAtLeastAsync(page, 1);
        await Assertions.Expect(page.Locator("[data-testid='modeling-issue-list']")).ToContainTextAsync("UseCaseDiagram");
        Assert.AreEqual(0, await page.Locator("[data-testid='modeling-diagram-preview'] [data-edge-id='uml-invalid-include']").CountAsync());
    }

    [TestMethod]
    [Description("UML Class without attributes renders only its header")]
    public async Task UmlClassWithoutAttributesRendersOnlyHeader()
    {
        var page = await OpenLoadedModelingPageAsync("?notation=uml25&viewpoint=ClassDiagram&scenario=uml25-class-empty");
        var node = page.Locator("[data-testid='modeling-diagram-preview'] g.tm-diagram-node[data-model-element-id='uml-order'][data-stencil-id='uml25.class']");

        await ExpectVisibleAsync(page, "[data-testid='modeling-diagram-preview'] g.tm-diagram-node[data-model-element-id='uml-order'][data-stencil-id='uml25.class']");
        await Assertions.Expect(node).ToContainTextAsync("Order");
        Assert.AreEqual(0, await node.Locator(".tm-diagram-node__list-item").CountAsync());
        Assert.AreEqual(0, await node.Locator(".tm-diagram-node__divider").CountAsync());
    }

    [TestMethod]
    [Description("Fáze 15: the dialog palette pins the UML profile stencils first (per-notation palette)")]
    public async Task DialogPalette_PinsUmlStencils()
    {
        var page = await OpenLoadedModelingPageAsync("?notation=uml25&viewpoint=ClassDiagram&scenario=uml25-class");

        await page.Locator("[data-testid='modeling-open-in-editor-button']")
            .ClickAsync(new LocatorClickOptions { Timeout = 10000 });
        var dialog = page.Locator("[data-testid='modeling-open-diagram-editor']");
        await dialog.Locator(".tm-modal").WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15000 });

        var pinned = dialog.Locator(".tm-diagram-toolbox__library[data-library-id='notation-palette']");
        await Assertions.Expect(pinned).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 15000 });
        // Phase 15 review: the pinned library does NOT repeat a library header — the label
        // lives on the single palette's own collapsible category header.
        await Assertions.Expect(pinned.Locator("[data-palette-id='notation-palette'] .tm-diagram-toolbox__category-header"))
            .ToContainTextAsync("UML 2.5", new LocatorAssertionsToContainTextOptions { Timeout = 10000 });
        await Assertions.Expect(pinned.Locator("[data-stencil-id='uml25.class']"))
            .ToHaveCountAsync(1, new LocatorAssertionsToHaveCountOptions { Timeout = 10000 });
        // The BPMN stencils are gone from the pinned palette (per-notation palette).
        Assert.AreEqual(0, await pinned.Locator("[data-stencil-id^='bpmn2.']").CountAsync());
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
        await page.Locator("[data-testid='modeling-diagram-preview']")
            .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });
        return page;
    }

    private static async Task ExpectVisibleAsync(IPage page, string selector, int timeout = 10000)
    {
        await page.Locator(selector).First.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = timeout });
    }

    private static Task WaitForIssueCountAtLeastAsync(IPage page, int minimumCount) =>
        page.WaitForFunctionAsync(
            """
            minimumCount => {
                const editor = document.querySelector("[data-testid='modeling-editor']");
                return Number(editor?.getAttribute('data-issue-count') ?? '0') >= minimumCount;
            }
            """,
            minimumCount,
            new PageWaitForFunctionOptions { Timeout = 10000 });

    private static async Task SaveStableScreenshotAsync(IPage page, string fileName)
    {
        var directory = Path.Combine(
            Path.GetDirectoryName(typeof(ModelingUmlProfileM18E2ETests).Assembly.Location)!,
            "..",
            "..",
            "..",
            "TestResults",
            "modeling-m18");
        Directory.CreateDirectory(directory);

        var bytes = await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Type = ScreenshotType.Png,
            FullPage = true
        });

        Assert.IsTrue(bytes.Length > 20_000, $"{fileName} screenshot should contain the UML modeling editor.");
        await File.WriteAllBytesAsync(Path.Combine(directory, fileName), bytes);
    }
}
