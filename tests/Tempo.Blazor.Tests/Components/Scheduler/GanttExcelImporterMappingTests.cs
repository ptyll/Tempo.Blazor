using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using FluentAssertions;
using Tempo.Blazor.Abstractions.Models;
using Tempo.Blazor.Abstractions.WorkItems;
using Tempo.Blazor.Components.Scheduler;
using Tempo.Blazor.Services;
using Tempo.Blazor.Tests.Localization;
using Xunit;

namespace Tempo.Blazor.Tests.Components.Scheduler;

/// <summary>
/// FZE-10: the WBS / Predecessor / Assignees mapping targets of <see cref="TmGanttImportMappingDialog"/>
/// must survive the whole trip through <see cref="GanttExcelImporter"/> into Gantt data — a hierarchy
/// from WBS codes, dependencies (with type and lag) from predecessor tokens and people from assignee
/// cells — with every data-level problem reported as a localized-later warning, never an exception.
/// Culture-independent: dates are ISO strings, ids and names are raw strings.
/// </summary>
public class GanttExcelImporterMappingTests : LocalizationTestBase
{
    // ── WBS → ParentId ────────────────────────────────────────────

    [Fact]
    public void Import_Wbs_Column_Builds_ParentId_Hierarchy()
    {
        using var stream = Xlsx(
            [WBS, TaskName, Start, Finish],
            ["1", "Foundation", "2024-06-03", "2024-06-05"],
            ["1.1", "Walls", "2024-06-05", "2024-06-12"],
            ["1.1.2", "Plaster", "2024-06-10", "2024-06-14"],
            ["2.9", "Orphan parent", "2024-06-04", "2024-06-06"]);

        var result = GanttExcelImporter.ImportResult(stream, Mappings);

        var tasks = result.Tasks;
        tasks.Should().HaveCount(4);
        var foundation = tasks.Single(t => t.Title == "Foundation");
        var walls = tasks.Single(t => t.Title == "Walls");
        var plaster = tasks.Single(t => t.Title == "Plaster");
        var orphan = tasks.Single(t => t.Title == "Orphan parent");

        foundation.ParentId.Should().BeNull("WBS '1' has no parent code");
        walls.ParentId.Should().Be(foundation.Id, "WBS '1.1' nests under '1'");
        plaster.ParentId.Should().Be(walls.Id, "WBS '1.1.2' nests under '1.1'");
        orphan.ParentId.Should().BeNull("parent WBS '2' has no row, so the task stays a root");

        var warning = result.Warnings.Should().ContainSingle().Subject;
        warning.Code.Should().Be(GanttImportWarningCode.UnknownWbsParent);
        warning.Value.Should().Be("2", "the missing parent code is carried, not a ready-made message");
        warning.Row.Should().Be(4);
        warning.Column.Should().Be(WBS);
        result.Dependencies.Should().BeEmpty();
    }

    [Fact]
    public void Import_Wbs_Out_Of_Order_Rows_Still_Resolve()
    {
        using var stream = Xlsx(
            [WBS, TaskName, Start, Finish],
            ["1.1", "Child first", "2024-06-05", "2024-06-12"],
            ["1", "Parent later", "2024-06-03", "2024-06-05"]);

        var result = GanttExcelImporter.ImportResult(stream, Mappings);

        var parent = result.Tasks.Single(t => t.Title == "Parent later");
        result.Tasks.Single(t => t.Title == "Child first").ParentId.Should().Be(parent.Id,
            "the hierarchy is built in a second pass, so the parent may come later in the sheet");
        result.Warnings.Should().BeEmpty();
    }

    // ── Assignees → people ────────────────────────────────────────

    [Fact]
    public void Import_Assignees_Splits_On_Comma_And_Semicolon_Trims_And_Dedupes()
    {
        using var stream = Xlsx(
            [TaskName, Start, Finish, AssigneesHeader],
            ["Shared", "2024-06-03", "2024-06-05", "  Alice , Bob;Alice;;  "]);

        var result = GanttExcelImporter.ImportResult(stream, Mappings);

        var assignees = result.Tasks.Single().Assignees;
        assignees.Should().HaveCount(2, "the repeated 'Alice' and the empty fragments collapse");
        assignees.Select(a => (a.Id, a.Name)).Should().Equal(
            ("Alice", "Alice"),
            ("Bob", "Bob"));
    }

    [Fact]
    public void Import_Assignees_Uses_Resolver_When_Supplied()
    {
        using var stream = Xlsx(
            [TaskName, Start, Finish, AssigneesHeader],
            ["Staffed", "2024-06-03", "2024-06-05", "alice, ghost, bob"]);

        var options = new GanttExcelImportOptions
        {
            AssigneeResolver = name => name == "ghost"
                ? null
                : new TmWorkItemAssignee { Id = "u-" + name, Name = name.ToUpperInvariant() },
        };

        var result = GanttExcelImporter.ImportResult(stream, Mappings, options);

        // A null answer skips the name instead of inventing a person.
        result.Tasks.Single().Assignees.Select(a => (a.Id, a.Name)).Should().Equal(
            ("u-alice", "ALICE"),
            ("u-bob", "BOB"));
    }

    // ── Predecessors → dependencies ───────────────────────────────

    [Fact]
    public void Import_Predecessor_Row_Number_FS_Default()
    {
        using var stream = Xlsx(
            [TaskName, Start, Finish, PredecessorsHeader],
            ["First", "2024-06-03", "2024-06-05", string.Empty],
            ["Second", "2024-06-04", "2024-06-06", string.Empty],
            ["Third", "2024-06-05", "2024-06-07", string.Empty],
            ["Fourth", "2024-06-06", "2024-06-08", "3"]);

        var result = GanttExcelImporter.ImportResult(stream, Mappings);

        var dep = result.Dependencies.Should().ContainSingle().Subject;
        dep.FromId.Should().Be("xlsx-row-3", "a bare number references the sheet row (1 = first data row)");
        dep.ToId.Should().Be("xlsx-row-4");
        dep.DepType.Should().Be(GanttDependencyType.FinishToStart, "FS is the default type");
        dep.LagDays.Should().Be(0);
        result.Warnings.Should().BeEmpty();
    }

    [Fact]
    public void Import_Predecessor_Parses_Type_And_Lag()
    {
        using var stream = Xlsx(
            [TaskName, Start, Finish, PredecessorsHeader],
            ["T1", "2024-06-03", "2024-06-05", string.Empty],
            ["T2", "2024-06-04", "2024-06-06", string.Empty],
            ["T3", "2024-06-05", "2024-06-07", string.Empty],
            ["T4", "2024-06-06", "2024-06-08", string.Empty],
            ["T5", "2024-06-07", "2024-06-09", "3FS+2d, 1SS; 4FF-1 ;2SF"]);

        var result = GanttExcelImporter.ImportResult(stream, Mappings);

        result.Dependencies.Should().HaveCount(4);
        result.Dependencies.Should().OnlyContain(d => d.ToId == "xlsx-row-5");
        var byType = result.Dependencies.ToDictionary(d => d.DepType);
        byType[GanttDependencyType.FinishToStart].FromId.Should().Be("xlsx-row-3");
        byType[GanttDependencyType.FinishToStart].LagDays.Should().Be(2, "the 'd' suffix is optional");
        byType[GanttDependencyType.StartToStart].FromId.Should().Be("xlsx-row-1");
        byType[GanttDependencyType.StartToStart].LagDays.Should().Be(0);
        byType[GanttDependencyType.FinishToFinish].FromId.Should().Be("xlsx-row-4");
        byType[GanttDependencyType.FinishToFinish].LagDays.Should().Be(-1, "a negative lag is a lead");
        byType[GanttDependencyType.StartToFinish].FromId.Should().Be("xlsx-row-2");
        result.Warnings.Should().BeEmpty();
    }

    [Fact]
    public void Import_Predecessor_By_Wbs_Code()
    {
        using var stream = Xlsx(
            [WBS, TaskName, Start, Finish, PredecessorsHeader],
            ["1", "A", "2024-06-03", "2024-06-05", string.Empty],
            ["1.1", "B", "2024-06-04", "2024-06-06", string.Empty],
            ["1.2", "C", "2024-06-05", "2024-06-07", string.Empty],
            ["1.3", "D", "2024-06-06", "2024-06-08", "1.2FS"]);

        var result = GanttExcelImporter.ImportResult(stream, Mappings);

        var dep = result.Dependencies.Should().ContainSingle().Subject;
        dep.FromId.Should().Be(result.Tasks.Single(t => t.Title == "C").Id, "a dotted reference is a WBS code");
        dep.ToId.Should().Be(result.Tasks.Single(t => t.Title == "D").Id);
        dep.DepType.Should().Be(GanttDependencyType.FinishToStart);
        result.Warnings.Should().BeEmpty();
    }

    [Fact]
    public void Import_Predecessor_Unknown_Or_Self_Or_Cycle_Produces_Warning_Not_Exception()
    {
        using var stream = Xlsx(
            [TaskName, Start, Finish, PredecessorsHeader],
            ["T1", "2024-06-03", "2024-06-05", "99"],
            ["T2", "2024-06-04", "2024-06-06", "2"],
            ["T3", "2024-06-05", "2024-06-07", "4"],
            ["T4", "2024-06-06", "2024-06-08", "3"]);

        var result = GanttExcelImporter.ImportResult(stream, Mappings);

        // The unknown predecessor and the self reference produce no edge at all.
        result.Dependencies.Should().ContainSingle("4→3 stays; the later edge 3→4 that would close the cycle is dropped");
        result.Dependencies[0].FromId.Should().Be("xlsx-row-4");
        result.Dependencies[0].ToId.Should().Be("xlsx-row-3");

        result.Warnings.Select(w => w.Code).Should().Equal(
            GanttImportWarningCode.UnknownPredecessor,
            GanttImportWarningCode.SelfDependency,
            GanttImportWarningCode.CircularDependency);
        result.Warnings[0].Value.Should().Be("99");
        result.Warnings[0].Row.Should().Be(1);
        result.Warnings[1].Row.Should().Be(2);
        result.Warnings[2].Row.Should().Be(4, "the warning names the row whose edge was skipped");
        result.Warnings[2].Value.Should().Be("3");
    }

    // ── Backward compatibility and the sparse-cell fix ────────────

    [Fact]
    public void Import_Without_Mappings_Keeps_Todays_Default_Headers()
    {
        using var standard = Xlsx(
            ["Title", "Start", "Finish", "Progress", "Status", "Priority"],
            ["Legacy", "2024-01-01", "2024-01-05", "50", "InProgress", "High"]);

        var legacy = GanttExcelImporter.Import(standard);
        var task = legacy.Should().ContainSingle().Subject;
        task.Title.Should().Be("Legacy");
        task.Start.Should().Be(new DateTime(2024, 1, 1));
        task.End.Should().Be(new DateTime(2024, 1, 5));
        task.PercentComplete.Should().Be(50);
        task.Status.Should().Be(TmWorkItemStatus.InProgress);
        task.Priority.Should().Be(TmWorkItemPriority.High);

        // The new default headers are part of the same no-mappings vocabulary.
        using var extended = Xlsx(
            ["Title", "Start", "Finish", WBS, PredecessorsHeader, AssigneesHeader],
            ["Root", "2024-06-03", "2024-06-05", "1", string.Empty, "Alice"],
            ["Leaf", "2024-06-06", "2024-06-08", "1.1", "1FS", "Bob"]);

        var result = GanttExcelImporter.ImportResult(extended);
        var root = result.Tasks.Single(t => t.Title == "Root");
        var leaf = result.Tasks.Single(t => t.Title == "Leaf");
        leaf.ParentId.Should().Be(root.Id);
        result.Dependencies.Should().ContainSingle();
        result.Dependencies[0].FromId.Should().Be(root.Id);
        result.Dependencies[0].ToId.Should().Be(leaf.Id);
        result.Tasks.SelectMany(t => t.Assignees).Select(a => a.Name).Should().Equal("Alice", "Bob");
    }

    [Fact]
    public void ReadHeaders_Returns_First_Row()
    {
        using var stream = Xlsx(
            [TaskName, Start, Finish, WBS],
            ["Ignored", "2024-06-03", "2024-06-05", "1"]);

        GanttExcelImporter.ReadHeaders(stream).Should().Equal([TaskName, Start, Finish, WBS]);
    }

    [Fact]
    public void Import_Sparse_Row_Keeps_Column_Alignment()
    {
        // The Finish cell sits in column C; the Start cell (column B) is simply not in the file.
        // A positional reader slides "2024-01-05" onto Start — the CellReference index must not.
        using var stream = Xlsx(
            [(0u, TaskName), (1u, Start), (2u, Finish)],
            [[(0u, "Sparse"), (2u, "2024-01-05")]]);

        var result = GanttExcelImporter.ImportResult(stream, Mappings);

        var task = result.Tasks.Should().ContainSingle().Subject;
        task.End.Should().Be(new DateTime(2024, 1, 5));
        task.Start.Should().Be(default(DateTime), "the missing Start cell stays empty instead of stealing the Finish value");
    }

    [Fact]
    public void Import_Assignees_Feed_The_Workload_Calculator()
    {
        using var stream = Xlsx(
            [TaskName, Start, Finish, AssigneesHeader],
            ["Shared", "2024-06-03", "2024-06-05", "Alice"]);

        var result = GanttExcelImporter.ImportResult(stream, Mappings);

        var cut = Render<TmGantt>(p => p
            .Add(c => c.Items, result.Tasks)
            .Add(c => c.View, GanttView.Workload));

        cut.Find("[data-testid='workload-row-Alice']").Should().NotBeNull(
            "the import's assignee name becomes the workload person id, so imported people are planned");
    }

    // ── Test fixtures ─────────────────────────────────────────────

    private const string TaskName = "Task Name";
    private const string Start = "Start";
    private const string Finish = "Finish";
    private const string WBS = "wbs";
    private const string PredecessorsHeader = "predecessors";
    private const string AssigneesHeader = "assignees";
    private const string ProgressHeader = "progress";
    private const string StatusHeader = "status";
    private const string PriorityHeader = "priority";

    /// <summary>The mapping the column-mapping dialog confirms for these headers.</summary>
    private static GanttColumnMapping[] Mappings =>
    [
        new() { SourceColumn = TaskName,           TargetProperty = GanttColumnKey.Title },
        new() { SourceColumn = Start,              TargetProperty = GanttColumnKey.Start },
        new() { SourceColumn = Finish,             TargetProperty = GanttColumnKey.End },
        new() { SourceColumn = WBS,                TargetProperty = GanttColumnKey.WBS },
        new() { SourceColumn = PredecessorsHeader, TargetProperty = GanttColumnKey.Predecessor },
        new() { SourceColumn = AssigneesHeader,    TargetProperty = GanttColumnKey.Assignees },
    ];

    private static Stream Xlsx(string[] headers, params string[][] rows)
        => BuildXlsx(
            headers.Select((header, index) => ((uint)index, header)),
            rows.Select(row => row.Select((value, index) => ((uint)index, value)).ToArray()));

    private static Stream Xlsx(IEnumerable<(uint Column, string Value)> headers, params (uint Column, string Value)[][] rows)
        => BuildXlsx(headers, rows);

    private static Stream BuildXlsx(IEnumerable<(uint Column, string Value)> headers, IEnumerable<(uint Column, string Value)[]> rows)
    {
        var ms = new MemoryStream();
        using (var doc = SpreadsheetDocument.Create(ms, SpreadsheetDocumentType.Workbook, true))
        {
            var wbp = doc.AddWorkbookPart();
            wbp.Workbook = new Workbook();

            var wsp = wbp.AddNewPart<WorksheetPart>();
            var sd = new SheetData();
            wsp.Worksheet = new Worksheet(sd);

            sd.Append(Row(1U, headers));
            var ordinal = 2U;
            foreach (var row in rows)
            {
                sd.Append(Row(ordinal, row));
                ordinal++;
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
