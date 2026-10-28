using Bunit;
using Microsoft.AspNetCore.Components.Web;
using Tempo.Blazor.Components.Spreadsheet.Enums;
using Tempo.Blazor.Components.Spreadsheet.Models;
using Tempo.Blazor.Components.Spreadsheet;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.Spreadsheet;

[Collection("SpreadsheetClipboard")]
public class TmSpreadsheetTests : LocalizationTestBase
{
    [Fact]
    public void Render_Always_RendersCanvasGrid()
    {
        var cut = Render<TmSpreadsheet>();

        cut.Find(".tm-spreadsheet").Should().NotBeNull();
        cut.Find(".tm-spreadsheet-canvas-grid").Should().NotBeNull();
        cut.Find("canvas.tm-spreadsheet-canvas-grid__canvas").Should().NotBeNull();
    }

    [Fact]
    public void Render_DoesNotRenderDomGrid()
    {
        var cut = Render<TmSpreadsheet>();

        // The DOM grid renderer has been removed; only the canvas engine remains.
        // The canvas grid root reuses the .tm-spreadsheet-grid styling class, so
        // assert there is no *separate* (non-canvas) DOM grid element.
        cut.FindAll(".tm-spreadsheet-grid:not(.tm-spreadsheet-canvas-grid)").Should().BeEmpty();
    }

    [Fact]
    public void Render_CustomHeight_AppliesStyle()
    {
        var cut = Render<TmSpreadsheet>(parameters => parameters
            .Add(p => p.Height, "400px")
            .Add(p => p.Width, "800px"));

        var element = cut.Find(".tm-spreadsheet");
        element.GetAttribute("style").Should().Contain("height: 400px");
        element.GetAttribute("style").Should().Contain("width: 800px");
    }

    [Fact]
    public void Render_DefaultParameters_CreatesWorkbookWithOneSheet()
    {
        var cut = Render<TmSpreadsheet>();

        var component = cut.Instance;
        component.Workbook.Sheets.Should().HaveCount(1);
        component.Workbook.ActiveSheet.Should().NotBeNull();
    }

    [Fact]
    public void Render_WithRowsAndColumnsCount_SetsSheetDimensions()
    {
        var cut = Render<TmSpreadsheet>(parameters => parameters
            .Add(p => p.RowsCount, 100)
            .Add(p => p.ColumnsCount, 30));

        var sheet = cut.Instance.Workbook.ActiveSheet;
        sheet.Should().NotBeNull();
        sheet!.RowCount.Should().Be(100);
        sheet.ColumnCount.Should().Be(30);
    }

    [Fact]
    public void Render_WithCustomRowHeightAndColumnWidth_SetsDefaults()
    {
        var cut = Render<TmSpreadsheet>(parameters => parameters
            .Add(p => p.RowHeight, 25)
            .Add(p => p.ColumnWidth, 80));

        var sheet = cut.Instance.Workbook.ActiveSheet;
        sheet.Should().NotBeNull();
        sheet!.DefaultRowHeight.Should().Be(25);
        sheet.DefaultColumnWidth.Should().Be(80);
    }

    [Fact]
    public void Render_WithClass_AppliesCssClass()
    {
        var cut = Render<TmSpreadsheet>(parameters => parameters
            .Add(p => p.Class, "my-custom-class"));

        cut.Find(".tm-spreadsheet").ClassList.Should().Contain("my-custom-class");
    }

    [Fact]
    public void CopyButton_InvokesCopyCommand()
    {
        var cut = Render<TmSpreadsheet>();
        var sheet = cut.Instance.Workbook.ActiveSheet!;
        sheet.Cells["A1"] = new SpreadsheetCell { Value = "test" };
        sheet.ActiveCellRef = "A1";

        var btn = cut.FindAll(".tm-spreadsheet-toolbar__button")
            .First(b => b.GetAttribute("title") == "Copy");
        btn.Click();

        SpreadsheetClipboard.Cells.Should().NotBeNull();
        SpreadsheetClipboard.Cells!.Should().ContainKey("A1");
    }

    [Fact]
    public void CutButton_RemovesCellAndSetsClipboard()
    {
        var cut = Render<TmSpreadsheet>();
        var sheet = cut.Instance.Workbook.ActiveSheet!;
        sheet.Cells["A1"] = new SpreadsheetCell { Value = "test" };
        sheet.ActiveCellRef = "A1";

        var btn = cut.FindAll(".tm-spreadsheet-toolbar__button")
            .First(b => b.GetAttribute("title") == "Cut");
        btn.Click();

        SpreadsheetClipboard.IsCut.Should().BeTrue();
        sheet.Cells.ContainsKey("A1").Should().BeFalse();
    }

    [Fact]
    public void PasteButton_InsertsClipboardContent()
    {
        var cut = Render<TmSpreadsheet>();
        var sheet = cut.Instance.Workbook.ActiveSheet!;
        sheet.Cells["A1"] = new SpreadsheetCell { Value = "hello" };
        sheet.ActiveCellRef = "A1";

        cut.FindAll(".tm-spreadsheet-toolbar__button")
            .First(b => b.GetAttribute("title") == "Copy").Click();

        sheet.ActiveCellRef = "B1";
        cut.FindAll(".tm-spreadsheet-toolbar__button")
            .First(b => b.GetAttribute("title") == "Paste").Click();

        sheet.Cells["B1"].Value.Should().Be("hello");
    }

    [Fact]
    public void InsertRowButton_AddsRow()
    {
        var cut = Render<TmSpreadsheet>();
        var sheet = cut.Instance.Workbook.ActiveSheet!;
        sheet.Cells["A1"] = new SpreadsheetCell { Value = "top" };
        sheet.Cells["A2"] = new SpreadsheetCell { Value = "bottom" };
        sheet.ActiveCellRef = "A2";

        cut.FindAll(".tm-spreadsheet-toolbar__button")
            .First(b => b.GetAttribute("title") == "Insert row").Click();

        sheet.Cells["A1"].Value.Should().Be("top");
        sheet.Cells["A3"].Value.Should().Be("bottom");
    }

    [Fact]
    public void DeleteRowButton_RemovesRow()
    {
        var cut = Render<TmSpreadsheet>();
        var sheet = cut.Instance.Workbook.ActiveSheet!;
        sheet.Cells["A1"] = new SpreadsheetCell { Value = "top" };
        sheet.Cells["A2"] = new SpreadsheetCell { Value = "middle" };
        sheet.Cells["A3"] = new SpreadsheetCell { Value = "bottom" };
        sheet.ActiveCellRef = "A2";

        cut.FindAll(".tm-spreadsheet-toolbar__button")
            .First(b => b.GetAttribute("title") == "Delete row").Click();

        sheet.Cells["A1"].Value.Should().Be("top");
        sheet.Cells["A2"].Value.Should().Be("bottom");
    }

    [Fact]
    public void InsertColumnButton_AddsColumn()
    {
        var cut = Render<TmSpreadsheet>();
        var sheet = cut.Instance.Workbook.ActiveSheet!;
        sheet.Cells["A1"] = new SpreadsheetCell { Value = "left" };
        sheet.Cells["B1"] = new SpreadsheetCell { Value = "right" };
        sheet.ActiveCellRef = "B1";

        cut.FindAll(".tm-spreadsheet-toolbar__button")
            .First(b => b.GetAttribute("title") == "Insert column").Click();

        sheet.Cells["A1"].Value.Should().Be("left");
        sheet.Cells["C1"].Value.Should().Be("right");
    }

    [Fact]
    public void DeleteColumnButton_RemovesColumn()
    {
        var cut = Render<TmSpreadsheet>();
        var sheet = cut.Instance.Workbook.ActiveSheet!;
        sheet.Cells["A1"] = new SpreadsheetCell { Value = "left" };
        sheet.Cells["B1"] = new SpreadsheetCell { Value = "middle" };
        sheet.Cells["C1"] = new SpreadsheetCell { Value = "right" };
        sheet.ActiveCellRef = "B1";

        cut.FindAll(".tm-spreadsheet-toolbar__button")
            .First(b => b.GetAttribute("title") == "Delete column").Click();

        sheet.Cells["A1"].Value.Should().Be("left");
        sheet.Cells["B1"].Value.Should().Be("right");
    }

    // ── Sheet Tabs ──

    [Fact]
    public void Render_DisplaysSheetTabs()
    {
        var cut = Render<TmSpreadsheet>();
        cut.FindComponent<TmSpreadsheetSheetTabs>().Should().NotBeNull();
    }

    [Fact]
    public void AddSheetButton_AddsNewSheet()
    {
        var cut = Render<TmSpreadsheet>();
        var workbook = cut.Instance.Workbook;
        workbook.Sheets.Should().HaveCount(1);

        var addBtn = cut.Find(".tm-spreadsheet-sheet-tabs__add");
        addBtn.Click();

        workbook.Sheets.Should().HaveCount(2);
        workbook.ActiveSheetIndex.Should().Be(1);
    }

    [Fact]
    public void SheetTab_Click_SwitchesActiveSheet()
    {
        var cut = Render<TmSpreadsheet>();
        var workbook = cut.Instance.Workbook;
        workbook.AddSheet("Sheet2");
        cut.Render();

        var tabs = cut.FindAll(".tm-spreadsheet-sheet-tab");
        tabs[1].Click();

        workbook.ActiveSheetIndex.Should().Be(1);
    }

    [Fact]
    public void DeleteSheetButton_RemovesSheet()
    {
        var cut = Render<TmSpreadsheet>();
        var workbook = cut.Instance.Workbook;
        workbook.AddSheet("Sheet2");
        cut.Render();

        var closeBtns = cut.FindAll(".tm-spreadsheet-sheet-tab__close");
        closeBtns[0].Click();

        workbook.Sheets.Should().HaveCount(1);
    }

    // ── Phase 12: Toolbar completion ──

    [Fact]
    public void InsertLinkDialog_OpensAndCloses()
    {
        var cut = Render<TmSpreadsheet>();
        var sheet = cut.Instance.Workbook.ActiveSheet!;
        sheet.ActiveCellRef = "A1";

        var tabs = cut.FindAll(".tm-spreadsheet-toolbar__tab");
        tabs[1].Click();

        var linkBtn = cut.FindAll(".tm-spreadsheet-toolbar__button")
            .First(b => b.GetAttribute("title") == "Insert link");
        linkBtn.Click();

        cut.FindAll(".tm-spreadsheet-hyperlink").Count.Should().Be(1);

        var cancelBtn = cut.FindAll("button").First(b => b.TextContent.Contains("Cancel"));
        cancelBtn.Click();

        cut.FindAll(".tm-spreadsheet-hyperlink").Count.Should().Be(0);
    }

    [Fact]
    public void InsertImageDialog_OpensAndCloses()
    {
        var cut = Render<TmSpreadsheet>();
        var sheet = cut.Instance.Workbook.ActiveSheet!;
        sheet.ActiveCellRef = "B2";

        var tabs = cut.FindAll(".tm-spreadsheet-toolbar__tab");
        tabs[1].Click();

        var imgBtn = cut.FindAll(".tm-spreadsheet-toolbar__button")
            .First(b => b.GetAttribute("title") == "Insert image");
        imgBtn.Click();

        cut.FindAll(".tm-spreadsheet-insert-image").Count.Should().Be(1);

        var cancelBtn = cut.FindAll("button").First(b => b.TextContent.Contains("Cancel"));
        cancelBtn.Click();

        cut.FindAll(".tm-spreadsheet-insert-image").Count.Should().Be(0);
    }

    [Fact]
    public void ToggleGridLines_HidesBorders()
    {
        var cut = Render<TmSpreadsheet>();
        var sheet = cut.Instance.Workbook.ActiveSheet!;
        sheet.ShowGridLines.Should().BeTrue();

        var tabs = cut.FindAll(".tm-spreadsheet-toolbar__tab");
        tabs[3].Click();

        var gridBtn = cut.FindAll(".tm-spreadsheet-toolbar__button")
            .First(b => b.GetAttribute("title") == "Grid lines");
        gridBtn.Click();

        sheet.ShowGridLines.Should().BeFalse();
        cut.Find(".tm-spreadsheet-grid--no-gridlines").Should().NotBeNull();
    }

    [Fact]
    public void Render_FormulaBar_DisplaysActiveCellValue()
    {
        var cut = Render<TmSpreadsheet>();
        var sheet = cut.Instance.Workbook.ActiveSheet!;
        sheet.SetCellValue(0, 0, "Hello World");
        sheet.ActiveCellRef = "A1";
        cut.Render();

        var display = cut.Find(".tm-spreadsheet-formula-bar__display");
        display.TextContent.Trim().Should().Be("Hello World");
    }

    [Fact]
    public void Render_FormulaBar_DisplaysActiveCellFormula()
    {
        var cut = Render<TmSpreadsheet>();
        var sheet = cut.Instance.Workbook.ActiveSheet!;
        sheet.SetCellValue(0, 0, "=SUM(B1:B5)");
        sheet.ActiveCellRef = "A1";
        cut.Render();

        var display = cut.Find(".tm-spreadsheet-formula-bar__display");
        display.TextContent.Trim().Should().Be("=SUM(B1:B5)");
    }

}
