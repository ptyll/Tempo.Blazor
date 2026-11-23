using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// PR-gate smoke lane (run with <c>scripts/run-e2e-smoke.ps1</c> or
/// <c>dotnet test --filter TestCategory=Smoke</c>).
/// Boots the WASM demo host and verifies the most important demo surfaces
/// render without unhandled Blazor exceptions. Keep this lane small and
/// deterministic (&lt; 20 minutes wall clock including host startup) — the
/// exhaustive coverage lives in the nightly full lane
/// (<c>scripts/run-e2e-full.ps1</c>, no filter).
/// </summary>
[TestClass]
[TestCategory("Smoke")]
public sealed class SmokeLaneE2ETests : WasmTestBase
{
    [TestMethod]
    [DataRow("/", "main", DisplayName = "Home")]
    [DataRow("/document-editor", "[data-testid='document-editor-contract']", DisplayName = "DocumentEditor")]
    [DataRow("/charts", "[data-testid='chart-donut-custom']", DisplayName = "Charts")]
    [DataRow("/data-table", "[data-testid='dt-ergonomics-section']", DisplayName = "DataTable")]
    [DataRow("/notion-editor", "[data-testid='notion-single-page-demo']", DisplayName = "NotionEditor")]
    [DataRow("/pdf-viewer", ".tm-pdf-viewer__canvas", DisplayName = "PdfViewer")]
    [DataRow("/kanban", ".tm-kanban", DisplayName = "Kanban")]
    [DataRow("/diagram-editor", ".tm-diagram-node", DisplayName = "DiagramEditor")]
    [DataRow("/forms", "[data-testid='decimal-input-section']", DisplayName = "Forms")]
    public async Task Smoke_DemoPage_RendersWithoutUnhandledExceptions(string route, string readySelector)
    {
        var (page, errors) = await OpenPageWithErrorCaptureAsync(route);

        await Assertions.Expect(page.Locator(readySelector).First)
            .ToBeVisibleAsync(new() { Timeout = 30_000 });

        await AssertPageHealthyAsync(page, errors, route);
    }

    [TestMethod]
    public async Task Smoke_CanvasEngineHost_BecomesReadyAndRendersContent()
    {
        var (page, errors) = await OpenPageWithErrorCaptureAsync(
            "/canvas-engine-host?documentId=phase-5-canvas-render&showToolbar=true");

        await WaitForPredicateAsync(page, """
            () => document.querySelector('[data-testid="document-canvas-engine-host"][data-canvas-engine-ready="true"]')
                && document.querySelectorAll('[data-testid="document-canvas-page"]').length >= 1
            """, 45_000);

        var commandCount = await page.EvaluateAsync<int>(
            """
            () => Array.from(document.querySelectorAll('[data-testid="document-canvas-page"]'))
                .reduce((total, p) => total + Number(p.getAttribute('data-canvas-render-command-count') || '0'), 0)
            """);
        Assert.IsTrue(commandCount > 0, "Canvas engine must produce render commands for the seeded document.");

        await AssertPageHealthyAsync(page, errors, "/canvas-engine-host");
    }

    /// <summary>
    /// N193: the boot gate used to end with an unconditional 1000 ms sleep, so every
    /// page open paid a full second of dead time. ReloadAppReadyAsync must add well under
    /// 1000 ms over a plain reload + ready-attribute wait — a fixed sleep would land at
    /// ≥1000 ms of overhead every time.
    /// </summary>
    [TestMethod]
    public async Task Smoke_AppReady_OnReadyPage_ReturnsBelowOldFixedSleep()
    {
        var (page, errors) = await OpenPageWithErrorCaptureAsync("/");

        var plain = System.Diagnostics.Stopwatch.StartNew();
        await page.ReloadAsync(new PageReloadOptions { WaitUntil = WaitUntilState.Load });
        await page.Locator("body[data-blazor-ready='true']").WaitForAsync();
        plain.Stop();
        var gated = System.Diagnostics.Stopwatch.StartNew();
        await ReloadAppReadyAsync(page);
        gated.Stop();
        var overhead = gated.ElapsedMilliseconds - plain.ElapsedMilliseconds;
        TestContext.WriteLine($"boot gate overhead vs plain reload: {overhead} ms (plain {plain.ElapsedMilliseconds} ms, gated {gated.ElapsedMilliseconds} ms)");
        Assert.IsTrue(overhead < 1000, $"boot gate added {overhead} ms over a plain reload — the old fixed 1000 ms sleep (or another fixed wait) is back");
        await AssertPageHealthyAsync(page, errors, "/");
    }

    /// <summary>Edge case: an unknown route must render the not-found surface, not crash the app.</summary>
    [TestMethod]
    public async Task Smoke_UnknownRoute_RendersNotFoundWithoutCrash()
    {
        var (page, errors) = await OpenPageWithErrorCaptureAsync("/this-route-does-not-exist-smoke-probe");

        await WaitForPredicateAsync(page, "() => (document.body?.textContent || '').trim().length > 0", 30_000);

        await AssertPageHealthyAsync(page, errors, "/this-route-does-not-exist-smoke-probe");
    }

    private async Task<(IPage Page, List<string> Errors)> OpenPageWithErrorCaptureAsync(string route)
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        var errors = new List<string>();
        page.PageError += (_, message) => errors.Add($"pageerror: {message}");
        page.Console += (_, msg) =>
        {
            if (msg.Type == "error" && msg.Text.Contains("Unhandled exception", StringComparison.OrdinalIgnoreCase))
            {
                errors.Add($"console.error: {msg.Text}");
            }
        };

        await GotoAppReadyAsync(page, $"{BaseUrl}{route}", WaitUntilState.DOMContentLoaded);
        return (page, errors);
    }

    private async Task AssertPageHealthyAsync(IPage page, List<string> errors, string route)
    {
        var errorUiVisible = await page.EvaluateAsync<bool>(
            """
            () => {
                const el = document.querySelector('#blazor-error-ui');
                return !!el && getComputedStyle(el).display !== 'none';
            }
            """);
        Assert.IsFalse(errorUiVisible, $"#blazor-error-ui is visible on {route} — an unhandled Blazor exception occurred.");
        Assert.AreEqual(0, errors.Count, $"Unexpected page errors on {route}:{Environment.NewLine}{string.Join(Environment.NewLine, errors)}");
    }
}
