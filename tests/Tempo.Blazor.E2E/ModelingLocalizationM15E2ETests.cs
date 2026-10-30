using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>E2E and screenshot checks for modeling localization phase M15.</summary>
[TestClass]
[TestCategory("WASM")]
public sealed class ModelingLocalizationM15E2ETests : WasmTestBase
{
    private const string ModelingEditorUrl = "/modeling-editor";

    [TestMethod]
    [Description("Modeling editor renders toolbar, panels and buttons in Czech")]
    public async Task ModelingEditor_CzechLocaleRendersLocalizedUi()
    {
        var page = await OpenCzechModelingPageAsync();
        var editor = page.Locator("[data-testid='modeling-editor']");

        await Assertions.Expect(editor.Locator("[data-testid='modeling-editor-toolbar']")).ToContainTextAsync("Modelovací editor");
        await Assertions.Expect(editor.Locator("[data-testid='modeling-editor-toolbar']")).ToContainTextAsync("Poskytovatel");
        await Assertions.Expect(editor.Locator("[data-testid='modeling-editor-toolbar']")).ToContainTextAsync("Pohled");
        await Assertions.Expect(editor.Locator("[data-testid='modeling-model-tree']")).ToContainTextAsync("Strom modelu");
        await Assertions.Expect(editor.Locator("[data-testid='modeling-diagram-preview']")).ToContainTextAsync("Náhled diagramu");
        await Assertions.Expect(editor.Locator("[data-testid='modeling-diagram-preview']")).ToContainTextAsync("Generovat diagram");
        await Assertions.Expect(editor.Locator("[data-testid='modeling-diagram-preview']")).ToContainTextAsync("Otevřít v editoru");
        await Assertions.Expect(editor.Locator("[data-testid='modeling-inspector']")).ToContainTextAsync("Vyberte prvek");
        await Assertions.Expect(editor.Locator("[data-testid='modeling-issue-panel']")).ToContainTextAsync("Nálezy");
        // Phase 5 (MO1): the source panel is the inspector's Source tab on the docked layout —
        // only the ACTIVE tab renders its body, so the source assertions run after the switch.
        await editor.Locator("[data-testid='modeling-tab-source']").ClickAsync(new LocatorClickOptions { Timeout = 10000 });
        await Assertions.Expect(editor.Locator("[data-testid='modeling-source-panel']")).ToContainTextAsync("Datový zdroj");
    }

    [TestMethod]
    [Description("Modeling editor Czech locale does not show English UI resource text")]
    public async Task ModelingEditor_CzechLocaleHasNoEnglishUiText()
    {
        var page = await OpenCzechModelingPageAsync();

        var uiText = await GetModelingUiTextWithoutModelDataAsync(page);
        var forbidden = new[]
        {
            "Modeling Editor",
            "Model tree",
            "Diagram preview",
            "Generate diagram",
            "Open in editor",
            "Issues",
            "No issues found",
            "The generated model passed validation",
            "Inspector",
            "Select an element",
            "Choose an element",
            "Viewpoint",
            "Provider",
            "Data source",
            "Load model",
            "Elements",
            "Relationships",
            "Diagram nodes",
            "Diagram edges",
            "Application usage",
            "Overview",
            "Process"
        };

        foreach (var text in forbidden)
        {
            Assert.IsFalse(uiText.Contains(text, StringComparison.Ordinal), $"Czech modeling UI should not contain English UI text: {text}");
        }

        Assert.IsFalse(uiText.Contains("[TmModeling", StringComparison.Ordinal), "Czech modeling UI should not render missing resource keys.");
    }

    [TestMethod]
    [Description("Captures required M15 Czech localization screenshot")]
    public async Task ModelingEditor_CapturesCzechLocalizationScreenshot()
    {
        var page = await OpenCzechModelingPageAsync();

        await TakeScreenshotAsync(page, "modeling-editor-cs-CZ");
        await SaveStableScreenshotAsync(page, "modeling-editor-cs-CZ.png");
    }

    private async Task<IPage> OpenCzechModelingPageAsync()
    {
        var context = await CreateContextAsync();
        await context.AddInitScriptAsync("localStorage.setItem('tm-demo-culture', 'cs-CZ');");
        var page = await context.NewPageAsync();
        // MO3: the "Pohled" toolbar text lives on the labeled viewpoint select, which renders
        // once a notation is selected — open the page with the BPMN notation.
        await page.GotoAsync($"{BaseUrl}{ModelingEditorUrl}?notation=bpmn", new PageGotoOptions { WaitUntil = WaitUntilState.Load, Timeout = 60000 });
        await WaitForAppReadyAsync(page);
        await page.Locator("[data-testid='modeling-editor'][data-state='loaded']")
            .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });
        return page;
    }

    private static Task<string> GetModelingUiTextWithoutModelDataAsync(IPage page)
        => page.Locator("[data-testid='modeling-editor']").EvaluateAsync<string>(
            """
            element => {
                const clone = element.cloneNode(true);
                const modelDataSelectors = [
                    '.tm-modeling-model-tree__nodes',
                    '.tm-modeling-diagram-preview__canvas-shell',
                    '.tm-modeling-source-panel__value',
                    '[data-testid="modeling-inspector"] dd',
                    '[data-testid="modeling-issue-list"]'
                ];

                for (const selector of modelDataSelectors) {
                    clone.querySelectorAll(selector).forEach(item => item.remove());
                }

                return clone.innerText || '';
            }
            """);

    private static async Task SaveStableScreenshotAsync(IPage page, string fileName)
    {
        var directory = Path.Combine(
            Path.GetDirectoryName(typeof(ModelingLocalizationM15E2ETests).Assembly.Location)!,
            "..",
            "..",
            "..",
            "TestResults",
            "modeling-m15");
        Directory.CreateDirectory(directory);

        var bytes = await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Type = ScreenshotType.Png,
            FullPage = true
        });

        Assert.IsTrue(bytes.Length > 20_000, $"{fileName} screenshot should contain the Czech modeling editor.");
        await File.WriteAllBytesAsync(Path.Combine(directory, fileName), bytes);
    }
}
