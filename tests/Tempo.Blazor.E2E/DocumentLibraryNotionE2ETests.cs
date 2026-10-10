using System.Text.Json;
using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Tempo.Blazor.E2E.Mcp;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Phase 3 E2E: inserting an existing library document into a NotionEditor wireframe block
/// (link mode) over the live Demo.Api library, plus a UX screenshot.
/// </summary>
[TestClass]
public class DocumentLibraryNotionE2ETests : NotionE2ETestBase
{
    private async Task InsertWireframeBlockAsync(IPage page)
    {
        await page.EvaluateAsync(@"() => {
            const el = document.querySelector('.tm-notion-paragraph[contenteditable=""true""]');
            if (!el) return;
            el.focus();
            const range = document.createRange();
            range.selectNodeContents(el);
            range.collapse(false);
            const sel = window.getSelection();
            sel.removeAllRanges();
            sel.addRange(range);
        }");
        await page.Keyboard.PressAsync("Enter");
        await page.WaitForTimeoutAsync(800);
        await page.Keyboard.TypeAsync("/");
        await page.WaitForSelectorAsync(".tm-notion-slash",
            new PageWaitForSelectorOptions { State = WaitForSelectorState.Visible, Timeout = 15000 });
        await page.Locator(".tm-notion-slash__input").ClickAsync();
        await page.Keyboard.TypeAsync("wireframe");
        await page.WaitForTimeoutAsync(400);
        await page.Keyboard.PressAsync("Enter");
        await page.WaitForSelectorAsync(".tm-notion-wireframe-block",
            new PageWaitForSelectorOptions { Timeout = 15000 });
    }

    private static async Task SaveScreenshotAsync(IPage page, string fileName, string phase = "phase3")
    {
        var dir = Path.Combine(FindRepoRoot().FullName,
            "tests", "Tempo.Blazor.E2E", "__screenshots__", "document-library", phase);
        Directory.CreateDirectory(dir);
        await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = Path.Combine(dir, fileName), Type = ScreenshotType.Png, FullPage = true
        });
    }

    private static DirectoryInfo FindRepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "TempoBlazor.slnx")))
            {
                return current;
            }
            current = current.Parent!;
        }
        throw new DirectoryNotFoundException("Could not locate TempoBlazor.slnx.");
    }

    [TestMethod]
    public async Task DocLib2_InsertExistingWireframe_Link_RendersPreview()
    {
        var page = await OpenNotionEditorAsync();
        await SeedEmptyPageAsync();
        await InsertWireframeBlockAsync(page);

        // Placeholder offers Insert existing… because the library provider is wired.
        await page.WaitForSelectorAsync(".tm-notion-tempo-block__insert-existing",
            new PageWaitForSelectorOptions { Timeout = 10000 });
        await page.ClickAsync(".tm-notion-tempo-block__insert-existing");

        // Dialog opens; navigate to the seeded Designs folder and pick a document (link mode).
        await page.WaitForSelectorAsync(".tm-document-open-dialog",
            new PageWaitForSelectorOptions { Timeout = 15000 });
        await page.Locator(".tm-dod-tree-node", new() { HasTextString = "Designs" }).First.ClickAsync();
        await page.WaitForSelectorAsync(".tm-dod-row");
        await page.Locator(".tm-dod-row").First.ClickAsync();
        await page.ClickAsync(".tm-dod-open");

        // The block now renders the linked document's preview SVG.
        await page.WaitForSelectorAsync(".tm-notion-wireframe-block__svg-container svg",
            new PageWaitForSelectorOptions { Timeout = 15000 });
        Assert.IsTrue(
            await page.Locator(".tm-notion-wireframe-block__svg-container svg").CountAsync() > 0);

        await SaveScreenshotAsync(page, "01-inserted-wireframe-block.png");
    }

    [TestMethod]
    public async Task DocLib4_RemoteEdit_RefreshesLinkedBlock_WithoutReload()
    {
        var page = await OpenNotionEditorAsync();
        await SeedEmptyPageAsync();
        await InsertWireframeBlockAsync(page);

        await page.WaitForSelectorAsync(".tm-notion-tempo-block__insert-existing",
            new PageWaitForSelectorOptions { Timeout = 10000 });
        await page.ClickAsync(".tm-notion-tempo-block__insert-existing");
        await page.WaitForSelectorAsync(".tm-document-open-dialog");
        await page.Locator(".tm-dod-tree-node", new() { HasTextString = "Designs" }).First.ClickAsync();
        await page.WaitForSelectorAsync(".tm-dod-row");

        // Capture the linked document id, then link it.
        var docId = await page.Locator(".tm-dod-row").First.GetAttributeAsync("data-id");
        Assert.IsFalse(string.IsNullOrEmpty(docId));
        await page.Locator(".tm-dod-row").First.ClickAsync();
        await page.ClickAsync(".tm-dod-open");
        await page.WaitForSelectorAsync(".tm-notion-wireframe-block__svg-container svg",
            new PageWaitForSelectorOptions { Timeout = 15000 });

        // Wait until the block has actually joined the SignalR document group — the SVG renders
        // before the hub join completes, so a PUT right after the selector wait can race the join
        // and the broadcast is lost (group members only get messages sent after they joined).
        await page.WaitForSelectorAsync($".tm-notion-wireframe-block[data-doclib-subscribed='{docId}']",
            new PageWaitForSelectorOptions { Timeout = 15000 });

        // Simulate an edit from elsewhere: PUT a new preview via the API. The store publishes a
        // change → hub broadcasts → the subscribed block refreshes live.
        const string marker = "LIVEv2-marker";
        using var http = new HttpClient(new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (_, _, _, _) => true
        });
        var payload = await http.GetStringAsync($"https://localhost:5100/api/document-library/wireframe/documents/{docId}/payload");
        var saveBody = System.Text.Json.JsonSerializer.Serialize(new
        {
            payloadJson = payload,
            previewSvg = $"<svg xmlns=\"http://www.w3.org/2000/svg\" id=\"{marker}\"><rect width=\"10\" height=\"10\"/></svg>"
        });
        var resp = await http.PutAsync(
            $"https://localhost:5100/api/document-library/wireframe/documents/{docId}/payload",
            new StringContent(saveBody, System.Text.Encoding.UTF8, "application/json"));
        resp.EnsureSuccessStatusCode();

        // The block in this (unreloaded) page should pick up the new preview via SignalR.
        await page.WaitForFunctionAsync(
            $"() => document.querySelector('.tm-notion-wireframe-block__svg-container')?.innerHTML.includes('{marker}')",
            new PageWaitForFunctionOptions { Timeout = 15000 });

        await SaveScreenshotAsync(page, "04-live-refreshed-block.png", "phase4");
    }

    [TestMethod]
    public async Task Mcp4_LiveBridge_McpEdit_RefreshesLinkedBlock_WithoutReload()
    {
        var page = await OpenNotionEditorAsync();
        await SeedEmptyPageAsync();
        await InsertWireframeBlockAsync(page);

        await page.WaitForSelectorAsync(".tm-notion-tempo-block__insert-existing",
            new PageWaitForSelectorOptions { Timeout = 10000 });
        await page.ClickAsync(".tm-notion-tempo-block__insert-existing");
        await page.WaitForSelectorAsync(".tm-document-open-dialog");
        await page.Locator(".tm-dod-tree-node", new() { HasTextString = "Designs" }).First.ClickAsync();
        await page.WaitForSelectorAsync(".tm-dod-row");
        var docId = await page.Locator(".tm-dod-row").First.GetAttributeAsync("data-id");
        Assert.IsFalse(string.IsNullOrEmpty(docId));
        await page.Locator(".tm-dod-row").First.ClickAsync();
        await page.ClickAsync(".tm-dod-open");
        await page.WaitForSelectorAsync(".tm-notion-wireframe-block__svg-container svg",
            new PageWaitForSelectorOptions { Timeout = 15000 });

        // Same join barrier as DocLib4 — the MCP edit must only fire after the block is
        // confirmed inside the SignalR document group.
        await page.WaitForSelectorAsync($".tm-notion-wireframe-block[data-doclib-subscribed='{docId}']",
            new PageWaitForSelectorOptions { Timeout = 15000 });
        await SaveScreenshotAsync(page, "05-mcp-before.png", "phase4");

        // Connect an MCP client (acting as the LLM tooling) and add an element to the linked doc.
        var http = new HttpClient(new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (_, _, _, _) => true
        });
        var mcp = new McpJsonRpcClient(http, new Uri("https://localhost:5100/mcp"));
        await mcp.InitializeAsync();

        var id = Guid.Parse(docId!);
        var doc = await mcp.CallToolAsync("wireframe_get_document", new { documentId = id });
        var beforeCount = doc.GetProperty("document").GetProperty("pages")[0].GetProperty("elements").GetArrayLength();

        var list = await mcp.CallToolAsync("wireframe_list_components", new { compact = true });
        var type = list.GetProperty("items").EnumerateArray().First().GetProperty("type").GetString();
        var ops = JsonSerializer.Serialize(new object[]
        {
            new { op = "addElement", type, x = 24, y = 400, w = 200, h = 48 }
        });
        var applied = await mcp.CallToolAsync("wireframe_apply_operations", new { documentId = id, operationsJson = ops });
        Assert.IsTrue(applied.GetProperty("success").GetBoolean(), applied.GetRawText());

        // The block (still on the same, un-reloaded page) refreshes its preview live via SignalR.
        var expected = beforeCount + 1;
        await page.WaitForFunctionAsync(
            $"() => document.querySelector('.tm-notion-wireframe-block__svg-container')?.innerHTML.includes('data-elements=\"{expected}\"')",
            new PageWaitForFunctionOptions { Timeout = 15000 });

        await SaveScreenshotAsync(page, "06-mcp-after.png", "phase4");
    }

    [TestMethod]
    public async Task DocLibShot2_PlaceholderWithInsertExisting()
    {
        var page = await OpenNotionEditorAsync();
        await SeedEmptyPageAsync();
        await InsertWireframeBlockAsync(page);

        await page.WaitForSelectorAsync(".tm-notion-tempo-block__insert-existing",
            new PageWaitForSelectorOptions { Timeout = 10000 });
        await SaveScreenshotAsync(page, "02-placeholder-create-and-insert.png");

        await page.ClickAsync(".tm-notion-tempo-block__insert-existing");
        await page.WaitForSelectorAsync(".tm-document-open-dialog");
        await SaveScreenshotAsync(page, "03-dialog-from-block.png");
    }
}
