using System.Collections;
using System.Reflection;
using Bunit;
using FluentAssertions;
using Tempo.Blazor.Components.Spreadsheet;
using Tempo.Blazor.Components.Spreadsheet.Enums;
using Tempo.Blazor.Components.Spreadsheet.Models;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.Spreadsheet;

/// <summary>
/// Redesign phase 2 — the SP1 grid density and the mode-scoped chrome.
/// <para>
/// <c>Density</c> is a RENDER scale: the model row height (<see cref="TmSpreadsheet.RowHeight"/>,
/// <c>SpreadsheetSheet.DefaultRowHeight</c>, a per-row <c>Height</c>, and therefore every XLSX height)
/// keeps its value, while the canvas geometry draws rows (and their font) larger on a touch device. The
/// default <see cref="SpreadsheetDensity.Auto"/> therefore resolves to Comfortable on a coarse pointer and
/// Compact everywhere else — the resolution itself lives in the canvas script
/// (<c>resolveDensity</c>, covered by <c>npm run test:spreadsheet</c>) and the component consumes it.
/// </para>
/// </summary>
[Collection("SpreadsheetClipboard")]
public class TmSpreadsheetDensityTests : LocalizationTestBase
{
    private const string InitEngine = "tmSpreadsheetCanvas.initEngine";
    private const string RenderFrameCall = "tmSpreadsheetCanvas.render";

    [Fact]
    public void Spreadsheet_Density_DefaultAuto_PassedToCanvas()
    {
        var cut = Render<TmSpreadsheet>();

        var frame = CanvasFrame(cut);
        FrameValue(frame, "Density").Should().Be("Auto");
        FrameValue(frame, "DensityResolved").Should().Be("Compact",
            "bUnit has no coarse pointer, so Auto resolves to the Compact render scale");
        FrameValue(frame, "DensityRowScale").Should().Be(1.0);
    }

    [Fact]
    public void Spreadsheet_Density_Comfortable_ScalesTheCanvasRows_WithoutChangingTheModel()
    {
        var cut = Render<TmSpreadsheet>(parameters => parameters
            .Add(p => p.Density, SpreadsheetDensity.Comfortable));

        var frame = CanvasFrame(cut);
        FrameValue(frame, "Density").Should().Be("Comfortable");
        FrameValue(frame, "DensityResolved").Should().Be("Comfortable");
        FrameValue(frame, "DensityRowScale").Should().Be(1.5);

        var firstRowHeight = RowHeights(frame).First();
        firstRowHeight.Should().Be(30,
            "Comfortable draws a 20px default row at 1.5x (the tablet reference's taller rows)");

        // The MODEL is untouched: XLSX heights and the workbook contract stay exact.
        var sheet = cut.Instance.Workbook.ActiveSheet;
        sheet.Should().NotBeNull();
        sheet!.DefaultRowHeight.Should().Be(20);
    }

    [Fact]
    public void Spreadsheet_Density_Compact_KeepsTheDefaultRowGeometry()
    {
        var cut = Render<TmSpreadsheet>(parameters => parameters
            .Add(p => p.Density, SpreadsheetDensity.Compact));

        var frame = CanvasFrame(cut);
        FrameValue(frame, "DensityResolved").Should().Be("Compact");
        FrameValue(frame, "DensityRowScale").Should().Be(1.0);
        RowHeights(frame).First().Should().Be(20);
    }

    [Fact]
    public void Spreadsheet_FullMode_HasWorkspaceClass_EmbeddedDoesNot()
    {
        var full = Render<TmSpreadsheet>(parameters => parameters
            .Add(p => p.Mode, SpreadsheetMode.Full));

        var fullRoot = full.Find(".tm-spreadsheet");
        fullRoot.ClassList.Should().Contain("tm-spreadsheet--workspace",
            "Full mode paints the workspace and lifts the ribbon/formula bar as cards");
        fullRoot.ClassList.Should().NotContain("tm-spreadsheet--chrome-flat");

        var embedded = Render<TmSpreadsheet>(parameters => parameters
            .Add(p => p.Mode, SpreadsheetMode.Embedded));

        var embeddedRoot = embedded.Find(".tm-spreadsheet");
        embeddedRoot.ClassList.Should().Contain("tm-spreadsheet--chrome-flat",
            "an embedded grid (Notion block, modal) stays flat: no workspace, no card chrome");
        embeddedRoot.ClassList.Should().NotContain("tm-spreadsheet--workspace");
    }

    // ── Item 1: the density scale must never leak into the model ─────────────────────────────────

    [Fact]
    public async Task Density_Comfortable_RowResize_CommitsTheModelHeight()
    {
        var cut = Render<TmSpreadsheet>(parameters => parameters
            .Add(p => p.Density, SpreadsheetDensity.Comfortable));
        var grid = cut.FindComponent<TmSpreadsheetCanvasGrid>();

        // The canvas drags in RENDERED pixels: 45 px on a 1.5x grid is a 30 px model row.
        await cut.InvokeAsync(() => grid.Instance.OnCanvasRowResize(0, 45));

        cut.Instance.Workbook.ActiveSheet.Should().NotBeNull();
        cut.Instance.Workbook.ActiveSheet!.Rows[0].Height.Should().Be(30,
            "a comfortable row is drawn at 1.5x, so the drag's 45 rendered pixels are a 30px model height");
    }

    [Fact]
    public async Task Density_Compact_RowResize_KeepsTheRenderedHeight()
    {
        var cut = Render<TmSpreadsheet>(parameters => parameters
            .Add(p => p.Density, SpreadsheetDensity.Compact));
        var grid = cut.FindComponent<TmSpreadsheetCanvasGrid>();

        await cut.InvokeAsync(() => grid.Instance.OnCanvasRowResize(0, 45));

        cut.Instance.Workbook.ActiveSheet!.Rows[0].Height.Should().Be(45, "a compact row is drawn 1:1");
    }

    [Fact]
    public async Task Zoom_RowResize_CommitsTheModelHeight()
    {
        double? committed = null;
        var sheet = new SpreadsheetSheet { Name = "Sheet1", RowCount = 20, ColumnCount = 10 };
        var cut = Render<TmSpreadsheetCanvasGrid>(parameters => parameters
            .Add(p => p.Sheet, sheet)
            .Add(p => p.Zoom, 1.5)
            .Add(p => p.OnRowResizeRequested, (Action<(int RowIndex, double Height)>)(args => committed = args.Height)));

        await cut.InvokeAsync(() => cut.Instance.OnCanvasRowResize(0, 45));

        committed.Should().Be(30, "the rendered 45px at 150% zoom is a 30px model row");
    }

    [Fact]
    public async Task Zoom_ColumnResize_CommitsTheModelWidth()
    {
        double? committed = null;
        var sheet = new SpreadsheetSheet { Name = "Sheet1", RowCount = 20, ColumnCount = 10 };
        var cut = Render<TmSpreadsheetCanvasGrid>(parameters => parameters
            .Add(p => p.Sheet, sheet)
            .Add(p => p.Zoom, 1.5)
            .Add(p => p.OnColumnResizeRequested, (Action<(int ColIndex, double Width)>)(args => committed = args.Width)));

        await cut.InvokeAsync(() => cut.Instance.OnCanvasColumnResize(0, 96));

        committed.Should().Be(64, "the rendered 96px at 150% zoom is a 64px model column");
    }

    [Fact]
    public async Task Density_Comfortable_RowHeightDialog_ShowsAndCommitsTheModelHeight()
    {
        var workbook = new SpreadsheetWorkbook();
        var sheet = workbook.ActiveSheet!;
        sheet.Rows[0] = new SpreadsheetRow { Index = 0, Height = 18 };
        var cut = Render<TmSpreadsheet>(parameters => parameters
            .Add(p => p.Density, SpreadsheetDensity.Comfortable)
            .Add(p => p.InitialWorkbook, workbook));
        var grid = cut.FindComponent<TmSpreadsheetCanvasGrid>();

        // Open the context menu on row 0 and pick "Set row height".
        await cut.InvokeAsync(() => grid.Instance.OnCanvasContextMenu(4, 4, 10, 10));
        grid.Render();
        var setRowHeight = grid.FindAll(".tm-spreadsheet-context-menu__item")
            .First(item => item.TextContent.Contains("Set row height", StringComparison.Ordinal));
        setRowHeight.Click();

        var input = grid.Find(".tm-spreadsheet-resize-dialog__input");
        input.GetAttribute("value").Should().Be("18",
            "the dialog must show the MODEL height, not the 1.5x rendered one (27px)");

        // Applying without a change must not rewrite the model.
        grid.Find(".tm-spreadsheet-resize-dialog__btn--ok").Click();

        cut.Instance.Workbook.ActiveSheet!.Rows[0].Height.Should().Be(18,
            "opening and confirming the dialog without editing must leave the model untouched");
    }

    [Fact]
    public void Density_Auto_WithACoarsePointer_ResolvesComfortable()
    {
        JSInterop.Setup<bool>("tmSpreadsheetCanvas.isCoarsePointer").SetResult(true);

        var cut = Render<TmSpreadsheet>(parameters => parameters
            .Add(p => p.Density, SpreadsheetDensity.Auto));

        var frame = CanvasFrame(cut);
        FrameValue(frame, "DensityResolved").Should().Be("Comfortable");
        FrameValue(frame, "DensityRowScale").Should().Be(1.5);
        RowHeights(frame).First().Should().Be(30);
        cut.Find(".tm-spreadsheet-canvas-grid").GetAttribute("data-density").Should().Be("comfortable");
    }

    /// <summary>The last canvas frame handed to the engine (initEngine or a later render).</summary>
    private object CanvasFrame(IRenderedComponent<TmSpreadsheet> cut)
    {
        var invocation = JSInterop.Invocations
            .LastOrDefault(i => i.Identifier is InitEngine or RenderFrameCall);
        invocation.Should().NotBeNull("the canvas grid must hand a frame to the JS engine");
        var arguments = invocation!.Arguments;
        arguments.Count.Should().BeGreaterThan(0);
        return arguments[^1]!;
    }

    private static object? FrameValue(object frame, string name) =>
        frame.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance)?.GetValue(frame);

    private static List<double> RowHeights(object frame)
    {
        var rows = (IEnumerable?)FrameValue(frame, "Rows");
        rows.Should().NotBeNull("the canvas frame carries the rendered row frames");
        var heights = new List<double>();
        foreach (var row in rows!)
        {
            heights.Add(Convert.ToDouble(
                row.GetType().GetProperty("Height", BindingFlags.Public | BindingFlags.Instance)?.GetValue(row),
                System.Globalization.CultureInfo.InvariantCulture));
        }

        return heights;
    }
}
