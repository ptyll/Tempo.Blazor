using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E.Mcp;

/// <summary>
/// Deterministic MCP E2E against the live Demo.Api at https://localhost:5100/mcp (no LLM).
/// Exercises the wireframe tools end to end over JSON-RPC.
/// </summary>
[TestClass]
public class McpToolsE2ETests
{
    private static readonly Uri Endpoint = new("https://localhost:5100/mcp");

    private static async Task<McpJsonRpcClient> ConnectAsync()
    {
        var http = new HttpClient(DemoApiTestHttp.CreateHandler());
        var client = new McpJsonRpcClient(http, Endpoint);
        await client.InitializeAsync();
        return client;
    }

    private static async Task<string> FirstComponentTypeAsync(McpJsonRpcClient client)
    {
        var list = await client.CallToolAsync("wireframe_list_components", new { compact = true });
        return list.GetProperty("items").EnumerateArray().First().GetProperty("type").GetString()!;
    }

    private static bool HasWarning(JsonElement result, string code, string? elementId = null)
        => result.TryGetProperty("warnings", out var warnings)
           && warnings.ValueKind == JsonValueKind.Array
           && warnings.EnumerateArray().Any(w =>
               w.TryGetProperty("code", out var warningCode)
               && warningCode.GetString() == code
               && (elementId is null
                   || (w.TryGetProperty("elementId", out var warningElementId)
                       && warningElementId.GetString() == elementId)));

    [TestMethod]
    public async Task Mcp1_Initialize_And_ListTools_ExposesAllWireframeTools()
    {
        var client = await ConnectAsync();

        var tools = await client.ListToolsAsync();

        var names = tools.Select(t => t.Name).ToList();
        CollectionAssert.IsSubsetOf(new[]
        {
            "wireframe_list_components", "wireframe_get_component_schema",
            "wireframe_list_documents", "wireframe_get_document", "wireframe_create_document",
            "wireframe_validate_document", "wireframe_apply_operations",
            "wireframe_author_document", "wireframe_replace_document", "wireframe_get_implementation_brief"
        }, names.ToList());
        Assert.IsTrue(tools.All(t => !string.IsNullOrWhiteSpace(t.Description)), "every tool has a description");
    }

    [TestMethod]
    public async Task Mcp2_HappyPath_Build_Read_Validate_Brief()
    {
        var client = await ConnectAsync();
        var type = await FirstComponentTypeAsync(client);

        var created = await client.CallToolAsync("wireframe_create_document", new { title = "Orders dashboard" });
        Assert.IsTrue(created.GetProperty("success").GetBoolean());
        var id = created.GetProperty("id").GetGuid();

        var ops = JsonSerializer.Serialize(new object[]
        {
            new { op = "setTitle", title = "Orders dashboard" },
            new { op = "setCanvasSize", width = 1280, height = 900 },
            new { op = "addElement", type, x = 0, y = 0, w = 1280, h = 64 },
            new { op = "addElement", type, x = 40, y = 120, w = 120, h = 36 },
            new { op = "addElement", type, x = 200, y = 120, w = 120, h = 36 },
            new { op = "addElement", type, x = 40, y = 200, w = 1000, h = 400 }
        });

        var applied = await client.CallToolAsync("wireframe_apply_operations", new { documentId = id, operationsJson = ops });
        Assert.IsTrue(applied.GetProperty("success").GetBoolean(), applied.GetRawText());
        Assert.AreEqual(6, applied.GetProperty("applied").GetInt32());

        var doc = await client.CallToolAsync("wireframe_get_document", new { documentId = id });
        var documentJson = doc.GetProperty("document").GetRawText();

        var validated = await client.CallToolAsync("wireframe_validate_document", new { documentJson });
        Assert.IsTrue(validated.GetProperty("valid").GetBoolean(), validated.GetRawText());

        var brief = await client.CallToolAsync("wireframe_get_implementation_brief", new { documentId = id });
        Assert.IsTrue(brief.GetProperty("success").GetBoolean());
        Assert.IsTrue(brief.GetProperty("pages").GetArrayLength() > 0);
        Assert.IsTrue(brief.GetProperty("componentsUsed").GetArrayLength() > 0);
    }

    [TestMethod]
    public async Task Mcp8_WriteOperations_UpdatesDocumentLibraryThumbnailPreview()
    {
        var client = await ConnectAsync();
        var type = await FirstComponentTypeAsync(client);
        var title = "Thumbnail <script>alert(1)</script><foreignObject>bad</foreignObject> javascript:alert(2) "
                    + Guid.NewGuid().ToString("N")[..6];

        var created = await client.CallToolAsync("wireframe_create_document", new { title });
        Assert.IsTrue(created.GetProperty("success").GetBoolean(), created.GetRawText());
        var id = created.GetProperty("id").GetGuid();

        var operations = new List<object>
        {
            new { op = "setCanvasSize", width = 960, height = 720 }
        };
        for (var i = 0; i < 12; i++)
        {
            operations.Add(new
            {
                op = "addElement",
                id = "thumb-" + i,
                type,
                x = 24 + (i % 4) * 180,
                y = 24 + (i / 4) * 120,
                w = 140,
                h = 80
            });
        }

        var applied = await client.CallToolAsync("wireframe_apply_operations", new
        {
            documentId = id,
            operationsJson = JsonSerializer.Serialize(operations)
        });
        Assert.IsTrue(applied.GetProperty("success").GetBoolean(), applied.GetRawText());

        using var http = new HttpClient(DemoApiTestHttp.CreateHandler());
        using var response = await http.GetAsync($"https://localhost:5100/api/document-library/wireframe/documents/{id}");
        response.EnsureSuccessStatusCode();
        using var metadata = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var svg = metadata.RootElement.GetProperty("previewSvg").GetString();

        Assert.IsFalse(string.IsNullOrWhiteSpace(svg), "expected a cached thumbnail preview");
        StringAssert.StartsWith(svg!, "<svg");
        StringAssert.Contains(svg!, "viewBox=\"0 0 160 120\"");
        StringAssert.Contains(svg!, "width=\"160\"");
        StringAssert.Contains(svg!, "height=\"120\"");
        StringAssert.Contains(svg!, "data-elements=\"12\"");
        Assert.AreEqual(12, svg!.Split("<rect x=\"", StringSplitOptions.None).Length - 1);
        Assert.IsFalse(svg.Contains("script", StringComparison.OrdinalIgnoreCase), svg);
        Assert.IsFalse(svg.Contains("foreignObject", StringComparison.OrdinalIgnoreCase), svg);
        Assert.IsFalse(svg.Contains("javascript:", StringComparison.OrdinalIgnoreCase), svg);
    }

    private static string FixturePath(string name)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "TempoBlazor.slnx")))
        {
            dir = dir.Parent;
        }
        return Path.Combine(dir!.FullName, "tests", "Tempo.Blazor.E2E", "Mcp", "fixtures", name);
    }

    [TestMethod]
    public async Task Mcp5_AgentScenario_Replay_BuildsValidOrdersDashboard()
    {
        // Replays the design an LLM agent produced through the MCP tools (recorded in the fixture),
        // against a fresh /mcp session, and asserts the scenario's acceptance criteria.
        var fixture = JsonDocument.Parse(await File.ReadAllTextAsync(FixturePath("agent-transcript-orders-dashboard.json"))).RootElement;
        var title = fixture.GetProperty("title").GetString();
        var operationsJson = fixture.GetProperty("operations").GetRawText();

        var client = await ConnectAsync();
        var id = (await client.CallToolAsync("wireframe_create_document", new { title }))
            .GetProperty("id").GetGuid();

        var applied = await client.CallToolAsync("wireframe_apply_operations", new { documentId = id, operationsJson });
        Assert.IsTrue(applied.GetProperty("success").GetBoolean(), applied.GetRawText());

        // Criterion 1 — the document validates.
        var doc = await client.CallToolAsync("wireframe_get_document", new { documentId = id });
        var validated = await client.CallToolAsync("wireframe_validate_document",
            new { documentJson = doc.GetProperty("document").GetRawText() });
        Assert.IsTrue(validated.GetProperty("valid").GetBoolean(), validated.GetRawText());

        var brief = await client.CallToolAsync("wireframe_get_implementation_brief", new { documentId = id });
        var page = brief.GetProperty("pages")[0];

        // Criteria 2 & 3 — header, sidebar and content regions exist.
        var regions = page.GetProperty("regions").EnumerateArray()
            .Select(r => r.GetProperty("kind").GetString()).ToList();
        CollectionAssert.IsSubsetOf(new[] { "header", "sidebar", "content" }, regions);

        // Criteria 4 & 6 — >=4 KPI cards and a table + button are used.
        var components = brief.GetProperty("componentsUsed").EnumerateArray()
            .ToDictionary(c => c.GetProperty("type").GetString()!, c => c.GetProperty("count").GetInt32());
        Assert.IsTrue(components.GetValueOrDefault("TmStatCard") >= 4, "expected >=4 KPI cards");
        Assert.IsTrue(components.ContainsKey("TmDataTable"), "expected a data table");
        Assert.IsTrue(components.ContainsKey("TmButton"), "expected action buttons");

        // Criterion 5 — a navigation flow originates from the orders table.
        var flows = page.GetProperty("flows").EnumerateArray().ToList();
        Assert.IsTrue(flows.Any(f => f.TryGetProperty("fromType", out var ft) && ft.GetString() == "TmDataTable"),
            "expected a flow from the orders table");

        // Criterion 7 — at least 10 elements total.
        var totalElements = page.GetProperty("regions").EnumerateArray()
            .Sum(r => r.GetProperty("elements").GetArrayLength());
        Assert.IsTrue(totalElements >= 10, $"expected >=10 elements, got {totalElements}");
    }

    [TestMethod]
    public async Task Mcp6_OverlapLint_SuppressesContainedElementOnlyForContainer()
    {
        var client = await ConnectAsync();

        var containedId = (await client.CallToolAsync(
                "wireframe_create_document",
                new { title = "Contained overlap lint" }))
            .GetProperty("id").GetGuid();
        var containedOps = JsonSerializer.Serialize(new object[]
        {
            new { op = "setCanvasSize", width = 500, height = 400 },
            new { op = "addElement", id = "card", type = "TmCard", x = 20, y = 20, w = 240, h = 160 },
            new
            {
                op = "addElement",
                id = "button",
                type = "TmButton",
                x = 48,
                y = 76,
                w = 120,
                h = 36,
                props = new { label = "Save" }
            }
        });

        var contained = await client.CallToolAsync(
            "wireframe_apply_operations",
            new { documentId = containedId, operationsJson = containedOps });

        Assert.IsTrue(contained.GetProperty("success").GetBoolean(), contained.GetRawText());
        Assert.IsFalse(HasWarning(contained, "overlap"), contained.GetRawText());

        var partialId = (await client.CallToolAsync(
                "wireframe_create_document",
                new { title = "Partial overlap lint" }))
            .GetProperty("id").GetGuid();
        var partialOps = JsonSerializer.Serialize(new object[]
        {
            new { op = "setCanvasSize", width = 500, height = 400 },
            new { op = "addElement", id = "card", type = "TmCard", x = 20, y = 20, w = 160, h = 100 },
            new
            {
                op = "addElement",
                id = "button",
                type = "TmButton",
                x = 150,
                y = 80,
                w = 120,
                h = 36,
                props = new { label = "Save" }
            }
        });

        var partial = await client.CallToolAsync(
            "wireframe_apply_operations",
            new { documentId = partialId, operationsJson = partialOps });

        Assert.IsTrue(partial.GetProperty("success").GetBoolean(), partial.GetRawText());
        Assert.IsTrue(HasWarning(partial, "overlap", "card"), partial.GetRawText());
        Assert.IsTrue(HasWarning(partial, "overlap", "button"), partial.GetRawText());
    }

    [TestMethod]
    public async Task Mcp3_ErrorPaths_InvalidOps_UnknownType_Conflict()
    {
        var client = await ConnectAsync();
        var type = await FirstComponentTypeAsync(client);
        var id = (await client.CallToolAsync("wireframe_create_document", new { title = "Errors" }))
            .GetProperty("id").GetGuid();

        // Invalid operations JSON.
        var badJson = await client.CallToolAsync("wireframe_apply_operations",
            new { documentId = id, operationsJson = "{not an array" });
        Assert.IsFalse(badJson.GetProperty("success").GetBoolean());
        Assert.AreEqual("validation_failed", badJson.GetProperty("error").GetString());

        // Unknown component type → validation error with a did-you-mean suggestion.
        var unknownOps = JsonSerializer.Serialize(new object[]
        {
            new { op = "addElement", type = type + "X", w = 100, h = 40 }
        });
        var unknown = await client.CallToolAsync("wireframe_apply_operations",
            new { documentId = id, operationsJson = unknownOps });
        Assert.AreEqual("validation_failed", unknown.GetProperty("error").GetString());
        var errors = unknown.GetProperty("validationErrors").EnumerateArray().Select(e => e.GetString()).ToList();
        Assert.IsTrue(errors.Any(e => e!.Contains("Did you mean") && e.Contains(type)), string.Join(" | ", errors));

        // Optimistic concurrency: read the token, mutate once, then write again with the stale token.
        var staleToken = (await client.CallToolAsync("wireframe_get_document", new { documentId = id }))
            .GetProperty("modifiedAt").GetDateTime();
        var goodOps = JsonSerializer.Serialize(new object[]
        {
            new { op = "addElement", type, x = 0, y = 0, w = 100, h = 40 }
        });
        await client.CallToolAsync("wireframe_apply_operations", new { documentId = id, operationsJson = goodOps });

        var conflict = await client.CallToolAsync("wireframe_apply_operations",
            new { documentId = id, operationsJson = goodOps, expectedModifiedAt = staleToken });
        Assert.AreEqual("conflict", conflict.GetProperty("error").GetString(), conflict.GetRawText());
    }
}
