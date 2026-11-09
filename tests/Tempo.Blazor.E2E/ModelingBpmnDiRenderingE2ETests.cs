using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Carry-forward subtask c59bf3f1 (from phase 3): E2E proof that the BPMN DI the importer stores
/// in the model views actually RENDERS on the demo — three corpus files with full DI, the no-DI
/// fixture (headless auto-layout), the partial-DI fixture (mixed stored/synthesized geometry) and
/// the collapsed-plane fixture (subprocess with its own second BPMNPlane) — each at 1440 and 390,
/// light and dark. Phase 3 covered this only with bUnit rendering. Every shot asserts the Blazor
/// error UI stayed hidden and the PNG carries real content. The 24 captures (6 fixtures × 2
/// widths × 2 themes) are COMMITTED evidence: they land in
/// <c>tests/Tempo.Blazor.E2E/__screenshots__/modeling-bpmn-di/</c> (finding 2).
/// </summary>
[TestClass]
[TestCategory("WASM")]
public sealed class ModelingBpmnDiRenderingE2ETests : WasmTestBase
{
    private const string ModelingEditorUrl = "/modeling-editor";

    private static string OutputDirectory
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TempoBlazor.slnx")))
            {
                directory = directory.Parent!;
            }

            var path = Path.Combine(directory!.FullName, "tests", "Tempo.Blazor.E2E", "__screenshots__", "modeling-bpmn-di");
            Directory.CreateDirectory(path);
            return path;
        }
    }

    [DataTestMethod]
    [DataRow("simple-process.bpmn", "di-simple", DisplayName = "DI: simple process")]
    [DataRow("collaboration-3-pools.bpmn", "di-collaboration", DisplayName = "DI: collaboration with pools")]
    [DataRow("camunda-export.bpmn", "di-camunda", DisplayName = "DI: Camunda dialect")]
    [DataRow("no-bpmndi.bpmn", "no-di", DisplayName = "No DI: headless auto-layout")]
    [DataRow("partial-bpmndi.bpmn", "partial-di", DisplayName = "Partial DI fill")]
    [DataRow("collapsed-subprocess.bpmn", "collapsed-plane", DisplayName = "Collapsed subprocess plane")]
    [Description("BPMN DI rendering screenshots (carry-forward c59bf3f1): 1440/390 × light/dark, error UI hidden, PNG > 2KB")]
    public async Task BpmnDiRendering_ScreenshotsAtBothWidthsAndThemes(string fixture, string suffix)
    {
        foreach (var (width, height, touch) in new[] { (1440, 900, false), (390, 844, true) })
        {
            foreach (var dark in new[] { false, true })
            {
                var context = await Browser.NewContextAsync(new BrowserNewContextOptions
                {
                    ViewportSize = new ViewportSize { Width = width, Height = height },
                    HasTouch = touch,
                    IsMobile = touch && width < 640,
                    Locale = "en-US",
                    IgnoreHTTPSErrors = true,
                });
                RegisterContext(context);
                await context.AddInitScriptAsync("localStorage.setItem('tm-demo-culture', 'en');");

                var page = await context.NewPageAsync();
                await page.GotoAsync($"{BaseUrl}{ModelingEditorUrl}?bpmn={fixture}",
                    new PageGotoOptions { WaitUntil = WaitUntilState.Load, Timeout = 60000 });
                await WaitForAppReadyAsync(page);

                if (dark)
                {
                    await page.EvaluateAsync(
                        """
                        () => {
                            document.documentElement.setAttribute('data-theme', 'dark');
                            document.querySelectorAll('[data-theme]').forEach(el => el.setAttribute('data-theme', 'dark'));
                        }
                        """);
                }

                // The deep link auto-imports the fixture; wait for the imported preview to paint nodes.
                await page.WaitForFunctionAsync(
                    "() => document.querySelectorAll(\"[data-testid='modeling-diagram-preview'] g.tm-diagram-node\").length > 0",
                    null,
                    new PageWaitForFunctionOptions { Timeout = 20000 });

                // The Blazor error UI must stay hidden for every fixture and band.
                var errorUi = page.Locator("#blazor-error-ui");
                await Assertions.Expect(errorUi).ToBeHiddenAsync(new LocatorAssertionsToBeHiddenOptions { Timeout = 5000 });

                await SettleAsync(page, suffix, width, dark);
                var bytes = await page.ScreenshotAsync(new PageScreenshotOptions { Type = ScreenshotType.Png });
                Assert.IsTrue(bytes.Length > 2 * 1024,
                    $"{suffix} {width} {(dark ? "dark" : "light")}: the screenshot must contain the rendered editor, not a blank page.");

                var path = Path.Combine(OutputDirectory, $"{suffix}-{width}x{height}-{(dark ? "dark" : "light")}.png");
                await File.WriteAllBytesAsync(path, bytes);
                TestContext.AddResultFile(path);
            }
        }
    }

    private static async Task SettleAsync(IPage page, string suffix, int width, bool dark)
    {
        await page.EvaluateAsync(
            """
            async () => {
                await new Promise(resolve => {
                    let frames = 0;
                    const step = () => (++frames >= 3 ? resolve() : requestAnimationFrame(step));
                    requestAnimationFrame(step);
                });
            }
            """);
        // Finding 9: no fixed timeout — the settled signal is the imported nodes actually painted
        // (which also fails the shot loudly when a fixture stops rendering).
        var nodes = page.Locator("[data-testid='modeling-diagram-preview'] g.tm-diagram-node");
        await Assertions.Expect(nodes.First).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions
        {
            Timeout = 10000,
        });
        Assert.IsTrue(await nodes.CountAsync() > 0,
            $"{suffix} {width} {(dark ? "dark" : "light")}: the imported preview must render at least one node.");
    }
}
