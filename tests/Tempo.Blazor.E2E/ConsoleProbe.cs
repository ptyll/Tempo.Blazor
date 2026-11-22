using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

[TestClass]
public class ConsoleProbe : WasmTestBase
{
    [TestMethod]
    public async Task PrintConsoleLogs()
    {
        var page = await CreatePageAsync();
        var logs = new List<string>();
        page.Console += (_, msg) => logs.Add($"[{msg.Type}] {msg.Text}");
        await GotoAppReadyAsync(page, BaseUrl + "/diagram-editor");
        await page.WaitForTimeoutAsync(5000);
        foreach (var log in logs) TestContext.WriteLine(log);
        var html = await page.ContentAsync();
        if (html.Contains("blazor-error-ui"))
            TestContext.WriteLine("ERROR UI DETECTED");
        Assert.IsTrue(html.Contains("tm-diagram-node"), "Nodes should be rendered");
    }
}
