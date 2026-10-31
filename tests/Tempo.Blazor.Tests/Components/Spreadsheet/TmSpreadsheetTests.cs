using Bunit;
using Microsoft.AspNetCore.Components;
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
            .First(b => b.GetAttribute("title") == "Delete options").Click();
        cut.FindAll("[title='Delete row']")
            .First(b => b.ClassList.Contains("tm-border-picker__item")).Click();

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
            .First(b => b.GetAttribute("title") == "Delete options").Click();
        cut.FindAll("[title='Delete column']")
            .First(b => b.ClassList.Contains("tm-border-picker__item")).Click();

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


    // ── Ribbon v2 + document header (phase 5) ──

    [Fact]
    public void Spreadsheet_CustomTools_ForwardedToToolbar()
    {
        var tools = new List<SpreadsheetCustomTool>
        {
            new() { IconName = "copy", Title = "My Tool", Tab = "Home", Order = 0 }
        };

        var cut = Render<TmSpreadsheet>(parameters => parameters
            .Add(p => p.CustomTools, tools));

        cut.FindAll(".tm-spreadsheet-toolbar__button")
            .Count(b => b.GetAttribute("title") == "My Tool")
            .Should()
            .Be(1);
    }

    [Fact]
    public void Spreadsheet_ShowHeaderFalse_ByDefault_NoHeader()
    {
        var cut = Render<TmSpreadsheet>();

        cut.FindAll(".tm-spreadsheet__header").Should().BeEmpty("the document header is opt-in (G9/SP3)");
        cut.FindAll(".tm-spreadsheet-toolbar__tab")
            .Select(t => t.TextContent.Trim())
            .Should()
            .Contain("File", "without the header the File tab stays the compatibility path");
    }

    [Fact]
    public void Spreadsheet_Title_And_HeaderActions_RenderInHeader()
    {
        var cut = Render<TmSpreadsheet>(parameters => parameters
            .Add(p => p.ShowHeader, true)
            .Add(p => p.Title, "Projekt Aurora")
            .Add(p => p.HeaderStart, builder => { builder.OpenElement(0, "span"); builder.AddAttribute(1, "class", "host-start"); builder.AddContent(2, "start-slot"); builder.CloseElement(); })
            .Add(p => p.HeaderActions, builder => { builder.OpenElement(0, "button"); builder.AddAttribute(1, "class", "host-action"); builder.AddContent(2, "share"); builder.CloseElement(); }));

        var header = cut.Find(".tm-spreadsheet__header");
        header.TextContent.Should().Contain("Projekt Aurora");
        header.QuerySelector(".host-start").Should().NotBeNull();
        header.QuerySelector(".host-action").Should().NotBeNull();
        header.QuerySelector(".tm-spreadsheet__title")!.TextContent.Trim().Should().Be("Projekt Aurora");
    }

    [Fact]
    public void Spreadsheet_Header_HidesFileTab()
    {
        var cut = Render<TmSpreadsheet>(parameters => parameters
            .Add(p => p.ShowHeader, true));

        cut.FindAll(".tm-spreadsheet-toolbar__tab")
            .Select(t => t.TextContent.Trim())
            .Should()
            .NotContain("File", "the document header replaces the File tab");
    }

    [Fact]
    public void Spreadsheet_ShowFileTabFalse_HidesFileTabWithoutHeader()
    {
        var cut = Render<TmSpreadsheet>(parameters => parameters
            .Add(p => p.ShowFileTab, false));

        cut.FindAll(".tm-spreadsheet-toolbar__tab")
            .Select(t => t.TextContent.Trim())
            .Should()
            .NotContain("File");
    }

    [Fact]
    public void Spreadsheet_Header_FileActionsAndGridToggle()
    {
        var cut = Render<TmSpreadsheet>(parameters => parameters
            .Add(p => p.ShowHeader, true)
            .Add(p => p.OnOpen, EventCallback.Factory.Create<SpreadsheetOpenEventArgs>(this, _ => { }))
            .Add(p => p.OnDownload, EventCallback.Factory.Create<SpreadsheetDownloadEventArgs>(this, _ => { })));

        var header = cut.Find(".tm-spreadsheet__header");

        header.QuerySelector("[data-testid='tm-spreadsheet-header-open']").Should().NotBeNull();
        header.QuerySelector("[data-testid='tm-spreadsheet-header-download']").Should().NotBeNull();

        // The grid toggle is the core TmToggle (a real switch), named by the localized label.
        header.QuerySelector(".tm-toggle-wrapper").Should().NotBeNull("the header grid toggle is the core TmToggle");
        header.QuerySelector("input.tm-toggle-input[role='switch']").Should().NotBeNull();
        header.QuerySelector("svg").Should().NotBeNull();
        header.TextContent.Should().Contain("Show grid lines");
    }

    [Fact]
    public void Spreadsheet_Header_FileActions_HiddenWithoutDelegates()
    {
        var cut = Render<TmSpreadsheet>(parameters => parameters
            .Add(p => p.ShowHeader, true));

        cut.FindAll("[data-testid='tm-spreadsheet-header-open']").Should().BeEmpty(
            "the built-in Open action only renders for a host that listens to OnOpen");
        cut.FindAll("[data-testid='tm-spreadsheet-header-download']").Should().BeEmpty();
        cut.FindAll(".tm-spreadsheet__header input.tm-toggle-input").Count.Should().Be(1);
    }

    [Fact]
    public void Spreadsheet_Header_GridToggle_TogglesTheActiveSheet()
    {
        var cut = Render<TmSpreadsheet>(parameters => parameters
            .Add(p => p.ShowHeader, true));

        cut.Instance.Workbook.ActiveSheet!.ShowGridLines.Should().BeTrue();
        cut.Find(".tm-spreadsheet__header input.tm-toggle-input").Change(false);
        cut.Instance.Workbook.ActiveSheet!.ShowGridLines.Should().BeFalse();
    }

    [Fact]
    public void Spreadsheet_ToolbarSelects_BindTheActiveStyle_NotALiteralParameterName()
    {
        var cut = Render<TmSpreadsheet>();
        var toolbar = cut.FindComponent<TmSpreadsheetToolbar>();

        // The font/size/align/number-format parameters are EXPRESSIONS of the active style. A missing
        // '@' turns them into the literal C# identifier, which matches no option and renders a blank
        // select (the reported empty font/number-format dropdowns).
        toolbar.Instance.SelectedFontFamily.Should().NotBeNullOrEmpty();
        toolbar.Instance.SelectedFontSize.Should().NotBeNullOrEmpty();
        toolbar.Instance.SelectedNumberFormat.Should().Be("General");
        toolbar.Instance.SelectedHorizontalAlign.Should().NotBeNullOrEmpty();

        var sheet = cut.Instance.Workbook.ActiveSheet!;
        sheet.ActiveCellRef = "A1";
        sheet.Cells["A1"] = new SpreadsheetCell
        {
            Value = "styled",
            Style = new SpreadsheetCellStyle
            {
                FontFamily = "Courier New",
                FontSize = 14,
                NumberFormat = "0.00",
                HorizontalAlign = SpreadsheetHorizontalAlign.Center,
                VerticalAlign = SpreadsheetVerticalAlign.Middle,
            }
        };
        cut.Render();

        var refreshed = cut.FindComponent<TmSpreadsheetToolbar>().Instance;
        refreshed.SelectedFontFamily.Should().Be("Courier New");
        refreshed.SelectedFontSize.Should().Be("14");
        refreshed.SelectedNumberFormat.Should().Be("0.00");
        refreshed.SelectedHorizontalAlign.Should().Be("center");
        refreshed.SelectedVerticalAlign.Should().Be("middle");
    }

    [Fact]
    public void Spreadsheet_VerticalAlign_RaisesAndAppliesTheStyle()
    {
        var cut = Render<TmSpreadsheet>();
        var sheet = cut.Instance.Workbook.ActiveSheet!;
        sheet.ActiveCellRef = "A1";
        sheet.Cells["A1"] = new SpreadsheetCell { Value = "x" };

        var toolbar = cut.FindComponent<TmSpreadsheetToolbar>();
        toolbar.InvokeAsync(() => toolbar.Instance.OnVerticalAlignChanged.InvokeAsync("middle"));
        cut.WaitForAssertion(() =>
            sheet.Cells["A1"].Style!.VerticalAlign.Should().Be(SpreadsheetVerticalAlign.Middle));
    }

    // ── Review round: header custom tools + name box ranges/navigation ──

    [Fact]
    public void Spreadsheet_Header_RendersFileCustomToolsAsHeaderActions()
    {
        SpreadsheetCustomTool? clicked = null;
        var tools = new List<SpreadsheetCustomTool>
        {
            new() { Id = "file-tool", IconName = "download", Title = "Archive", Tab = "file", Order = 0 },
            new() { Id = "home-tool", IconName = "copy", Title = "Home tool", Tab = "Home", Order = 0 },
        };

        var cut = Render<TmSpreadsheet>(parameters => parameters
            .Add(p => p.ShowHeader, true)
            .Add(p => p.CustomTools, tools)
            .Add(p => p.OnCustomToolClick, EventCallback.Factory.Create<SpreadsheetCustomTool>(this, tool => clicked = tool)));

        var header = cut.Find(".tm-spreadsheet__header");
        var headerTool = header.QuerySelector("[data-tool-id='file-tool']");
        headerTool.Should().NotBeNull("a File-tab custom tool has no File tab with the header on");
        headerTool!.TextContent.Should().Contain("Archive");
        header.QuerySelector("[data-tool-id='home-tool']").Should().BeNull(
            "Home tools stay in the ribbon");

        headerTool.Click();
        cut.WaitForAssertion(() => clicked.Should().BeSameAs(tools[0]));
    }

    [Fact]
    public void Spreadsheet_NameBox_OffersWorkbookAndActiveSheetRanges_SheetScopeWins()
    {
        var workbook = new SpreadsheetWorkbook();
        workbook.Sheets.Add(new SpreadsheetSheet { Name = "Sheet2" });
        workbook.NamedRanges.Add(new SpreadsheetNamedRange { Name = "Total", RefersTo = "=A1", Scope = NamedRangeScope.Workbook });
        workbook.NamedRanges.Add(new SpreadsheetNamedRange { Name = "Total", RefersTo = "=B2", Scope = NamedRangeScope.Sheet, SheetIndex = 0 });
        workbook.NamedRanges.Add(new SpreadsheetNamedRange { Name = "Local", RefersTo = "=C3", Scope = NamedRangeScope.Sheet, SheetIndex = 0 });
        workbook.NamedRanges.Add(new SpreadsheetNamedRange { Name = "OtherSheetOnly", RefersTo = "=D4", Scope = NamedRangeScope.Sheet, SheetIndex = 1 });

        var cut = Render<TmSpreadsheet>(parameters => parameters.Add(p => p.InitialWorkbook, workbook));

        var names = cut.FindComponent<TmSpreadsheetFormulaBar>().Instance.NamedRanges!
            .Select(range => range.Name)
            .ToArray();
        names.Should().Equal("Local", "Total");
        cut.FindComponent<TmSpreadsheetFormulaBar>().Instance.NamedRanges!
            .Single(range => range.Name == "Total")
            .RefersTo.Should().Be("=B2", "the sheet-scoped name wins over the workbook one");

        cut.InvokeAsync(() => cut.Instance.Workbook.ActiveSheetIndex = 1);
        cut.Render();

        var sheet2Names = cut.FindComponent<TmSpreadsheetFormulaBar>().Instance.NamedRanges!
            .Select(range => range.Name)
            .ToArray();
        sheet2Names.Should().Equal("OtherSheetOnly", "Total");
        cut.FindComponent<TmSpreadsheetFormulaBar>().Instance.NamedRanges!
            .Single(range => range.Name == "Total")
            .Scope.Should().Be(NamedRangeScope.Workbook);
    }

    [Fact]
    public void Spreadsheet_NameBox_SheetQualifiedReference_SwitchesTheActiveSheet()
    {
        var workbook = new SpreadsheetWorkbook();
        workbook.Sheets.Add(new SpreadsheetSheet { Name = "Sheet2" });

        var cut = Render<TmSpreadsheet>(parameters => parameters.Add(p => p.InitialWorkbook, workbook));
        cut.Instance.Workbook.ActiveSheetIndex.Should().Be(0);

        var formulaBar = cut.FindComponent<TmSpreadsheetFormulaBar>().Instance;
        cut.InvokeAsync(() => formulaBar.OnNavigateToRef.InvokeAsync("Sheet2!B2"));

        cut.WaitForAssertion(() => cut.Instance.Workbook.ActiveSheetIndex.Should().Be(1));
    }
}
