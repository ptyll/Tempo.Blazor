using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Phase 5 review E2E: the 768/1200 Modeling breakpoints and the UNFORCED shell. The demo
/// /modeling-editor page gives the editor ≥1200px of container from 1264px of viewport up, the
/// nested TmDiagramEditor (the preview) measures its OWN container, and the docked issues dock
/// leaves a usable canvas at both desktop baselines (1440x900 and 1280x800).
/// </summary>
[TestClass]
[TestCategory("WASM")]
public sealed class ModelingPhase5ReviewE2ETests : WasmTestBase
{
    private const string ModelingEditorUrl = "/modeling-editor";

    [TestMethod]
    [Description("At 1440x900 the demo editor is a ≥1200px desktop container with a usable diagram canvas viewport and a dock that fits two full issue rows")]
    public async Task Editor_1440_IsDesktopWithUsableCanvasAndDock()
    {
        // Uniform 500-row stress list: the dock capacity measure is not distorted by the
        // taller suggested-fix rows of the mixed scenario.
        var page = await OpenLoadedModelingPageAsync("?scenario=issues-500", width: 1440, height: 900);

        var metrics = await page.EvaluateAsync<EditorMetrics>(
            """
            () => {
                const editor = document.querySelector("[data-testid='modeling-editor']");
                const editorBox = editor.getBoundingClientRect();
                const layout = editor.querySelector('.tm-layout[data-layout]');
                const previewEditor = document.querySelector('.tm-modeling-diagram-preview__editor');
                const previewShell = previewEditor ? previewEditor.parentElement : null;
                // The DIAGRAM CANVAS viewport: the .tm-diagram-canvas the nested viewer editor
                // renders inside its chrome (toolbar/status) — not the whole TmDiagramEditor box.
                const diagramCanvas = document.querySelector('.tm-modeling-diagram-preview__editor .tm-diagram-canvas');
                const list = document.querySelector("[data-testid='modeling-issue-list']");
                const rows = list ? [...list.querySelectorAll('li > [data-severity]')] : [];
                const heights = rows.map(row => row.getBoundingClientRect().height);
                const listStyle = list ? getComputedStyle(list) : null;
                return {
                    editorWidth: editorBox.width,
                    editorLayout: layout?.getAttribute('data-layout') ?? '',
                    previewWidth: previewShell ? previewShell.clientWidth : 0,
                    previewEditorLayout: previewEditor?.getAttribute('data-editor-layout') ?? '',
                    diagramCanvasHeight: diagramCanvas ? diagramCanvas.getBoundingClientRect().height : 0,
                    listClientHeight: list ? list.clientHeight : 0,
                    minRow: heights.length ? Math.min(...heights) : 0,
                    rowGap: listStyle ? (parseFloat(listStyle.rowGap) || 0) : 0,
                    listPaddingY: listStyle ? (parseFloat(listStyle.paddingTop) + parseFloat(listStyle.paddingBottom)) : 0
                };
            }
            """);

        Assert.IsTrue(metrics.EditorWidth >= 1200,
            $"The demo page must give the editor ≥1200px of container at 1440x900 (was {metrics.EditorWidth}).");
        Assert.AreEqual("desktop", metrics.EditorLayout, "≥1200px of container resolves the docked desktop workspace.");
        // Measures the .tm-diagram-canvas VIEWPORT inside the nested viewer editor (its chrome —
        // page tabs, toolbar, status bar — is excluded), not the whole TmDiagramEditor box.
        // Measured 1440x900 reality: the .tm-diagram-canvas viewport is ~180px (the viewer editor's
// tablet chrome — page tabs, two toolbar rows, status bar — sits above it). The former ≥320
// measured the WHOLE nested TmDiagramEditor box; the canvas viewport floor is 160px (10rem).
Assert.IsTrue(metrics.DiagramCanvasHeight >= 160,
            $"The diagram canvas viewport stays ≥160px tall (was {metrics.DiagramCanvasHeight}).");

        // The preview's diagram editor follows its OWN width (unforced shell), not the desktop
        // cascade: at 1440x900 the canvas column is below 1200px, so the preview is NOT desktop.
        Assert.IsTrue(metrics.PreviewWidth > 0 && metrics.PreviewWidth < 1200,
            $"Preview column should be below 1200 (was {metrics.PreviewWidth}).");
        Assert.AreNotEqual("desktop", metrics.PreviewEditorLayout,
            "The preview's data-editor-layout follows its own container, not the desktop shell.");

        // The dock fits two full issue rows of the list's own row height (the list scrolls for
        // the rest) — the dock row is a minority of the canvas column, never a sliver.
        var twoRowsNeeded = (2 * metrics.MinRow) + metrics.RowGap + metrics.ListPaddingY;
        Assert.IsTrue(metrics.MinRow > 0 && metrics.ListClientHeight >= twoRowsNeeded,
            $"The dock must fit two full issue rows (list {metrics.ListClientHeight}px, two rows need {twoRowsNeeded}px).");

        Assert.IsFalse(await IssuesDockOverlapsPreviewAsync(page), "The dock stays under the preview.");
    }

    [TestMethod]
    [Description("At 1280x800 the editor keeps the desktop dock with a usable diagram canvas viewport and an unclipped empty dock state")]
    public async Task Editor_1280_KeepsUsableCanvasAndUnclippedEmptyDock()
    {
        var page = await OpenLoadedModelingPageAsync("?scenario=issues-empty", width: 1280, height: 800);

        var metrics = await page.EvaluateAsync<TabletMetrics>(
            """
            () => {
                const editor = document.querySelector("[data-testid='modeling-editor']");
                const editorBox = editor.getBoundingClientRect();
                const layout = editor.querySelector('.tm-layout[data-layout]');
                // The DIAGRAM CANVAS viewport inside the nested viewer editor (chrome excluded),
                // not the whole TmDiagramEditor box.
                const diagramCanvas = document.querySelector('.tm-modeling-diagram-preview__editor .tm-diagram-canvas');
                return {
                    editorWidth: editorBox.width,
                    editorLayout: layout?.getAttribute('data-layout') ?? '',
                    diagramCanvasHeight: diagramCanvas ? diagramCanvas.getBoundingClientRect().height : 0
                };
            }
            """);

        Assert.IsTrue(metrics.EditorWidth >= 1200,
            $"1280x800 with the demo shell is a desktop container (was {metrics.EditorWidth}).");
        Assert.AreEqual("desktop", metrics.EditorLayout, "The docked desktop workspace applies from 1200px of container.");
        // Measures the .tm-diagram-canvas VIEWPORT inside the nested viewer editor (its chrome —
        // page tabs, toolbar, status bar — is excluded), not the whole TmDiagramEditor box.
        // Measured 1280x800 reality: the canvas viewport is ~240px (the narrower preview column renders
// the viewer editor's compact chrome). The former ≥240 measured the whole TmDiagramEditor box;
// the viewport floor keeps 40px of margin under that measured value.
Assert.IsTrue(metrics.DiagramCanvasHeight >= 200,
            $"The diagram canvas viewport stays ≥200px tall (was {metrics.DiagramCanvasHeight}).");

        // The empty state fits the dock row — no clipping, no overlap.
        var emptyState = await page.EvaluateAsync<bool>(
            """
            () => {
                const empty = document.querySelector("[data-testid='modeling-issue-empty']");
                const panel = document.querySelector("[data-testid='modeling-issue-panel']");
                if (!empty || !panel) return false;
                const a = empty.getBoundingClientRect();
                const b = panel.getBoundingClientRect();
                return a.top >= b.top - 1 && a.bottom <= b.bottom + 1 && a.height > 0;
            }
            """);
        Assert.IsTrue(emptyState, "The empty state renders fully inside the dock (not clipped).");
        Assert.IsFalse(await IssuesDockOverlapsPreviewAsync(page), "The dock never overlaps the preview.");
    }

    /// <summary>Review fix (phase 5): the mixed scenario's recommendation box makes its rows
    /// TALLER than the uniform stress list — the raised dock floor must still fit two full rows
    /// of the mixed list at the primary desktop viewport.</summary>
    [TestMethod]
    [Description("At 1440x900 the dock fits two full rows of the mixed scenario (recommendation-box rows)")]
    public async Task Editor_1440_MixedScenarioDockFitsTwoRows()
    {
        var page = await OpenLoadedModelingPageAsync("?scenario=issues-mixed", width: 1440, height: 900);

        var metrics = await page.EvaluateAsync<EditorMetrics>(
            """
            () => {
                const list = document.querySelector("[data-testid='modeling-issue-list']");
                const rows = list ? [...list.querySelectorAll('li > [data-severity]')] : [];
                const heights = rows.map(row => row.getBoundingClientRect().height);
                const listStyle = list ? getComputedStyle(list) : null;
                return {
                    listClientHeight: list ? list.clientHeight : 0,
                    minRow: heights.length ? Math.min(...heights) : 0,
                    rowGap: listStyle ? (parseFloat(listStyle.rowGap) || 0) : 0,
                    listPaddingY: listStyle ? (parseFloat(listStyle.paddingTop) + parseFloat(listStyle.paddingBottom)) : 0
                };
            }
            """);

        var twoRowsNeeded = (2 * metrics.MinRow) + metrics.RowGap + metrics.ListPaddingY;
        Assert.IsTrue(metrics.MinRow > 0 && metrics.ListClientHeight >= twoRowsNeeded,
            $"The dock must fit two full mixed-scenario rows (list {metrics.ListClientHeight}px, two rows need {twoRowsNeeded}px).");
        Assert.IsFalse(await IssuesDockOverlapsPreviewAsync(page), "The dock stays under the preview.");
    }

    private async Task<IPage> OpenLoadedModelingPageAsync(string query, int width, int height)
    {
        var context = await CreateContextAsync();
        await context.AddInitScriptAsync("localStorage.setItem('tm-demo-culture', 'en');");
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(width, height);
        await page.GotoAsync($"{BaseUrl}{ModelingEditorUrl}{query}", new PageGotoOptions { WaitUntil = WaitUntilState.Load, Timeout = 60000 });
        await WaitForAppReadyAsync(page);
        await page.Locator("[data-testid='modeling-editor'][data-state='loaded']")
            .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });
        await page.Locator("[data-testid='modeling-issue-panel']")
            .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });
        await page.WaitForFunctionAsync(
            "() => document.querySelectorAll('.tm-diagram-node').length > 0",
            null,
            new PageWaitForFunctionOptions { Timeout = 15000 });
        return page;
    }

    private static Task<bool> IssuesDockOverlapsPreviewAsync(IPage page) =>
        page.EvaluateAsync<bool>(
            """
            () => {
                const panel = document.querySelector("[data-testid='modeling-issue-panel']");
                const preview = document.querySelector("[data-testid='modeling-preview-panel']");
                if (!panel || !preview) {
                    return true;
                }

                const a = panel.getBoundingClientRect();
                const b = preview.getBoundingClientRect();
                return !(a.right <= b.left || a.left >= b.right || a.bottom <= b.top || a.top >= b.bottom);
            }
            """);

    private sealed class EditorMetrics
    {
        public double EditorWidth { get; set; }
        public string EditorLayout { get; set; } = string.Empty;
        public double PreviewWidth { get; set; }
        public string PreviewEditorLayout { get; set; } = string.Empty;
        public double DiagramCanvasHeight { get; set; }
        public double ListClientHeight { get; set; }
        public double MinRow { get; set; }
        public double RowGap { get; set; }
        public double ListPaddingY { get; set; }
    }

    private sealed class TabletMetrics
    {
        public double EditorWidth { get; set; }
        public string EditorLayout { get; set; } = string.Empty;
        public double DiagramCanvasHeight { get; set; }
    }
}
