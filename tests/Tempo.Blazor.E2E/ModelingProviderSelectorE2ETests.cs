using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// E2E checks for the modeling provider selector (redesign plan phase 3, MO3): the demo enables
/// <c>ShowProviderSelector</c> and registers a second demo provider, so the header select lists
/// both sources, switching reloads the model, and an unknown <c>?provider=</code> query keeps
/// rendering the empty state.
/// </summary>
[TestClass]
[TestCategory("WASM")]
public sealed class ModelingProviderSelectorE2ETests : WasmTestBase
{
    private const string ModelingEditorUrl = "/modeling-editor";
    private const string DemoProviderKey = "tempo.demo.modeling";
    private const string DemoUmlProviderKey = "tempo.demo.modeling.uml";

    [TestMethod]
    [Description("Provider selector lists the registered demo providers and switching reloads the model")]
    public async Task ProviderSelector_SwitchProviderReloadsModel()
    {
        var page = await OpenLoadedModelingPageAsync();
        try
        {
            await ExpectVisibleAsync(page, "[data-testid='modeling-provider-select']");
            await ExpectVisibleAsync(page, "[data-testid='modeling-editor'][data-provider='" + DemoProviderKey + "']");

            var options = await page.Locator("[data-testid='modeling-provider-select'] option")
                .EvaluateAllAsync<string[]>("options => options.map(o => o.value)");
            CollectionAssert.Contains(options, DemoProviderKey);
            CollectionAssert.Contains(options, DemoUmlProviderKey);

            // The header keeps the labeled "Provider" field (M15 contract — the label text stays).
            var toolbarText = await page.Locator("[data-testid='modeling-editor-toolbar']").TextContentAsync() ?? string.Empty;
            StringAssert.Contains(toolbarText, "Provider");

            await page.Locator("[data-testid='modeling-provider-select']").SelectOptionAsync([DemoUmlProviderKey]);

            await WaitForEditorAttributeAsync(page, "data-provider", DemoUmlProviderKey);
            await WaitForEditorAttributeAsync(page, "data-state", "loaded");
            // The UML demo provider ships FOUR elements since the phase-12 hierarchy change
            // (the "Order domain" Package parenting the three classes) — the stale "3" here
            // predates it and made this wait time out on every provider switch.
            await WaitForEditorAttributeAsync(page, "data-element-count", "4");

            await TakeScreenshotAsync(page, "provider-switched");
            await SaveStableScreenshotAsync(page, "provider-switched-1440.png");
        }
        finally
        {
            await page.Context.CloseAsync();
        }
    }

    private static async Task SaveStableScreenshotAsync(IPage page, string fileName)
    {
        var directory = Path.Combine(
            Path.GetDirectoryName(typeof(ModelingProviderSelectorE2ETests).Assembly.Location)!,
            "..", "..", "..", "TestResults", "modeling-provider-selector");
        Directory.CreateDirectory(directory);

        var bytes = await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Type = ScreenshotType.Png,
            FullPage = true
        });

        Assert.IsTrue(bytes.Length > 20_000, $"{fileName} screenshot should contain the rendered modeling editor.");
        await File.WriteAllBytesAsync(Path.Combine(directory, fileName), bytes);
    }

    [TestMethod]
    [Description("Unknown provider in the query keeps the editor in the empty state")]
    public async Task ProviderSelector_UnknownProviderQueryShowsEmptyState()
    {
        var context = await CreateContextAsync();
        await context.AddInitScriptAsync("localStorage.setItem('tm-demo-culture', 'en');");
        var page = await context.NewPageAsync();
        try
        {
            await page.GotoAsync($"{BaseUrl}{ModelingEditorUrl}?provider=unknown-provider", new PageGotoOptions { WaitUntil = WaitUntilState.Load, Timeout = 60000 });
            await WaitForAppReadyAsync(page);

            await ExpectVisibleAsync(page, "[data-testid='modeling-editor'][data-state='empty']");
            await ExpectVisibleAsync(page, "[data-testid='modeling-editor-empty']");
        }
        finally
        {
            await page.Context.CloseAsync();
        }
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
        return page;
    }

    private static async Task ExpectVisibleAsync(IPage page, string selector, int timeout = 5000)
    {
        await page.Locator(selector).WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = timeout });
    }

    private static Task WaitForEditorAttributeAsync(IPage page, string attributeName, string expectedValue) =>
        page.WaitForFunctionAsync(
            """
            ([attributeName, expectedValue]) => {
                const editor = document.querySelector("[data-testid='modeling-editor']");
                return editor?.getAttribute(attributeName) === expectedValue;
            }
            """,
            new[] { attributeName, expectedValue },
            new PageWaitForFunctionOptions { Timeout = 10000 });
}
