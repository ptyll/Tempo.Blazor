using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using FluentAssertions;
using Tempo.Blazor.Abstractions.Models;
using Tempo.Blazor.Abstractions.WorkItems;
using Tempo.Blazor.Services;
using Xunit;

namespace Tempo.Blazor.Tests.Components.Scheduler;

/// <summary>
/// FZE-10 review fixes on the <see cref="GanttExcelImporter"/> parsing surface: the mapping dialog's
/// remaining targets (Duration / Deadline / Estimation / TimeLog) reach the work item, Excel serial
/// dates and "NN%" / fraction progress cells parse, every unparsable typed cell warns
/// (<see cref="GanttImportWarningCode.UnparsedValue"/> with the sheet row for display), duplicate
/// headers map the first column only, and predecessor tokens with an unparsable lag fail as unparsed
/// instead of pretending to be unknown predecessors.
/// </summary>
public class GanttExcelImporterParsingTests
{
    // ── Fix 1: Duration / Deadline / Estimation / TimeLog targets ──

    [Fact]
    public void Import_Duration_Computes_End_From_Start_When_End_Is_Missing()
    {
        using var stream = Xlsx(
            [TaskName, Start, "duration"],
            ["Foundation", "2024-06-03", "5"]);

        var task = GanttExcelImporter.ImportResult(stream, Mappings).Tasks.Should().ContainSingle().Subject;

        task.End.Should().Be(new DateTime(2024, 6, 8), "End = Start + the duration in days");
    }

    [Fact]
    public void Import_Duration_Fractional_Days_Stay_Invariant()
    {
        using var stream = Xlsx(
            [TaskName, Start, "duration"],
            ["Pouring", "2024-06-03", "2.5"]);

        var task = GanttExcelImporter.ImportResult(stream, Mappings).Tasks.Should().ContainSingle().Subject;

        task.End.Should().Be(new DateTime(2024, 6, 5, 12, 0, 0), "2.5 days are read with the invariant decimal point");
    }

    [Fact]
    public void Import_Duration_Does_Not_Overwrite_A_Parsed_End()
    {
        using var stream = Xlsx(
            [TaskName, Start, Finish, "duration"],
            ["Foundation", "2024-06-03", "2024-06-05", "30"]);

        var result = GanttExcelImporter.ImportResult(stream, Mappings);

        result.Tasks.Single().End.Should().Be(new DateTime(2024, 6, 5), "an explicit End wins over the duration fallback");
        result.Warnings.Should().BeEmpty();
    }

    [Fact]
    public void Import_Duration_Unparsable_Cell_Warns_And_End_Stays_Empty()
    {
        using var stream = Xlsx(
            [TaskName, Start, "duration"],
            ["Foundation", "2024-06-03", "five days"]);

        var result = GanttExcelImporter.ImportResult(stream, Mappings);

        result.Tasks.Single().End.Should().Be(default(DateTime));
        var warning = result.Warnings.Should().ContainSingle().Subject;
        warning.Code.Should().Be(GanttImportWarningCode.UnparsedValue);
        warning.Column.Should().Be("duration");
        warning.Value.Should().Be("five days");
    }

    [Fact]
    public void Import_Deadline_Estimation_And_TimeLog_Fill_The_WorkItem()
    {
        using var stream = Xlsx(
            [TaskName, Start, Finish, "deadline", "estimation", "time log"],
            ["Foundation", "2024-06-03", "2024-06-05", "2024-07-01", "12.5", "8"]);

        var task = GanttExcelImporter.ImportResult(stream, Mappings).Tasks.Should().ContainSingle().Subject;

        task.DueDate.Should().Be(new DateTime(2024, 7, 1));
        task.EstimationHours.Should().Be(12.5, "hours are read with the invariant decimal point");
        task.LoggedHours.Should().Be(8);
    }

    [Fact]
    public void Import_Estimation_Unparsable_Cell_Warns()
    {
        using var stream = Xlsx(
            [TaskName, Start, Finish, "estimation"],
            ["Foundation", "2024-06-03", "2024-06-05", "about a week"]);

        var result = GanttExcelImporter.ImportResult(stream, Mappings);

        result.Tasks.Single().EstimationHours.Should().BeNull();
        var warning = result.Warnings.Should().ContainSingle().Subject;
        warning.Code.Should().Be(GanttImportWarningCode.UnparsedValue);
        warning.Column.Should().Be("estimation");
        warning.Value.Should().Be("about a week");
    }

    // ── Fix 2: OADate fallback, NN% / fraction progress, UnparsedValue warnings ──

    [Fact]
    public void Import_Date_Falls_Back_To_Excel_Serial_Numbers()
    {
        using var stream = Xlsx(
            [TaskName, Start, Finish],
            ["Serial", "45400", "45404"]);

        var task = GanttExcelImporter.ImportResult(stream, Mappings).Tasks.Should().ContainSingle().Subject;

        // OADate 45400 = 2024-04-18 (days since 1899-12-30, the value a raw numeric cell carries).
        task.Start.Should().Be(new DateTime(2024, 4, 18));
        task.End.Should().Be(new DateTime(2024, 4, 22));
    }

    [Fact]
    public void Import_Progress_Accepts_Percent_Signs_Fractions_And_Plain_Numbers()
    {
        using var stream = Xlsx(
            [TaskName, ProgressHeader],
            ["Signed", "50%"],
            ["Fraction", "0.5"],
            ["Plain", "50"]);

        var result = GanttExcelImporter.ImportResult(stream, Mappings);
        var tasks = result.Tasks;

        tasks.Should().HaveCount(3);
        tasks.Single(t => t.Title == "Signed").PercentComplete.Should().Be(50);
        tasks.Single(t => t.Title == "Fraction").PercentComplete.Should().Be(50,
            "a stored fraction ≤ 1 is Excel's 0–100% scale (0.5 = 50%)");
        tasks.Single(t => t.Title == "Plain").PercentComplete.Should().Be(50);
        result.Warnings.Should().BeEmpty();
    }

    [Fact]
    public void Import_Unparsable_Typed_Cells_Warn_With_Row_Column_And_Value()
    {
        using var stream = Xlsx(
            [TaskName, Start, Finish, ProgressHeader, StatusHeader, PriorityHeader],
            ["Broken", "not a date", "2024-06-05", "many", "Bogus", "Meh"],
            ["Empty ok", string.Empty, string.Empty, string.Empty, string.Empty, string.Empty]);

        var result = GanttExcelImporter.ImportResult(stream, Mappings);

        result.Tasks.Should().HaveCount(2, "warnings never block the row");
        var broken = result.Tasks.Single(t => t.Title == "Broken");
        broken.Start.Should().Be(default(DateTime));
        broken.PercentComplete.Should().Be(0);

        result.Warnings.Select(w => (w.Row, w.SheetRow, w.Column, w.Value)).Should().Equal(
            (1, 2, Start, "not a date"),
            (1, 2, ProgressHeader, "many"),
            (1, 2, StatusHeader, "Bogus"),
            (1, 2, PriorityHeader, "Meh"));
        result.Warnings.Select(w => w.Code).Should().OnlyContain(code => code == GanttImportWarningCode.UnparsedValue,
            "an empty cell is 'no data' and never warns");
    }

    [Fact]
    public void Import_Warnings_Carry_The_Sheet_Row_For_Display()
    {
        // Header on sheet row 1, a blank sheet row 2 (no Row element), data starts on sheet row 3:
        // the display row (SheetRow) follows Row.RowIndex, the data ordinal (Row) stays 1-based
        // over the PRESENT rows because predecessor references count them the same way.
        using var stream = XlsxWithRowNumbers(
            headerRow: 1,
            dataRowNumbers: [3, 4],
            [TaskName, Start, Finish, PredecessorsHeader],
            ["First", "2024-06-03", "2024-06-05", "99"],
            ["Second", "2024-06-05", "2024-06-07", string.Empty]);

        var result = GanttExcelImporter.ImportResult(stream, Mappings);

        var warning = result.Warnings.Should().ContainSingle().Subject;
        warning.Row.Should().Be(1, "the data ordinal the predecessor references use");
        warning.SheetRow.Should().Be(3, "the actual sheet row the user sees in Excel");
    }

    // ── Fix 3: duplicate headers map the first column only ──

    [Fact]
    public void Import_Duplicate_Headers_Map_The_First_Column_Only_And_Warn()
    {
        using var stream = Xlsx(
            [TaskName, Start, Start],
            ["Foundation", "2024-06-03", "2024-06-10"]);

        var result = GanttExcelImporter.ImportResult(stream, Mappings);

        var task = result.Tasks.Should().ContainSingle().Subject;
        task.Start.Should().Be(new DateTime(2024, 6, 3), "the first 'Start' column keeps the mapping");
        var warning = result.Warnings.Should().ContainSingle().Subject;
        warning.Code.Should().Be(GanttImportWarningCode.DuplicateHeader);
        warning.Value.Should().Be(Start);
        warning.SheetRow.Should().Be(1, "the header row");
    }

    [Fact]
    public void ReadHeaders_Skips_Empty_Headers()
    {
        using var stream = Xlsx(
            [TaskName, string.Empty, Finish],
            ["Ignored", string.Empty, "2024-06-05"]);

        GanttExcelImporter.ReadHeaders(stream).Should().Equal([TaskName, Finish]);
    }

    [Fact]
    public void Import_Duplicate_User_Mappings_Do_Not_Crash_And_The_First_Wins()
    {
        using var stream = Xlsx(
            [TaskName, "Note"],
            ["Foundation", "irrelevant"]);

        var mappings = new[]
        {
            new GanttColumnMapping { SourceColumn = TaskName, TargetProperty = GanttColumnKey.Title },
            new GanttColumnMapping { SourceColumn = TaskName, TargetProperty = GanttColumnKey.Progress },
        };

        var result = GanttExcelImporter.ImportResult(stream, mappings);

        // The second mapping for the same source column is ignored (and "Note" matches no
        // default header), but the list never throws.
        var task = result.Tasks.Should().ContainSingle().Subject;
        task.Title.Should().Be("Foundation", "the first mapping for a source column wins");
        task.PercentComplete.Should().Be(0);
        result.Warnings.Should().BeEmpty();
    }

    // ── Fix 4: the id prefix is an option ──

    [Fact]
    public void Import_Default_Id_Prefix_Is_Stable_Per_Row()
    {
        using var stream = Xlsx(
            [TaskName, Start, Finish, PredecessorsHeader],
            ["First", "2024-06-03", "2024-06-05", string.Empty],
            ["Second", "2024-06-05", "2024-06-07", "1"]);

        var result = GanttExcelImporter.ImportResult(stream, Mappings);

        result.Tasks.Select(t => t.Id).Should().Equal("xlsx-row-1", "xlsx-row-2");
        result.Dependencies.Single().FromId.Should().Be("xlsx-row-1");
    }

    [Fact]
    public void Import_IdPrefix_Is_Honoured_And_Row_References_Still_Resolve()
    {
        using var stream = Xlsx(
            [TaskName, Start, Finish, PredecessorsHeader],
            ["First", "2024-06-03", "2024-06-05", string.Empty],
            ["Second", "2024-06-05", "2024-06-07", "1"]);

        var result = GanttExcelImporter.ImportResult(stream, Mappings,
            new GanttExcelImportOptions { IdPrefix = "xlsx-ab12cd34-row-" });

        result.Tasks.Select(t => t.Id).Should().Equal("xlsx-ab12cd34-row-1", "xlsx-ab12cd34-row-2");
        var dep = result.Dependencies.Should().ContainSingle().Subject;
        dep.FromId.Should().Be("xlsx-ab12cd34-row-1",
            "a bare number still references the sheet row, whatever the prefix is");
        dep.ToId.Should().Be("xlsx-ab12cd34-row-2");
    }

    // ── Fix 8: predecessor dedupe and bad-lag tokens ──

    [Fact]
    public void Import_Predecessors_With_A_Bad_Lag_Warn_UnparsedValue_Not_UnknownPredecessor()
    {
        using var stream = Xlsx(
            [TaskName, Start, Finish, PredecessorsHeader],
            ["First", "2024-06-03", "2024-06-05", string.Empty],
            ["Second", "2024-06-05", "2024-06-07", "1FS+2w, 1+2 days"]);

        var result = GanttExcelImporter.ImportResult(stream, Mappings);

        result.Dependencies.Should().BeEmpty();
        result.Warnings.Select(w => w.Code).Should().Equal(
            GanttImportWarningCode.UnparsedValue,
            GanttImportWarningCode.UnparsedValue);
        result.Warnings.Select(w => w.Value).Should().Equal("1FS+2w", "1+2 days");
    }

    [Fact]
    public void Import_Predecessors_Dedupe_Same_Edge_Keep_Different_Types()
    {
        using var stream = Xlsx(
            [TaskName, Start, Finish, PredecessorsHeader],
            ["First", "2024-06-03", "2024-06-05", string.Empty],
            ["Second", "2024-06-05", "2024-06-07", "1, 1, 1SS"]);

        var result = GanttExcelImporter.ImportResult(stream, Mappings);

        result.Dependencies.Select(d => $"{d.FromId}:{d.DepType}").Should().Equal(
            new[] { "xlsx-row-1:FinishToStart", "xlsx-row-1:StartToStart" },
            "the same (from, to, type) trio is imported once");
    }

    // ── Fixtures ──────────────────────────────────────────────────

    private const string TaskName = "Task Name";
    private const string Start = "Start";
    private const string Finish = "Finish";
    private const string ProgressHeader = "progress";
    private const string StatusHeader = "status";
    private const string PriorityHeader = "priority";
    private const string PredecessorsHeader = "predecessors";

    /// <summary>The mapping the column-mapping dialog confirms for these headers.</summary>
    private static GanttColumnMapping[] Mappings =>
    [
        new() { SourceColumn = TaskName,           TargetProperty = GanttColumnKey.Title },
        new() { SourceColumn = Start,              TargetProperty = GanttColumnKey.Start },
        new() { SourceColumn = Finish,             TargetProperty = GanttColumnKey.End },
        new() { SourceColumn = "duration",         TargetProperty = GanttColumnKey.Duration },
        new() { SourceColumn = "deadline",         TargetProperty = GanttColumnKey.Deadline },
        new() { SourceColumn = "estimation",       TargetProperty = GanttColumnKey.Estimation },
        new() { SourceColumn = "time log",         TargetProperty = GanttColumnKey.TimeLog },
        new() { SourceColumn = ProgressHeader,     TargetProperty = GanttColumnKey.Progress },
        new() { SourceColumn = StatusHeader,       TargetProperty = GanttColumnKey.Status },
        new() { SourceColumn = PriorityHeader,     TargetProperty = GanttColumnKey.Priority },
        new() { SourceColumn = PredecessorsHeader, TargetProperty = GanttColumnKey.Predecessor },
    ];

    private static Stream Xlsx(string[] headers, params string[][] rows)
        => XlsxWithRowNumbers(1, Enumerable.Range(2, rows.Length).Select(i => (uint)i), headers, rows);

    private static Stream XlsxWithRowNumbers(uint headerRow, IEnumerable<uint> dataRowNumbers, string[] headers, params string[][] rows)
    {
        var ms = new MemoryStream();
        using (var doc = SpreadsheetDocument.Create(ms, SpreadsheetDocumentType.Workbook, true))
        {
            var wbp = doc.AddWorkbookPart();
            wbp.Workbook = new Workbook();

            var wsp = wbp.AddNewPart<WorksheetPart>();
            var sd = new SheetData();
            wsp.Worksheet = new Worksheet(sd);

            sd.Append(Row(headerRow, headers.Select((h, i) => ((uint)i, h))));
            foreach (var (rowNumber, values) in dataRowNumbers.Zip(rows))
            {
                sd.Append(Row(rowNumber, values.Select((v, i) => ((uint)i, v))));
            }

            wbp.Workbook.AppendChild(new Sheets()).Append(new Sheet
            {
                Id = wbp.GetIdOfPart(wsp),
                SheetId = 1,
                Name = "Tasks",
            });
            doc.Save();
        }

        ms.Position = 0;
        return ms;
    }

    private static Row Row(uint rowNumber, IEnumerable<(uint Column, string Value)> cells)
    {
        var row = new Row { RowIndex = rowNumber };
        foreach (var (column, value) in cells)
        {
            row.Append(new Cell
            {
                CellReference = CellReference(column, rowNumber),
                CellValue = new CellValue(value),
                DataType = CellValues.InlineString,
            });
        }

        return row;
    }

    private static string CellReference(uint column, uint row)
    {
        var letters = string.Empty;
        var index = (int)column;
        do
        {
            letters = (char)('A' + index % 26) + letters;
            index = index / 26 - 1;
        }
        while (index >= 0);

        return letters + row;
    }
}
