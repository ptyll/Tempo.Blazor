using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Faze T (T4) E2E tests for the headless server-side wireframe renderer (IWireframeSvgRenderer),
/// exercised over real HTTP against the Demo.Api host via the /api/wireframe/preview endpoints.
/// Verifies multi-page order, the empty-page placeholder, the unknown-component fallback, element
/// counts, and captures a browser screenshot baseline of a server-rendered SVG.
/// </summary>
[TestClass]
[TestCategory("WASM")]
[TestCategory("Wireframe")]
public class WireframeServerPreviewE2ETests : WasmTestBase
{
    private const string ApiBaseUrl = "https://localhost:5100";

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static async Task<string> ApiGetAsync(string path)
    {
        using var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
        };
        using var http = new HttpClient(handler) { BaseAddress = new Uri(ApiBaseUrl) };

        using var response = await http.GetAsync(path);
        if (response.StatusCode == HttpStatusCode.NotFound)
            Assert.Fail($"Endpoint not found: {path}. Is MapWireframePreviewEndpoints registered in Demo.Api?");
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync();
    }

    /// <summary>Case-insensitive property access (robust to camelCase/PascalCase JSON policy).</summary>
    private static JsonElement Prop(JsonElement element, string name)
    {
        foreach (var p in element.EnumerateObject())
            if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
                return p.Value;
        throw new KeyNotFoundException($"Property '{name}' not found.");
    }

    private static string Svg(JsonElement page) => Prop(page, "svg").GetString()!;

    // ── Multi-page order ─────────────────────────────────────────────────────────

    [TestMethod]
    public async Task RenderJson_MultiPage_ReturnsEveryPageInDocumentOrder()
    {
        var json = await ApiGetAsync("/api/wireframe/preview/render.json?scenario=multipage");
        using var doc = JsonDocument.Parse(json);
        var pages = doc.RootElement;

        Assert.AreEqual(3, pages.GetArrayLength());
        Prop(pages[0], "name").GetString().Should().Be("Home");
        Prop(pages[1], "name").GetString().Should().Be("Details");
        Prop(pages[2], "name").GetString().Should().Be("Settings");

        foreach (var page in pages.EnumerateArray())
            Svg(page).Should().StartWith("<svg");

        Svg(pages[1]).Should().Contain("viewBox=\"0 0 1024 768\"");   // page keeps its own dimensions
    }

    // ── Empty page placeholder ───────────────────────────────────────────────────

    [TestMethod]
    public async Task RenderJson_EmptyPage_ReturnsSizedSvgWithName()
    {
        var json = await ApiGetAsync("/api/wireframe/preview/render.json?scenario=empty");
        using var doc = JsonDocument.Parse(json);
        var svg = Svg(doc.RootElement[0]);

        svg.Should().StartWith("<svg");
        svg.Should().Contain("viewBox=\"0 0 500 400\"");
        svg.Should().Contain("Blank Screen");   // visible placeholder, never a blank box
    }

    // ── Unknown component fallback ───────────────────────────────────────────────

    [TestMethod]
    public async Task RenderJson_UnknownComponent_ReturnsFallbackNotError()
    {
        var json = await ApiGetAsync("/api/wireframe/preview/render.json?scenario=unknown");
        using var doc = JsonDocument.Parse(json);
        var svg = Svg(doc.RootElement[0]);

        svg.Should().Contain("stroke-dasharray");   // dashed placeholder box
        svg.Should().Contain("GhostWidget");         // the missing type is shown
    }

    // ── Element / connector counts match the document ────────────────────────────

    [TestMethod]
    public async Task RenderJson_Connectors_ElementCountMatchesDocument()
    {
        var json = await ApiGetAsync("/api/wireframe/preview/render.json?scenario=connectors");
        using var doc = JsonDocument.Parse(json);
        var svg = Svg(doc.RootElement[0]);

        Regex.Matches(svg, "data-el-id=").Count.Should().Be(2);   // two elements
        svg.Should().Contain("next");                              // connector label rendered
    }

    // ── Browser screenshot baseline of a server-rendered SVG ─────────────────────

    [TestMethod]
    public async Task RenderSvg_RendersInBrowser_AndCapturesBaseline()
    {
        // Fetch the server-rendered SVG over HTTP, then render it inline in the browser. Inlining
        // (rather than navigating to the HTTPS endpoint) keeps the screenshot independent of the
        // browser's dev-cert trust for the Demo.Api origin.
        var svgMarkup = await ApiGetAsync("/api/wireframe/preview/render.svg?scenario=multipage&page=0");
        svgMarkup.Should().StartWith("<svg");

        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        await page.SetContentAsync($"<!doctype html><html><body style=\"margin:0\">{svgMarkup}</body></html>");

        var svg = page.Locator("svg").First;
        await svg.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15000 });

        var path = BaselinePath("wireframe-server-preview", "multipage-home");
        await page.ScreenshotAsync(new PageScreenshotOptions { Path = path, Type = ScreenshotType.Png, FullPage = true });

        File.Exists(path).Should().BeTrue();
    }

    private static string BaselinePath(string area, string state)
    {
        var dir = BaselineOutput.DirectoryFor(null, area);
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, $"{state}.png");
    }
}
