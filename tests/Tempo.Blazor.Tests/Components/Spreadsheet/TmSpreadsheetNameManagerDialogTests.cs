using System.Linq;
using Bunit;
using FluentAssertions;
using Tempo.Blazor.Components.Spreadsheet.Dialogs;
using Tempo.Blazor.Components.Spreadsheet.Models;
using Tempo.Blazor.Tests.Localization;
using Xunit;

namespace Tempo.Blazor.Tests.Components.Spreadsheet;

public class TmSpreadsheetNameManagerDialogTests : LocalizationTestBase
{
    [Fact]
    public void Renders_TitleAndColumns()
    {
        var workbook = new SpreadsheetWorkbook();
        workbook.NamedRanges.Add(new SpreadsheetNamedRange { Name = "Sales", RefersTo = "A1:A10" });

        var cut = Render<TmSpreadsheetNameManagerDialog>(
            parameters => parameters.Add(p => p.Workbook, workbook));

        cut.Find(".tm-modal-title").TextContent.Should().Be("Name Manager");
        cut.FindAll("th").Count.Should().Be(5);
    }

    [Fact]
    public void Ranges_AreListed()
    {
        var workbook = new SpreadsheetWorkbook();
        workbook.NamedRanges.Add(new SpreadsheetNamedRange { Name = "Sales", RefersTo = "A1:A10" });
        workbook.NamedRanges.Add(new SpreadsheetNamedRange { Name = "Tax", RefersTo = "B1", Scope = NamedRangeScope.Sheet, SheetIndex = 0 });

        var cut = Render<TmSpreadsheetNameManagerDialog>(
            parameters => parameters.Add(p => p.Workbook, workbook));

        var rows = cut.FindAll("tbody tr");
        rows.Count.Should().Be(2);
        rows[0].TextContent.Should().Contain("Sales");
        rows[1].TextContent.Should().Contain("Tax");
    }

    [Fact]
    public void Filter_ReducesResults()
    {
        var workbook = new SpreadsheetWorkbook();
        workbook.NamedRanges.Add(new SpreadsheetNamedRange { Name = "Sales", RefersTo = "A1" });
        workbook.NamedRanges.Add(new SpreadsheetNamedRange { Name = "Costs", RefersTo = "B1" });

        var cut = Render<TmSpreadsheetNameManagerDialog>(
            parameters => parameters.Add(p => p.Workbook, workbook));

        var filterInput = cut.Find(".tm-spreadsheet-name-manager__filter input");
        filterInput.Input("Sales");

        var rows = cut.FindAll("tbody tr");
        rows.Count.Should().Be(1);
        rows[0].TextContent.Should().Contain("Sales");
    }

    [Fact]
    public void SelectRow_HighlightsAndEnablesButtons()
    {
        var workbook = new SpreadsheetWorkbook();
        workbook.NamedRanges.Add(new SpreadsheetNamedRange { Name = "Sales", RefersTo = "A1" });

        var cut = Render<TmSpreadsheetNameManagerDialog>(
            parameters => parameters.Add(p => p.Workbook, workbook));

        cut.Find("tbody tr").Click();

        cut.Find("tbody tr").ClassName.Should().Contain("tm-spreadsheet-name-manager__row--selected");
        cut.FindAll("button").First(b => b.TextContent.Contains("Edit")).HasAttribute("disabled").Should().BeFalse();
        cut.FindAll("button").First(b => b.TextContent.Contains("Delete")).HasAttribute("disabled").Should().BeFalse();
    }

    [Fact]
    public void NewClick_RaisesEvent()
    {
        var workbook = new SpreadsheetWorkbook();
        var fired = false;

        var cut = Render<TmSpreadsheetNameManagerDialog>(
            parameters => parameters
                .Add(p => p.Workbook, workbook)
                .Add(p => p.OnNew, () => fired = true));

        cut.FindAll("button").First(b => b.TextContent.Contains("New")).Click();
        fired.Should().BeTrue();
    }

    [Fact]
    public void DeleteClick_RaisesEvent_WithSelectedRange()
    {
        var workbook = new SpreadsheetWorkbook();
        var range = new SpreadsheetNamedRange { Name = "Sales", RefersTo = "A1" };
        workbook.NamedRanges.Add(range);
        SpreadsheetNamedRange? deleted = null;

        var cut = Render<TmSpreadsheetNameManagerDialog>(
            parameters => parameters
                .Add(p => p.Workbook, workbook)
                .Add(p => p.OnDelete, r => deleted = r));

        cut.Find("tbody tr").Click();
        cut.FindAll("button").First(b => b.TextContent.Contains("Delete")).Click();

        deleted.Should().NotBeNull();
        deleted!.Name.Should().Be("Sales");
    }
}
