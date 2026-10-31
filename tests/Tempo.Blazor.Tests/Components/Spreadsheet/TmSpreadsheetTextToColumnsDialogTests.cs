using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Components.Spreadsheet.Data;
using Tempo.Blazor.Components.Spreadsheet.Dialogs;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.Spreadsheet;

public class TmSpreadsheetTextToColumnsDialogTests : LocalizationTestBase
{
    private static readonly string[] Sample = ["Jan;Novak;Praha", "Petr;Svoboda;Brno"];

    [Fact]
    public void Step1_ShowsModeChoices_Localized()
    {
        var cut = Render<TmSpreadsheetTextToColumnsDialog>(p => p
            .Add(c => c.SourceRows, Sample));

        cut.Markup.Should().Contain("Text to columns");
        cut.Markup.Should().Contain("Delimited");
        cut.Markup.Should().Contain("Fixed width");
    }

    [Fact]
    public void Next_AdvancesToStep2_AndShowsDelimiters()
    {
        var cut = Render<TmSpreadsheetTextToColumnsDialog>(p => p
            .Add(c => c.SourceRows, Sample));

        cut.Find(".tm-spreadsheet-t2c__btn--ok").Click(); // Next

        cut.Markup.Should().Contain("Delimiters");
        cut.Markup.Should().Contain("Semicolon");
    }

    [Fact]
    public void LivePreview_UpdatesWhenSemicolonChosen()
    {
        var cut = Render<TmSpreadsheetTextToColumnsDialog>(p => p
            .Add(c => c.SourceRows, Sample));

        cut.Find(".tm-spreadsheet-t2c__btn--ok").Click(); // → step 2 (comma default, no split on ';')

        // Enable semicolon delimiter.
        var semicolon = cut.FindAll(".tm-spreadsheet-t2c__delims input[type=checkbox]")[1];
        semicolon.Change(true);

        // Preview now splits into 3 columns → each preview row has 3 cells.
        var firstRowCells = cut.FindAll(".tm-spreadsheet-t2c__preview-table tbody tr")[0].QuerySelectorAll("td");
        firstRowCells.Length.Should().Be(3);
        cut.Markup.Should().Contain("Novak");
    }

    [Fact]
    public void Finish_ReturnsOptionsAndFormats()
    {
        SpreadsheetTextToColumnsResult? result = null;
        var cut = Render<TmSpreadsheetTextToColumnsDialog>(p => p
            .Add(c => c.SourceRows, Sample)
            .Add(c => c.OnApply, EventCallback.Factory.Create<SpreadsheetTextToColumnsResult>(this, r => result = r)));

        cut.Find(".tm-spreadsheet-t2c__btn--ok").Click(); // step 2
        cut.FindAll(".tm-spreadsheet-t2c__delims input[type=checkbox]")[1].Change(true); // semicolon
        cut.Find(".tm-spreadsheet-t2c__btn--ok").Click(); // step 3
        cut.Find(".tm-spreadsheet-t2c__btn--ok").Click(); // Finish

        result.Should().NotBeNull();
        result!.Options.Semicolon.Should().BeTrue();
        result.Formats.Should().HaveCount(3);
    }
}
