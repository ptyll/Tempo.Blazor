using System.Text.Json;
using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Tempo.Blazor.E2E.Mcp;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Phase 7.5 — builds the orders-dashboard wireframe through the MCP tools (as the agent did),
/// then opens it in the live wireframe editor and captures screenshots for the UX#4 review.
/// </summary>
[TestClass]
public class McpAgentScreenshotE2ETests : WasmTestBase
{
    private static async Task<(McpJsonRpcClient Client, string Title)> BuildDashboardAsync()
    {
        var fixturePath = FindFixture("agent-transcript-orders-dashboard.json");
        var fixture = JsonDocument.Parse(await File.ReadAllTextAsync(fixturePath)).RootElement;
        var operationsJson = fixture.GetProperty("operations").GetRawText();

        // Unique title so the editor's open dialog can find this exact run.
        var title = "Správa objednávek " + Guid.NewGuid().ToString("N")[..6];

        var http = new HttpClient(DemoApiTestHttp.CreateHandler());
        var client = new McpJsonRpcClient(http, new Uri("https://localhost:5100/mcp"));
        await client.InitializeAsync();
        var id = (await client.CallToolAsync("wireframe_create_document", new { title })).GetProperty("id").GetGuid();
        // The fixture's setTitle would rename it; drop that op so the unique title survives.
        var ops = JsonSerializer.Deserialize<List<JsonElement>>(operationsJson)!
            .Where(o => o.GetProperty("op").GetString() != "setTitle").ToList();
        await client.CallToolAsync("wireframe_apply_operations",
            new { documentId = id, operationsJson = JsonSerializer.Serialize(ops) });
        return (client, title);
    }

    private static string FindFixture(string name)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "TempoBlazor.slnx")))
        {
            dir = dir.Parent;
        }
        return Path.Combine(dir!.FullName, "tests", "Tempo.Blazor.E2E", "Mcp", "fixtures", name);
    }

    private static async Task SaveAsync(IPage page, string fileName)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "TempoBlazor.slnx")))
        {
            root = root.Parent;
        }
        var outDir = Path.Combine(root!.FullName, "tests", "Tempo.Blazor.E2E", "__screenshots__", "document-library", "phase7");
        Directory.CreateDirectory(outDir);
        await page.ScreenshotAsync(new PageScreenshotOptions { Path = Path.Combine(outDir, fileName), Type = ScreenshotType.Png, FullPage = true });
    }

    [TestMethod]
    public async Task Mcp7_AgentDesign_OpensInWireframeEditor_Screenshot()
    {
        var (_, title) = await BuildDashboardAsync();

        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(1600, 1000);
        await page.GotoAsync($"{BaseUrl}/wireframe-editor", new PageGotoOptions { WaitUntil = WaitUntilState.Load, Timeout = 60000 });
        await WaitForAppReadyAsync(page);

        await page.ClickAsync("[data-testid=wf-open-btn]");
        await page.WaitForSelectorAsync(".tm-document-open-dialog", new PageWaitForSelectorOptions { Timeout = 15000 });

        // The MCP-created design lives at the library root; search for this run's unique title.
        await page.FillAsync(".tm-dod-search", "Správa objednávek");
        await page.WaitForSelectorAsync($".tm-dod-row[data-name='{title}']", new PageWaitForSelectorOptions { Timeout = 15000 });
        await page.Locator($".tm-dod-row[data-name='{title}']").First.DblClickAsync();

        // The editor renders the designed components (header, sidebar, KPI cards, table, buttons).
        await page.WaitForSelectorAsync(".tm-wd-canvas__svg", new PageWaitForSelectorOptions { Timeout = 20000 });
        await page.WaitForTimeoutAsync(1500);
        await SaveAsync(page, "01-orders-dashboard-in-editor.png");

        // Sanity: the canvas SVG rendered a non-trivial number of element nodes.
        var nodeCount = await page.Locator(".tm-wd-canvas__svg [data-el-id], .tm-wd-canvas__svg g").CountAsync();
        Assert.IsTrue(nodeCount > 0, "expected rendered wireframe elements on the canvas");
    }
}
