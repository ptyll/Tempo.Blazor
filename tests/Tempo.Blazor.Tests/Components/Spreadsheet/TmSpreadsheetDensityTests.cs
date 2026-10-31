using System.Collections;
using System.Reflection;
using Bunit;
using FluentAssertions;
using Tempo.Blazor.Components.Spreadsheet;
using Tempo.Blazor.Components.Spreadsheet.Enums;
using Tempo.Blazor.Components.Spreadsheet.Models;
using Tempo.Blazor.Components.Spreadsheet.Rendering;
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

        // Open the context menu on row 0 and pick "Set row height". The menu belongs to the HOST now (a
        // position:fixed menu may not live inside the layout container).
        await cut.InvokeAsync(() => grid.Instance.OnCanvasContextMenu(4, 4, 10, 10));
        cut.Render();
        var setRowHeight = cut.FindAll(".tm-spreadsheet-context-menu__item")
            .First(item => item.TextContent.Contains("Set row height", StringComparison.Ordinal));
        setRowHeight.Click();

        // The resize dialog is host-owned (a position:fixed surface may not live inside the layout container).
        var input = cut.Find(".tm-spreadsheet-resize-dialog__input");
        input.GetAttribute("value").Should().Be("18",
            "the dialog must show the MODEL height, not the 1.5x rendered one (27px)");

        // Applying without a change must not rewrite the model.
        cut.Find(".tm-spreadsheet-resize-dialog__btn--ok").Click();

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

    // ── Phase 7b: the header-gutter half of the density contract ───────────────────────────────

    [Fact]
    public void SpreadsheetDensityScale_Resolve_GutterScale()
    {
        var comfortable = SpreadsheetDensityScale.Resolve(SpreadsheetDensity.Comfortable, coarsePointer: false);

        comfortable.GutterScale.Should().Be(SpreadsheetDensityScale.ComfortableGutterScale);
        comfortable.GutterScale.Should().Be(1.4,
            "the tablet reference's gutter scale: 40 × 1.4 = 56 and 20 × 1.4 = 28");
        Math.Round(SpreadsheetGridConstants.RowHeaderWidth * comfortable.GutterScale).Should().Be(56);
        Math.Round(SpreadsheetGridConstants.ColumnHeaderHeight * comfortable.GutterScale).Should().Be(28);

        var compact = SpreadsheetDensityScale.Resolve(SpreadsheetDensity.Compact, coarsePointer: true);
        compact.GutterScale.Should().Be(1, "an explicit Compact keeps exactly 40/20 px even on a coarse pointer");
        compact.RowScale.Should().Be(1);
        compact.FontScale.Should().Be(1);
        compact.HandleScale.Should().Be(1);

        // Auto follows the pointer kind, which is the SP1 decision.
        SpreadsheetDensityScale.Resolve(SpreadsheetDensity.Auto, coarsePointer: true).GutterScale.Should().Be(1.4);
        SpreadsheetDensityScale.Resolve(SpreadsheetDensity.Auto, coarsePointer: false).GutterScale.Should().Be(1);
    }

    [Fact]
    public void CanvasGrid_ComfortableDensity_ScalesHeaderGutters()
    {
        var comfortable = Render<TmSpreadsheet>(parameters => parameters
            .Add(p => p.Density, SpreadsheetDensity.Comfortable));
        var frame = CanvasFrame(comfortable);

        FrameValue(frame, "RowHeaderWidth").Should().Be(56.0,
            "the canvas frame carries the SCALED gutter — the canvas itself never multiplies a constant");
        FrameValue(frame, "ColumnHeaderHeight").Should().Be(28.0);
        FrameValue(frame, "DensityGutterScale").Should().Be(1.4);

        var compact = Render<TmSpreadsheet>(parameters => parameters
            .Add(p => p.Density, SpreadsheetDensity.Compact));
        var compactFrame = CanvasFrame(compact);
        FrameValue(compactFrame, "RowHeaderWidth").Should().Be(40.0, "a fine pointer keeps exactly 40/20 px");
        FrameValue(compactFrame, "ColumnHeaderHeight").Should().Be(20.0);
        FrameValue(compactFrame, "DensityGutterScale").Should().Be(1.0);
    }

    [Fact]
    public void CanvasGrid_ScrollSize_IncludesScaledGutters()
    {
        var comfortable = Render<TmSpreadsheet>(parameters => parameters
            .Add(p => p.Density, SpreadsheetDensity.Comfortable));
        var compact = Render<TmSpreadsheet>(parameters => parameters
            .Add(p => p.Density, SpreadsheetDensity.Compact));

        var comfortableSize = comfortable.Find(".tm-spreadsheet-canvas-grid__size");
        var compactSize = compact.Find(".tm-spreadsheet-canvas-grid__size");
        var comfortableWidth = ScrollerSize(comfortableSize.GetAttribute("style"), "width");
        var compactWidth = ScrollerSize(compactSize.GetAttribute("style"), "width");
        var comfortableHeight = ScrollerSize(comfortableSize.GetAttribute("style"), "height");
        var compactHeight = ScrollerSize(compactSize.GetAttribute("style"), "height");

        comfortableWidth.Should()
            .Be(compactWidth + 16,
                "the scrollable width must include the 16px wider Comfortable row-header gutter, otherwise "
                + "the last column is clipped by the wider gutter");
        // The height delta is the scaled ROWS plus the 8px taller gutter: subtract the gutter from both
        // sizes and the remaining content must scale by the row scale (1.5) and nothing else.
        (comfortableHeight - 28).Should()
            .Be((compactHeight - 20) * 1.5,
                "the scrollable height must include the 8px taller Comfortable column-header gutter on top "
                + "of the 1.5x scaled rows");
    }

    [Fact]
    public void CanvasGrid_FirstFrame_WaitsForPointerKind()
    {
        JSInterop.Setup<bool>("tmSpreadsheetCanvas.isCoarsePointer").SetResult(true);

        var cut = Render<TmSpreadsheet>(parameters => parameters
            .Add(p => p.Density, SpreadsheetDensity.Auto));

        // The very first frame the engine sees must already carry the coarse answer: the coarse-pointer
        // interop is awaited BEFORE initEngine, so no Compact frame is ever painted on a touch device
        // (the "one-frame compact flash" the SP1 carry-forward asked about).
        var invocations = JSInterop.Invocations.ToList();
        var coarseIndex = invocations.FindIndex(invocation => invocation.Identifier == "tmSpreadsheetCanvas.isCoarsePointer");
        var initIndex = invocations.FindIndex(invocation => invocation.Identifier == InitEngine);
        coarseIndex.Should().BeGreaterThanOrEqualTo(0);
        initIndex.Should().BeGreaterThan(coarseIndex,
            "isCoarsePointer must be answered before the engine is initialised");

        var initFrame = invocations[initIndex].Arguments[^1]!;
        FrameValue(initFrame, "DensityResolved").Should().Be("Comfortable");
        FrameValue(initFrame, "RowHeaderWidth").Should().Be(56.0, "the first painted frame uses the scaled gutter");

        // The data-density attribute is present in the markup, but the PAINTED frame already agrees with
        // it — that is what makes a flash impossible.
        cut.Find(".tm-spreadsheet-canvas-grid").GetAttribute("data-density").Should().Be("comfortable");
    }

    private static double ScrollerSize(string? style, string property)
    {
        var match = System.Text.RegularExpressions.Regex.Match(
            style ?? string.Empty,
            $@"(?:^|;)\s*{property}\s*:\s*([\d.]+)px",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        match.Success.Should().BeTrue($"the scroller inline style carries {property} (style: {style})");
        return double.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
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
