using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Signing redesign phase 1 — Signing components respond to their CONTAINER, not the viewport.
/// <para>
/// The discriminating case: on a wide (1440 px) DESKTOP viewport a Signing panel squeezed into a
/// ~300 px column must collapse exactly like on a phone. The pre-container-query stylesheets keyed
/// these layouts off <c>@media (max-width: …)</c>, so a narrow designer inspector on a wide screen
/// kept the roomy desktop layout; with <c>container-type: inline-size</c> on the panel and the
/// builders (plus the 640/768/1024/1280 container breakpoints), the same panel re-wraps by the
/// width it actually gets.
/// </para>
/// </summary>
[TestClass]
public class SigningContainerQueryE2ETests : WasmTestBase
{
    [TestMethod]
    [Description("Field editor panel in a 300px container on a 1440px viewport collapses its grid to one column")]
    public async Task FieldEditorPanel_NarrowContainer_CollapsesGridOnWideViewport()
    {
        var page = await OpenSigningComponentsAsync();

        await page.EvaluateAsync("""
            () => {
                const panel = document.querySelector("[data-testid='field-editor-panel']");
                if (!panel) throw new Error('field editor panel not found');
                panel.style.width = '300px';
            }
            """);
        await page.WaitForTimeoutAsync(200);

        var tracks = await page.EvaluateAsync<string>(
            "() => getComputedStyle(document.querySelector('.tm-signing-field-editor-panel__grid')).gridTemplateColumns");

        Assert.IsFalse(tracks.Contains(' '),
            $"expected the field editor grid to collapse to a single column inside a 300px container " +
            $"(on a 1440px viewport), but computed grid-template-columns was '{tracks}'");
    }

    [TestMethod]
    [Description("Condition builder embedded in the inspector wraps to one column on a wide viewport")]
    public async Task ConditionBuilder_InFieldEditorInspector_WrapsByContainerWidth()
    {
        var page = await OpenSigningComponentsAsync();

        await page.EvaluateAsync("""
            () => {
                const panel = document.querySelector("[data-testid='field-editor-panel']");
                if (!panel) throw new Error('field editor panel not found');
                panel.style.width = '300px';
            }
            """);
        await page.Locator(".tm-signing-field-editor-panel__open-conditions").ClickAsync();
        await page.WaitForSelectorAsync(
            ".tm-signing-field-editor-panel__condition-builder .tm-condition-builder__row",
            new PageWaitForSelectorOptions { State = WaitForSelectorState.Visible, Timeout = 10_000 });
        await page.WaitForTimeoutAsync(200);

        var tracks = await page.EvaluateAsync<string>("""
            () => getComputedStyle(
                document.querySelector('.tm-signing-field-editor-panel__condition-builder .tm-condition-builder__row')
            ).gridTemplateColumns
            """);

        Assert.IsFalse(tracks.Contains(' '),
            $"expected the embedded condition builder row to wrap into a single column inside the narrow " +
            $"inspector (on a 1440px viewport), but computed grid-template-columns was '{tracks}'");
    }

    [TestMethod]
    [Description("Role colour reaches the signing field overlays through --tm-signing-role-color")]
    public async Task RunnerOverlays_CarryRoleColorCustomProperty()
    {
        var page = await OpenSigningRunnerAsync();

        var hasRoleColour = await page.EvaluateAsync<bool>("""
            () => {
                const overlay = document.querySelector("[data-testid='signing-runner-demo'] .tm-signing-field-overlay");
                if (!overlay) return false;
                return (overlay.getAttribute('style') || '').includes('--tm-signing-role-color');
            }
            """);

        Assert.IsTrue(hasRoleColour,
            "the demo runner passes the submitter role colour, so its field overlays must carry " +
            "--tm-signing-role-color on the inline style");
    }

    [TestMethod]
    [Description("Designer keeps the 3-track workspace grid (palette | canvas | inspector) at a 1440px viewport")]
    public async Task Designer_KeepsThreeTrackWorkspaceGrid_At1440()
    {
        foreach (var page in await OpenBothSigningPagesAsync())
        {
            var measurement = await page.EvaluateAsync<string>("""
                () => {
                    const workspace = document.querySelector('.tm-pdf-template-designer__workspace');
                    const grid = document.querySelector('.tm-pdf-template-designer__workspace-grid');
                    if (!grid) return JSON.stringify({ tracks: '', containerWidth: 0 });
                    return JSON.stringify({
                        tracks: getComputedStyle(grid).gridTemplateColumns,
                        containerWidth: workspace ? workspace.offsetWidth : 0
                    });
                }
                """);
            var (tracks, containerWidth) = ParseMeasurement(measurement);

            Assert.AreEqual(3, tracks.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length,
                $"the designer workspace container is {containerWidth}px wide at a 1440px viewport, so the " +
                $"workspace grid must keep its palette | canvas | inspector layout, " +
                $"but computed grid-template-columns was '{tracks}'");
        }
    }

    [TestMethod]
    [Description("Runner keeps the 2-track layout (document | steps rail) at a 1440px viewport")]
    public async Task Runner_KeepsTwoTrackLayout_At1440()
    {
        foreach (var page in await OpenBothSigningPagesAsync())
        {
            var measurement = await page.EvaluateAsync<string>("""
                () => {
                    const root = document.querySelector('.tm-signing-form-runner');
                    const layout = document.querySelector('.tm-signing-form-runner__layout');
                    if (!layout) return JSON.stringify({ tracks: '', containerWidth: 0 });
                    return JSON.stringify({
                        tracks: getComputedStyle(layout).gridTemplateColumns,
                        containerWidth: root ? root.offsetWidth : 0
                    });
                }
                """);
            var (tracks, containerWidth) = ParseMeasurement(measurement);

            Assert.AreEqual(2, tracks.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length,
                $"the runner container is {containerWidth}px wide at a 1440px viewport, so the layout must keep " +
                $"its document | steps-rail columns, but computed grid-template-columns was '{tracks}'");
        }
    }

    private static (string Tracks, double ContainerWidth) ParseMeasurement(string json)
    {
        using var document = System.Text.Json.JsonDocument.Parse(json);
        return (
            document.RootElement.GetProperty("tracks").GetString() ?? string.Empty,
            document.RootElement.GetProperty("containerWidth").GetDouble());
    }

    private async Task<List<IPage>> OpenBothSigningPagesAsync()
    {
        var pages = new List<IPage>();
        foreach (var url in new[] { "/signing-components", "/signing-from-editor" })
        {
            var context = await CreateContextAsync();
            var page = await context.NewPageAsync();
            await page.SetViewportSizeAsync(1440, 900);
            await page.AddInitScriptAsync("() => localStorage.setItem('tm-demo-culture', 'en')");
            await page.GotoAsync($"{BaseUrl}{url}", new PageGotoOptions
            {
                WaitUntil = WaitUntilState.DOMContentLoaded,
                Timeout = 60_000
            });
            await WaitForAppReadyAsync(page);
            if (url == "/signing-from-editor")
            {
                // The bridge page renders the designer and runner only after the editor export /
                // preview steps, so drive them before measuring the grids.
                await DriveEditorBridgeAsync(page);
            }

            pages.Add(page);
        }

        return pages;
    }

    /// <summary>Advances the document-editor → signing bridge so the designer and runner sections render.</summary>
    private static async Task DriveEditorBridgeAsync(IPage page)
    {
        var export = page.Locator("[data-testid='signing-from-editor-export']");
        await export.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 60_000 });
        await page.WaitForFunctionAsync(
            "() => { const b = document.querySelector(\"[data-testid='signing-from-editor-export']\"); return b && !b.disabled; }",
            null,
            new PageWaitForFunctionOptions { Timeout = 60_000 });

        // The editor may still be laying out its pages when the button unlocks; the export then
        // yields ZERO pages and the designer renders its empty state. Retry until the designer
        // actually receives its workspace (documents bound).
        for (var attempt = 0; ; attempt++)
        {
            await page.WaitForTimeoutAsync(500);
            await export.ClickAsync();
            try
            {
                await page.WaitForSelectorAsync(
                    "[data-testid='signing-from-editor-designer'] .tm-pdf-template-designer__workspace",
                    new PageWaitForSelectorOptions { State = WaitForSelectorState.Attached, Timeout = 15_000 });
                break;
            }
            catch (TimeoutException)
            {
                if (attempt >= 2)
                {
                    throw;
                }
            }
        }

        await page.Locator("[data-testid='signing-from-editor-add-field']").ClickAsync();
        await page.WaitForFunctionAsync(
            "() => { const b = document.querySelector(\"[data-testid='signing-from-editor-preview']\"); return b && !b.disabled; }",
            null,
            new PageWaitForFunctionOptions { Timeout = 30_000 });
        await page.Locator("[data-testid='signing-from-editor-preview']").ClickAsync();
        await page.WaitForSelectorAsync("[data-testid='signing-from-editor-runner'] .tm-signing-form-runner__document",
            new PageWaitForSelectorOptions { State = WaitForSelectorState.Attached, Timeout = 30_000 });
        await page.WaitForTimeoutAsync(300);
    }

    private async Task<IPage> OpenSigningComponentsAsync()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(1440, 900);
        await page.AddInitScriptAsync("() => localStorage.setItem('tm-demo-culture', 'en')");
        await page.GotoAsync($"{BaseUrl}/signing-components", new PageGotoOptions
        {
            WaitUntil = WaitUntilState.DOMContentLoaded,
            Timeout = 60_000
        });
        await WaitForAppReadyAsync(page);
        await page.WaitForSelectorAsync("[data-testid='field-editor-panel']",
            new PageWaitForSelectorOptions { State = WaitForSelectorState.Attached, Timeout = 20_000 });
        return page;
    }

    private async Task<IPage> OpenSigningRunnerAsync()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(1440, 900);
        await page.AddInitScriptAsync("() => localStorage.setItem('tm-demo-culture', 'en')");
        await page.GotoAsync($"{BaseUrl}/signing-components", new PageGotoOptions
        {
            WaitUntil = WaitUntilState.DOMContentLoaded,
            Timeout = 60_000
        });
        await WaitForAppReadyAsync(page);
        await page.WaitForSelectorAsync("[data-testid='signing-runner-demo']",
            new PageWaitForSelectorOptions { State = WaitForSelectorState.Attached, Timeout = 20_000 });
        return page;
    }
}
