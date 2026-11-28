using System.Text.Json;
using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Regression coverage for the fullscreen toggle: the View-tab button must apply the
/// <c>tm-document-editor--fullscreen</c> body class through the <c>tmDocumentEditor.setFullscreen</c>
/// browser global (broken between 295b7020 and the restore commit — the toggle flipped C# state while
/// routing to a canvas-engine command that never existed). Screenshots land in
/// <c>__screenshots__/document-editor-fullscreen/</c> for UX review.
/// </summary>
[TestClass]
[TestCategory("DocumentEditor")]
[TestCategory("DocumentEditor:CanvasEngine")]
[DoNotParallelize]
public sealed class DocumentEditorFullscreenE2ETests : WasmTestBase
{
    private const string DocumentId = "phase-12-canvas-history-save";
    private const string FullscreenBodyClass = "tm-document-editor--fullscreen";

    [TestMethod]
    public async Task Fullscreen_ToggleAndEscape_AppliesBodyClassScrollLockAndFixedEditor()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(1440, 1000);
        await OpenDocumentAsync(page);

        var fullscreenActivePath = ScreenshotPath("fullscreen-active.png");
        var fullscreenExitedPath = ScreenshotPath("fullscreen-exited.png");

        Assert.IsFalse(await ReadFullscreenBodyStateAsync(page), "The page must not start in fullscreen.");

        await page.GetByTestId("document-ribbon-tab-view").ClickAsync();
        var fullscreenButton = page.GetByTestId("document-fullscreen");
        await Assertions.Expect(fullscreenButton).ToBeVisibleAsync(new() { Timeout = 10_000 });
        await Assertions.Expect(fullscreenButton).ToHaveAttributeAsync("aria-pressed", "false");

        await fullscreenButton.ClickAsync();
        await WaitForFullscreenBodyStateAsync(page, active: true);
        await Assertions.Expect(fullscreenButton).ToHaveAttributeAsync("aria-pressed", "true", new() { Timeout = 10_000 });

        var activeProbe = await ReadFullscreenProbeAsync(page);
        Assert.IsTrue(activeProbe.BodyHasClass, "Entering fullscreen must add the body class the CSS keys off.");
        Assert.AreEqual("hidden", activeProbe.BodyOverflow, "Entering fullscreen must lock body scrolling.");
        Assert.AreEqual("fixed", activeProbe.EditorPosition, "Fullscreen CSS must elevate the editor to position:fixed.");
        Assert.IsTrue(activeProbe.EditorCoversViewport,
            $"The fixed editor must span the whole viewport. Probe: {JsonSerializer.Serialize(activeProbe)}");
        Assert.IsTrue(activeProbe.EditorOpaque,
            $"The fullscreen editor root must have an opaque background (--tm-color-surface-secondary regression). Probe: {JsonSerializer.Serialize(activeProbe)}");

        await page.ScreenshotAsync(new PageScreenshotOptions { Path = fullscreenActivePath, Type = ScreenshotType.Png });

        // Escape must exit fullscreen through the editor's keydown pipeline. Focus the runtime hidden
        // canvas input first: after the toggle re-render the ribbon button may have lost focus, and a
        // body-focused Escape would never reach the editor root handler.
        //
        // Escape peels editor layers topmost-first (Word parity): with the side panel open the FIRST Escape
        // closes the side panel and only the next one exits fullscreen. Assert the layering explicitly instead
        // of hiding it behind a retry. The panel is opened here on purpose: since the readable floor (phase 11)
        // the demo's 1048 px container presents the inspector as a sheet that starts closed.
        var sidePanel = page.GetByTestId("document-side-panel");
        if (await sidePanel.CountAsync() == 0 || !await sidePanel.First.IsVisibleAsync())
        {
            await page.GetByTestId("document-side-panel-edge-toggle").ClickAsync();
        }

        await Assertions.Expect(sidePanel.First).ToBeVisibleAsync(new() { Timeout = 10_000 });
        await FocusHiddenCanvasInputAsync(page);
        await page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(page.GetByTestId("document-side-panel"))
            .Not.ToBeVisibleAsync(new() { Timeout = 10_000 });
        Assert.IsTrue(await ReadFullscreenBodyStateAsync(page),
            "The first Escape must close the topmost layer (side panel), not fullscreen.");

        await FocusHiddenCanvasInputAsync(page);
        await page.Keyboard.PressAsync("Escape");
        await WaitForFullscreenBodyStateAsync(page, active: false);
        await Assertions.Expect(fullscreenButton).ToHaveAttributeAsync("aria-pressed", "false", new() { Timeout = 10_000 });

        var exitedProbe = await ReadFullscreenProbeAsync(page);
        Assert.IsFalse(exitedProbe.BodyHasClass, "Escape must remove the fullscreen body class.");
        Assert.AreNotEqual("hidden", exitedProbe.BodyOverflow, "Escape must restore body scrolling.");
        Assert.AreNotEqual("fixed", exitedProbe.EditorPosition, "Escape must drop the editor back into the page flow.");

        await page.ScreenshotAsync(new PageScreenshotOptions { Path = fullscreenExitedPath, Type = ScreenshotType.Png });

        // Edge case: fullscreen must be re-enterable after an Escape exit (no stale one-shot state).
        await fullscreenButton.ClickAsync();
        await WaitForFullscreenBodyStateAsync(page, active: true);
        await fullscreenButton.ClickAsync();
        await WaitForFullscreenBodyStateAsync(page, active: false);

        TestContext.AddResultFile(fullscreenActivePath);
        TestContext.AddResultFile(fullscreenExitedPath);
    }

    [TestMethod]
    public async Task Fullscreen_NavigateAwayWhileActive_DisposeRemovesBodyClassAndScrollLock()
    {
        // Edge case: the editor disposes while fullscreen is active (user navigates to another page).
        // Without the dispose cleanup the body class and scroll lock would leak onto the next page.
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(1440, 1000);
        await OpenDocumentAsync(page);

        await page.GetByTestId("document-ribbon-tab-view").ClickAsync();
        var fullscreenButton = page.GetByTestId("document-fullscreen");
        await Assertions.Expect(fullscreenButton).ToBeVisibleAsync(new() { Timeout = 10_000 });
        await fullscreenButton.ClickAsync();
        await WaitForFullscreenBodyStateAsync(page, active: true);

        await page.EvaluateAsync(
            """
            () => window.Blazor?.navigateTo
                ? window.Blazor.navigateTo('/')
                : window.location.assign('/')
            """);
        await WaitForFullscreenBodyStateAsync(page, active: false);

        var probe = await ReadFullscreenProbeAsync(page);
        Assert.IsFalse(probe.BodyHasClass, "Disposing the editor must remove the fullscreen body class.");
        Assert.AreNotEqual("hidden", probe.BodyOverflow, "Disposing the editor must restore body scrolling.");

        var leakCheckPath = ScreenshotPath("fullscreen-dispose-cleanup.png");
        await page.ScreenshotAsync(new PageScreenshotOptions { Path = leakCheckPath, Type = ScreenshotType.Png });
        TestContext.AddResultFile(leakCheckPath);
    }

    private async Task OpenDocumentAsync(IPage page)
    {
        await GotoAppReadyAsync(page, $"{BaseUrl}/canvas-engine-host?documentId={DocumentId}&showToolbar=true", WaitUntilState.DOMContentLoaded);
        await WaitForPredicateAsync(page, """
            () => document.querySelector('[data-testid="document-canvas-engine-host"][data-canvas-engine-ready="true"]')
                && document.querySelector('[data-testid="document-ribbon-tab-view"]')
            """, 30_000);
    }

    private static Task<bool> ReadFullscreenBodyStateAsync(IPage page)
        => page.EvaluateAsync<bool>($"() => document.body.classList.contains('{FullscreenBodyClass}')");

    private static Task WaitForFullscreenBodyStateAsync(IPage page, bool active)
        => page.WaitForFunctionAsync(
            $"active => document.body.classList.contains('{FullscreenBodyClass}') === active",
            active,
            new PageWaitForFunctionOptions { Timeout = 10_000 });

    private static Task FocusHiddenCanvasInputAsync(IPage page)
        => page.EvaluateAsync(
            """
            () => {
                const input = document.querySelector('[data-testid="document-canvas-hidden-input"]');
                input?.focus();
            }
            """);

    private static Task<FullscreenProbe> ReadFullscreenProbeAsync(IPage page)
        => page.EvaluateAsync<FullscreenProbe>(
            $$"""
            () => {
                const body = document.body;
                const editor = document.querySelector('.tm-document-editor');
                const editorStyle = editor ? getComputedStyle(editor) : null;
                const rect = editor?.getBoundingClientRect() ?? null;
                // The editor root paints a linear-gradient over --tm-color-surface-secondary. When that
                // token is undefined the whole background declaration is invalid at computed-value time
                // and background-image collapses to 'none' — the exact transparent-editor regression.
                const backgroundImage = editorStyle?.backgroundImage ?? '';
                const backgroundColor = editorStyle?.backgroundColor ?? '';
                const opaque = backgroundImage.includes('gradient')
                    || (backgroundColor !== '' && backgroundColor !== 'transparent' && !/rgba\([^)]*,\s*0\)$/.test(backgroundColor));
                return {
                    bodyHasClass: body.classList.contains('{{FullscreenBodyClass}}'),
                    bodyOverflow: getComputedStyle(body).overflow,
                    editorPosition: editorStyle?.position ?? '',
                    editorCoversViewport: !!rect
                        && Math.abs(rect.left) <= 1
                        && Math.abs(rect.top) <= 1
                        && Math.abs(rect.width - window.innerWidth) <= 1
                        && Math.abs(rect.height - window.innerHeight) <= 1,
                    editorOpaque: opaque
                };
            }
            """);

    private string ScreenshotPath(string fileName)
    {
        var dir = Path.Combine(FindRepoRoot().FullName, "tests", "Tempo.Blazor.E2E", "__screenshots__", "document-editor-fullscreen");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, fileName);
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

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate TempoBlazor.slnx from test output directory.");
    }

    private sealed class FullscreenProbe
    {
        [System.Text.Json.Serialization.JsonPropertyName("bodyHasClass")] public bool BodyHasClass { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("bodyOverflow")] public string BodyOverflow { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("editorPosition")] public string EditorPosition { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("editorCoversViewport")] public bool EditorCoversViewport { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("editorOpaque")] public bool EditorOpaque { get; set; }
    }
}
