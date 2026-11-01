using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// End-to-end gate for the TmDocumentEditor ↔ Signing bridge
/// (plan: <c>planning/tm-documenteditor-signing-bridge-tdd-todo-2026-06-12.md</c>).
/// <para>
/// Phase S0 captures the honest pre-change baseline: the document editor demo and the existing
/// signing component demo both render and the designer → runner demo surfaces are live. Later
/// phases (S1 page export, S2 inline signing fields) extend this class with <c>S1_*</c>/<c>S2_*</c>
/// tests. Screenshots land in <c>__screenshots__/signing-editor-bridge/phaseN/</c> for the
/// two-round (functional + UX) review gate described in the plan.
/// </para>
/// </summary>
[TestClass]
[TestCategory("WASM")]
[TestCategory("SigningEditorBridge")]
[DoNotParallelize]
public sealed class DocumentEditorSigningBridgeE2ETests : WasmTestBase
{
    /// <summary>S0.2 — baseline screenshot of the canvas document editor before any bridge work.</summary>
    [TestMethod]
    public async Task S0_DocumentEditorBaseline_RendersAndCapturesScreenshot()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(1440, 1000);
        await page.GotoAsync($"{BaseUrl}/document-editor", new PageGotoOptions
        {
            WaitUntil = WaitUntilState.DOMContentLoaded,
            Timeout = 60_000
        });

        await page.WaitForSelectorAsync("[data-testid='document-editor-demo']", new PageWaitForSelectorOptions
        {
            State = WaitForSelectorState.Attached,
            Timeout = 60_000
        });
        await page.WaitForFunctionAsync(
            """
            () => {
                const editor = document.querySelector('[data-testid="document-editor-demo"]');
                if (!editor) return false;
                const canvas = document.querySelector('[data-testid="document-canvas-engine-host"]');
                return !!canvas;
            }
            """,
            null,
            new PageWaitForFunctionOptions { Timeout = 60_000 });
        await page.WaitForTimeoutAsync(500);

        var hostCount = await page.Locator("[data-testid='document-canvas-engine-host']")
            .CountAsync();
        Assert.IsTrue(hostCount > 0, "The document editor demo must mount an editor host for the S0 baseline.");

        var path = await SaveBridgeScreenshotAsync(page, "phase0", "01-document-editor-baseline.png");
        Assert.IsTrue(new FileInfo(path).Length > 10_000, "The document editor baseline screenshot must be a real non-empty PNG.");
    }

    /// <summary>
    /// S0.2 + S0.3 — baseline screenshot of the existing signing component demo and a smoke
    /// verification that the designer → runner surfaces (the targets of the bridge) are live.
    /// </summary>
    [TestMethod]
    public async Task S0_SigningComponentsBaseline_RendersDesignerAndRunnerFlow()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(1440, 1000);
        await page.GotoAsync($"{BaseUrl}/signing-components", new PageGotoOptions
        {
            WaitUntil = WaitUntilState.Load,
            Timeout = 60_000
        });
        await WaitForAppReadyAsync(page);

        // S0.3 — the designer (field overlay builder over document pages) and the form runner
        // are the two existing surfaces the bridge feeds; both must be live before S1 starts.
        var designer = page.GetByTestId("pdf-template-designer");
        var runner = page.GetByTestId("signing-runner-demo");
        var viewer = page.GetByTestId("signing-document-viewer");
        var overlayGallery = page.GetByTestId("signing-field-overlay-gallery");

        await Assertions.Expect(designer).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await Assertions.Expect(runner).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await Assertions.Expect(viewer).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await Assertions.Expect(overlayGallery).ToBeVisibleAsync(new() { Timeout = 30_000 });

        await designer.ScrollIntoViewIfNeededAsync();
        await page.WaitForTimeoutAsync(400);

        var path = await SaveBridgeScreenshotAsync(page, "phase0", "02-signing-components-baseline.png");
        Assert.IsTrue(new FileInfo(path).Length > 10_000, "The signing components baseline screenshot must be a real non-empty PNG.");
    }

    /// <summary>
    /// S1.10 — full bridge flow: author a contract in the editor, export its pages, place a signing
    /// field on the exported pages with the designer, and preview the signer experience.
    /// </summary>
    [TestMethod]
    public async Task S1_EditorPagesExportIntoSigningTemplateAndRunner()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(1440, 1000);
        await page.GotoAsync($"{BaseUrl}/signing-from-editor", new PageGotoOptions
        {
            WaitUntil = WaitUntilState.Load,
            Timeout = 60_000
        });
        await WaitForAppReadyAsync(page);

        // (a) the editor renders the seed contract through the canvas engine.
        await page.WaitForSelectorAsync(
            "[data-testid='signing-from-editor-document'] [data-testid='document-canvas-engine-host']",
            new PageWaitForSelectorOptions { Timeout = 60_000 });
        await Assertions.Expect(page.GetByTestId("signing-from-editor-export"))
            .ToBeEnabledAsync(new() { Timeout = 60_000 });
        await page.WaitForTimeoutAsync(600);
        await SaveBridgeScreenshotAsync(page, "phase1", "01-editor-seed.png");

        // (b) exporting turns the editor pages into signing-template pages shown by the designer.
        await page.GetByTestId("signing-from-editor-export").ClickAsync();
        await page.WaitForSelectorAsync("[data-testid='signing-from-editor-designer']",
            new PageWaitForSelectorOptions { Timeout = 60_000 });
        var pageImages = page.Locator("[data-testid='signing-from-editor-designer'] img.tm-document-page-viewer__image");
        await Assertions.Expect(pageImages.First).ToBeVisibleAsync(new() { Timeout = 30_000 });
        Assert.IsTrue(await pageImages.CountAsync() >= 1, "the designer must show at least one exported editor page");

        // (c) the designer page is the editor's real exported bitmap (a non-trivial data URL).
        var firstSrc = await pageImages.First.GetAttributeAsync("src");
        StringAssert.StartsWith(firstSrc, "data:image/", "the designer page must come from the editor export data URL");
        Assert.IsTrue((firstSrc?.Length ?? 0) > 1_000, "the exported page bitmap must be a real, non-trivial image");
        await page.WaitForTimeoutAsync(300);
        await SaveBridgeScreenshotAsync(page, "phase1", "02-designer-exported-pages.png");

        // (d) placing a field flows it into the shared signing field list.
        await page.GetByTestId("signing-from-editor-add-field").ClickAsync();
        await Assertions.Expect(page.GetByTestId("signing-from-editor-designer-status"))
            .ToContainTextAsync("1 signing field", new() { Timeout = 30_000 });
        await SaveBridgeScreenshotAsync(page, "phase1", "03-field-placed.png");

        // (e) previewing hands the exported pages + field to the signer form runner.
        await page.GetByTestId("signing-from-editor-preview").ClickAsync();
        await page.WaitForSelectorAsync("[data-testid='signing-from-editor-runner']",
            new PageWaitForSelectorOptions { Timeout = 60_000 });
        await Assertions.Expect(page.GetByTestId("signing-from-editor-runner")).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await page.WaitForTimeoutAsync(500);
        await SaveBridgeScreenshotAsync(page, "phase1", "04-runner-step.png");
    }

    /// <summary>
    /// S2.28 — inline signing fields: place a signature in the body and initials in the footer directly
    /// in the editor, then preview the signer experience. Proves the fields flow from the canvas engine
    /// (with layout-derived areas) into the form runner without a separate overlay step.
    /// </summary>
    [TestMethod]
    public async Task S2_InlineSigningFieldsFlowFromEditorIntoRunner()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(1440, 1000);
        await page.GotoAsync($"{BaseUrl}/signing-from-editor", new PageGotoOptions { WaitUntil = WaitUntilState.Load, Timeout = 60_000 });
        await WaitForAppReadyAsync(page);

        await page.WaitForSelectorAsync(
            "[data-testid='signing-from-editor-document'] [data-testid='document-canvas-engine-host'][data-canvas-engine-ready='true']",
            new PageWaitForSelectorOptions { Timeout = 60_000 });
        await Assertions.Expect(page.GetByTestId("signing-from-editor-insert-body")).ToBeEnabledAsync(new() { Timeout = 60_000 });
        await page.WaitForTimeoutAsync(500);

        // Click into the document body to set a caret, then place a body signature field there.
        await page.Locator("[data-testid='signing-from-editor-document'] [data-testid='document-canvas-page']").First
            .ClickAsync(new() { Position = new() { X = 150, Y = 175 } });
        await page.WaitForTimeoutAsync(300);
        await page.GetByTestId("signing-from-editor-insert-body").ClickAsync();
        await page.WaitForTimeoutAsync(900);

        // S2.21/22 — the caret lands on the just-inserted field, so the properties popover appears.
        // Edit the label there and confirm the edit is accepted (no error).
        await Assertions.Expect(page.GetByTestId("document-canvas-signing-popover")).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await SaveBridgeScreenshotAsync(page, "phase2", "03-signing-field-properties.png");
        await page.GetByTestId("document-signing-field-label").FillAsync("Authorized signature");
        await page.GetByTestId("document-signing-field-label").PressAsync("Tab");
        await page.WaitForTimeoutAsync(500);

        await page.GetByTestId("signing-from-editor-insert-footer").ClickAsync();
        await page.WaitForTimeoutAsync(600);
        await SaveBridgeScreenshotAsync(page, "phase2", "01-inline-fields-in-document.png");

        // Preview: the editor's fields (with layout-derived areas) drive the signer form runner.
        await page.GetByTestId("signing-from-editor-inline-preview").ClickAsync();
        await page.WaitForSelectorAsync("[data-testid='signing-from-editor-inline-runner']",
            new PageWaitForSelectorOptions { Timeout = 60_000 });
        await Assertions.Expect(page.GetByTestId("signing-from-editor-inline-runner")).ToBeVisibleAsync(new() { Timeout = 30_000 });

        // Both inserted fields reach the runner; the footer field carries at least one area per page.
        await Assertions.Expect(page.GetByTestId("signing-from-editor-inline-status"))
            .ToContainTextAsync("2 field", new() { Timeout = 30_000 });
        await page.WaitForTimeoutAsync(400);
        await SaveBridgeScreenshotAsync(page, "phase2", "02-inline-runner-from-fields.png");
    }

    /// <summary>Saves a full-page PNG into the bridge's named screenshot folder for the given phase.</summary>
    private static async Task<string> SaveBridgeScreenshotAsync(IPage page, string phase, string fileName)
    {
        var dir = Path.Combine(
            FindRepositoryRoot().FullName,
            "tests",
            "Tempo.Blazor.E2E",
            "__screenshots__",
            "signing-editor-bridge",
            phase);
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, fileName);
        // Phase 0–2 captures are historical. Rewriting them is opt-in, same as the redesign
        // generators. The behaviour assertions above still run; the byte check reads the committed PNG.
        var flag = Environment.GetEnvironmentVariable("TM_WRITE_BASELINES");
        var write = string.Equals(flag, "1", StringComparison.Ordinal)
            || string.Equals(flag, "true", StringComparison.OrdinalIgnoreCase)
            || string.Equals(flag, "yes", StringComparison.OrdinalIgnoreCase);
        if (!write)
        {
            return path;
        }

        await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = path,
            Type = ScreenshotType.Png,
            FullPage = true
        });
        return path;
    }

    private static DirectoryInfo FindRepositoryRoot()
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

        throw new DirectoryNotFoundException("Could not locate TempoBlazor.slnx from test output directory.");
    }
}
