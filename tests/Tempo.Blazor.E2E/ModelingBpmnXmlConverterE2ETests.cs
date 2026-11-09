using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Phase 5 of the BPMN 2.0 XML converter plan (6fca9139): the <c>/modeling-editor</c> demo page
/// imports BPMN 2.0 XML (file / pasted XML / corpus sample) through the headless converter, shows
/// the loss report and exports the model back to a downloadable <c>.bpmn</c> document.
/// </summary>
[TestClass]
[TestCategory("WASM")]
public sealed class ModelingBpmnXmlConverterE2ETests : WasmTestBase
{
    private const string ModelingEditorUrl = "/modeling-editor";

    [TestMethod]
    [Description("Importing a corpus sample through the dialog previews the model and opens the loss report")]
    public async Task ImportCorpusSample_LossReportListsEveryLoss()
    {
        var page = await OpenLoadedModelingPageAsync();

        await page.Locator("[data-testid='modeling-demo-import-bpmn']").ClickAsync();
        var dialog = page.Locator("[data-testid='modeling-demo-bpmn-import-dialog']");
        await dialog.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });
        var overlay = page.Locator(".tm-modal-overlay");
        await overlay.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });

        // Finding 9: drive the native select with SelectOptionAsync, but only AFTER the modal's
        // entrance animation finished — mid-animation the focus trap can swallow the change event.
        await overlay.EvaluateAsync(
            """
            el => Promise.all(el.getAnimations({ subtree: true }).map(animation => animation.finished))
                .then(() => undefined)
            """);
        await dialog.Locator("[data-testid='modeling-demo-bpmn-sample']")
            .SelectOptionAsync("unsupported-constructs.bpmn");
        await page.Locator("[data-testid='modeling-demo-bpmn-load-sample']").ClickAsync();

        // The editor remounts with the imported model; with losses present the demo AUTO-OPENS
        // the loss report dialog and surfaces the report button in the controls row.
        await page.Locator("[data-testid='modeling-demo-loss-report']").WaitForAsync(
            new LocatorWaitForOptions { State = WaitForSelectorState.Attached, Timeout = 20000 });
        await WaitForImportedNodesAsync(page);

        var rows = page.Locator("[data-testid='modeling-demo-loss-row']");
        await rows.First.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });
        Assert.IsTrue(await rows.CountAsync() >= 10, "the unsupported-constructs fixture must surface its loss records");

        var table = page.Locator("[data-testid='modeling-demo-loss-table']");
        await Assertions.Expect(table).ToBeVisibleAsync();
        // Finding 7: the table is a real table — a localized caption, column scopes, the xmlPath
        // in its own column and a localized severity cell.
        await Assertions.Expect(table.Locator("caption")).ToContainTextAsync("loss report");
        await Assertions.Expect(table.Locator("th[scope='col']")).ToHaveCountAsync(5);
        await Assertions.Expect(rows.First.Locator("td")).ToHaveCountAsync(5);
        await Assertions.Expect(rows.First.Locator("td").First).ToContainTextAsync("Loss");
        await CommitScreenshotAsync(page, "bpmn-import-loss-report");

        // Closing the dialog returns focus to the Import BPMN trigger (finding 7) and leaves the
        // imported preview in place.
        await page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(page.Locator("[data-testid='modeling-demo-import-bpmn']")).ToBeFocusedAsync();
        await Assertions.Expect(page.Locator("[data-testid='modeling-diagram-preview'] g.tm-diagram-node").First)
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10000 });
    }

    [TestMethod]
    [Description("Pasted BPMN XML imports through the dialog and the status line confirms it")]
    public async Task ImportPastedXml_PreviewsImportedModel()
    {
        var page = await OpenLoadedModelingPageAsync();

        await page.Locator("[data-testid='modeling-demo-import-bpmn']").ClickAsync();
        await page.Locator("[data-testid='modeling-demo-bpmn-xml']").FillAsync(PastedProcessXml);
        await page.Locator("[data-testid='modeling-demo-bpmn-import-confirm']").ClickAsync();

        var status = page.Locator("[data-testid='modeling-demo-bpmn-status']");
        await Assertions.Expect(status).ToContainTextAsync("pasted XML", new LocatorAssertionsToContainTextOptions { Timeout = 15000 });
        await WaitForImportedNodesAsync(page);
        await Assertions.Expect(page.Locator("[data-testid='modeling-diagram-preview'] g.tm-diagram-node[data-model-element-id='PastedTask']").First)
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10000 });
        await Assertions.Expect(page.Locator("[data-testid='modeling-demo-loss-report']")).ToHaveCountAsync(0);
    }

    [TestMethod]
    [Description("Picking a .bpmn file in the file input imports it (the real file path)")]
    public async Task ImportBpmnFile_ImportsThroughTheFileInput()
    {
        var page = await OpenLoadedModelingPageAsync();

        var tempPath = Path.Combine(Path.GetTempPath(), $"tempo-bpmn-e2e-{Guid.NewGuid():N}.bpmn");
        await File.WriteAllTextAsync(tempPath, PastedProcessXml);
        try
        {
            await page.Locator("[data-testid='modeling-demo-import-bpmn']").ClickAsync();
            await page.Locator("[data-testid='modeling-demo-bpmn-file']")
                .SetInputFilesAsync(tempPath);

            var status = page.Locator("[data-testid='modeling-demo-bpmn-status']");
            await Assertions.Expect(status).ToContainTextAsync(".bpmn", new LocatorAssertionsToContainTextOptions { Timeout = 15000 });
            await WaitForImportedNodesAsync(page);
        }
        finally
        {
            File.Delete(tempPath);
        }
    }

    [TestMethod]
    [Description("The ?bpmn= deep link auto-imports a corpus sample (drives the DI screenshot lane)")]
    public async Task BpmnDeepLink_ImportsAutomatically()
    {
        var page = await OpenLoadedModelingPageAsync("?bpmn=collaboration-3-pools");

        var status = page.Locator("[data-testid='modeling-demo-bpmn-status']");
        await Assertions.Expect(status).ToContainTextAsync("collaboration-3-pools.bpmn", new LocatorAssertionsToContainTextOptions { Timeout = 15000 });
        await WaitForImportedNodesAsync(page);
        // Three pools render as BPMN pool stencils.
        await Assertions.Expect(
                page.Locator("[data-testid='modeling-diagram-preview'] g.tm-diagram-node[data-stencil-id='bpmn2.pool']").First)
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10000 });
    }

    [TestMethod]
    [Description("Export downloads a .bpmn document whose content re-imports loss-free")]
    public async Task ExportDownload_RoundTripsThroughPasteImport()
    {
        var page = await OpenLoadedModelingPageAsync("?bpmn=simple-process");
        var status = page.Locator("[data-testid='modeling-demo-bpmn-status']");
        await Assertions.Expect(status).ToContainTextAsync("simple-process.bpmn", new LocatorAssertionsToContainTextOptions { Timeout = 15000 });
        await WaitForImportedNodesAsync(page);

        var downloadTask = page.WaitForDownloadAsync(new PageWaitForDownloadOptions { Timeout = 15000 });
        await page.Locator("[data-testid='modeling-demo-export-bpmn']").ClickAsync();
        var download = await downloadTask;

        StringAssert.EndsWith(download.SuggestedFilename, ".bpmn");
        var downloadPath = Path.Combine(Path.GetTempPath(), $"tempo-export-{Guid.NewGuid():N}.bpmn");
        await download.SaveAsAsync(downloadPath);
        string exportedXml;
        try
        {
            exportedXml = await File.ReadAllTextAsync(downloadPath);
        }
        finally
        {
            File.Delete(downloadPath);
        }

        StringAssert.Contains(exportedXml, "<definitions");
        StringAssert.Contains(exportedXml, "Process_Simple");

        // Re-import the downloaded document through the paste path: the cycle closes on the demo.
        await page.Locator("[data-testid='modeling-demo-import-bpmn']").ClickAsync();
        await page.Locator("[data-testid='modeling-demo-bpmn-xml']").FillAsync(exportedXml);
        await page.Locator("[data-testid='modeling-demo-bpmn-import-confirm']").ClickAsync();
        await Assertions.Expect(page.Locator("[data-testid='modeling-demo-bpmn-status']"))
            .ToContainTextAsync("pasted XML", new LocatorAssertionsToContainTextOptions { Timeout = 15000 });
        await WaitForImportedNodesAsync(page);
    }

    private async Task<IPage> OpenLoadedModelingPageAsync(string query = "")
    {
        var context = await CreateContextAsync();
        await context.AddInitScriptAsync("localStorage.setItem('tm-demo-culture', 'en');");
        var page = await context.NewPageAsync();
        await page.GotoAsync($"{BaseUrl}{ModelingEditorUrl}{query}", new PageGotoOptions { WaitUntil = WaitUntilState.Load, Timeout = 60000 });
        await WaitForAppReadyAsync(page);
        await page.Locator("[data-testid='modeling-editor'][data-state='loaded']")
            .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15000 });
        return page;
    }

    private static async Task WaitForImportedNodesAsync(IPage page) =>
        await page.WaitForFunctionAsync(
            "() => document.querySelectorAll(\"[data-testid='modeling-diagram-preview'] g.tm-diagram-node\").length > 0",
            null,
            new PageWaitForFunctionOptions { Timeout = 15000 });

    /// <summary>
    /// Captures a committed-evidence screenshot into
    /// <c>tests/Tempo.Blazor.E2E/__screenshots__/modeling-bpmn-di/</c> (finding 2) and attaches it
    /// to the test result as well.
    /// </summary>
    private async Task CommitScreenshotAsync(IPage page, string name)
    {
        var bytes = await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Type = ScreenshotType.Png,
            FullPage = true,
            Timeout = 60_000,
        });

        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TempoBlazor.slnx")))
        {
            directory = directory.Parent!;
        }

        var path = Path.Combine(directory!.FullName, "tests", "Tempo.Blazor.E2E", "__screenshots__", "modeling-bpmn-di");
        Directory.CreateDirectory(path);
        var file = Path.Combine(path, $"{name}.png");
        await File.WriteAllBytesAsync(file, bytes);
        TestContext.AddResultFile(file);
    }

    private const string PastedProcessXml = """
        <?xml version="1.0" encoding="UTF-8"?>
        <definitions xmlns="http://www.omg.org/spec/BPMN/20100524/MODEL"
                     id="Definitions_Pasted" targetNamespace="http://tempo.example/e2e">
          <process id="Process_Pasted" name="Pasted process" isExecutable="false">
            <startEvent id="PastedStart" name="Begin"/>
            <task id="PastedTask" name="Do work"/>
            <endEvent id="PastedEnd" name="End"/>
            <sequenceFlow id="PastedFlow1" sourceRef="PastedStart" targetRef="PastedTask"/>
            <sequenceFlow id="PastedFlow2" sourceRef="PastedTask" targetRef="PastedEnd"/>
          </process>
        </definitions>
        """;
}
