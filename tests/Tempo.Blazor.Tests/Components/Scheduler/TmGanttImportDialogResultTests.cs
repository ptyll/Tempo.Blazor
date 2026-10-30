using Bunit;
using FluentAssertions;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Tempo.Blazor.Abstractions.Models;
using Tempo.Blazor.Components.Scheduler;
using Tempo.Blazor.Tests.Localization;
using Xunit;

namespace Tempo.Blazor.Tests.Components.Scheduler;

/// <summary>
/// FZE-10 UI wiring: the import dialog's Excel flow of a result-capable GanttXlsx package —
/// headers → column mapping → full result (tasks, dependencies, warnings). The mapping targets the
/// dialog offers finally reach Gantt data, and warnings keep the dialog open with an alert instead
/// of silently vanishing. Culture is pinned through the mock localizer (en).
/// </summary>
public class TmGanttImportDialogResultTests : LocalizationTestBase
{
    [Fact]
    public void ImportDialog_Excel_Import_Reads_Headers_And_Opens_The_Mapping_Dialog()
    {
        var cut = RenderDialog();
        Upload(cut, XlsxBytes(Sheet()));

        cut.Find("[data-testid='import-submit']").Click();

        cut.Find(".tm-gantt__import-mapping-dialog").Should().NotBeNull("the mapping step rides its own modal");
        cut.FindAll(".tm-gantt__mapping-row").Should().HaveCount(SheetHeaders.Length,
            "every header of the chosen file gets a mapping row");
    }

    [Fact]
    public async Task ImportDialog_Mapping_Confirmed_Raises_The_Full_Result()
    {
        GanttImportResult? result = null;
        var closed = 0;
        var cut = RenderDialog(p => p
            .Add(x => x.OnImportResultCompleted, EventCallback.Factory.Create<GanttImportResult>(this, r => result = r))
            .Add(x => x.OnClose, () => closed++));
        Upload(cut, XlsxBytes(Sheet()));

        cut.Find("[data-testid='import-submit']").Click();
        cut.Find("[data-testid='mapping-submit']").Click();

        result.Should().NotBeNull();
        result!.Tasks.Should().HaveCount(2);
        result.Dependencies.Should().ContainSingle("row 2's predecessor '1' becomes an edge");
        result.Warnings.Should().BeEmpty();
        closed.Should().Be(1, "a clean import closes the dialog");
    }

    [Fact]
    public void ImportDialog_Warnings_Keep_The_Dialog_Open_With_A_Localized_Alert()
    {
        GanttImportResult? result = null;
        var closed = 0;
        var cut = RenderDialog(p => p
            .Add(x => x.OnImportResultCompleted, EventCallback.Factory.Create<GanttImportResult>(this, r => result = r))
            .Add(x => x.OnClose, () => closed++));
        // Row 1 references predecessor 9, which does not exist.
        Upload(cut, XlsxBytes(Sheet(predecessors: ["9", string.Empty])));

        cut.Find("[data-testid='import-submit']").Click();
        cut.Find("[data-testid='mapping-submit']").Click();

        result.Should().NotBeNull();
        result!.Tasks.Should().HaveCount(2, "warnings never block the import");
        result.Warnings.Should().ContainSingle(w => w.Code == GanttImportWarningCode.UnknownPredecessor);
        closed.Should().Be(0, "the dialog stays open so the alert can be read");

        var alert = cut.Find("[data-testid='import-warnings']");
        alert.TextContent.Should().Contain("1 warning from the import");
        alert.TextContent.Should().Contain("Row 2: unknown predecessor '9'",
            "the message names the SHEET row (the header row is row 1), not the data ordinal");
        alert.QuerySelector("summary")!.TextContent.Trim().Should().Be("Show details",
            "the expander reads as an action, not as the repeated plural title");
    }

    [Fact]
    public void ImportDialog_Warnings_Footer_Shows_A_Single_Close_Button_And_Resets_The_File()
    {
        var closed = 0;
        var cut = RenderDialog(p =>
        {
            p.Add(x => x.OnImportResultCompleted, EventCallback.Factory.Create<GanttImportResult>(this, _ => { }));
            p.Add(x => x.OnClose, () => closed++);
        });
        Upload(cut, XlsxBytes(Sheet(predecessors: ["9", string.Empty])));

        cut.Find("[data-testid='import-submit']").Click();
        cut.Find("[data-testid='mapping-submit']").Click();

        cut.FindAll("[data-testid='import-submit']").Should().BeEmpty("the import is done; Import must not offer a second run");
        cut.FindAll("[data-testid='import-cancel']").Should().BeEmpty("Cancel would only re-close an already completed dialog");
        var close = cut.Find("[data-testid='import-close']");
        close.TextContent.Trim().Should().Be("Close");
        close.ClassList.Should().Contain("tm-btn-primary", "Close is the one primary action left");

        cut.Find(".tm-gantt__import-file-name").TextContent.Trim().Should().Be("No file chosen",
            "the chosen file is dropped so nothing can be re-imported from this dialog");

        close.Click();
        closed.Should().Be(1, "Close is the dialog's explicit close");
    }

    [Fact]
    public void TmGantt_Import_Raises_Tasks_Then_Dependencies_Then_The_Result()
    {
        var order = new List<string>();
        var cut = Render<TmGantt>(p => p
            .Add(x => x.Items, new List<TmWorkItem>())
            .Add(x => x.AllowDependencyCreation, true)
            .Add(x => x.OnDependencyAdded, EventCallback.Factory.Create<GanttDependency>(this, _ => order.Add("dependency")))
            .Add(x => x.OnImportCompleted, EventCallback.Factory.Create<IReadOnlyList<TmWorkItem>>(this, _ => order.Add("tasks")))
            .Add(x => x.OnImportResultCompleted, EventCallback.Factory.Create<GanttImportResult>(this, _ => order.Add("result"))));

        cut.Find("[data-testid='gantt-import-btn']").Click();
        cut.FindComponent<InputFile>().UploadFiles(InputFileContent.CreateFromBinary(
            XlsxBytes(Sheet()), "tasks.xlsx"));
        cut.Find("[data-testid='import-submit']").Click();
        cut.Find("[data-testid='mapping-submit']").Click();

        order.Should().Equal(new[] { "tasks", "dependency", "result" },
            "documented order: the tasks first, then every new edge, then the full payload");
    }

    [Fact]
    public void TmGantt_Import_Inserts_Dependencies_And_Forwards_The_Result()
    {
        var dependencies = new List<GanttDependency>();
        var completed = new List<IReadOnlyList<TmWorkItem>>();
        var results = new List<GanttImportResult>();
        var cut = Render<TmGantt>(p => p
            .Add(x => x.Items, new List<TmWorkItem>())
            .Add(x => x.AllowDependencyCreation, true)
            .Add(x => x.OnDependencyAdded, EventCallback.Factory.Create<GanttDependency>(this, dependencies.Add))
            .Add(x => x.OnImportCompleted, EventCallback.Factory.Create<IReadOnlyList<TmWorkItem>>(this, completed.Add))
            .Add(x => x.OnImportResultCompleted, EventCallback.Factory.Create<GanttImportResult>(this, results.Add)));

        cut.Find("[data-testid='gantt-import-btn']").Click();
        cut.FindComponent<InputFile>().UploadFiles(InputFileContent.CreateFromBinary(
            XlsxBytes(Sheet()), "tasks.xlsx"));
        cut.Find("[data-testid='import-submit']").Click();
        cut.Find("[data-testid='mapping-submit']").Click();

        // Every new edge is announced through OnDependencyAdded, so the host keeps one source of truth.
        dependencies.Select(d => (Ordinal(d.FromId), Ordinal(d.ToId))).Should().Equal((1, 2));
        completed.Should().ContainSingle("the task-only event keeps working for existing hosts");
        completed[0].Should().HaveCount(2);
        results.Should().ContainSingle();
        results[0].Dependencies.Should().ContainSingle();
        cut.FindAll(".tm-modal-overlay").Should().BeEmpty("a clean import closes the dialog");
    }

    [Fact]
    public void TmGantt_Import_Uses_A_Fresh_Id_Prefix_Per_Import()
    {
        var results = new List<GanttImportResult>();
        var cut = Render<TmGantt>(p => p
            .Add(x => x.Items, new List<TmWorkItem>())
            .Add(x => x.AllowDependencyCreation, true)
            .Add(x => x.OnImportResultCompleted, EventCallback.Factory.Create<GanttImportResult>(this, results.Add)));

        ImportOnce(cut);
        ImportOnce(cut);

        results.Should().HaveCount(2);
        var first = results[0].Tasks.Select(t => t.Id).ToList();
        var second = results[1].Tasks.Select(t => t.Id).ToList();
        first.Should().OnlyContain(id => TaskIdPattern.IsMatch(id),
            "the dialog mints a fresh per-import prefix (xlsx-{8 hex}-row-{n})");
        first.SequenceEqual(second).Should().BeFalse(
            "importing the same sheet twice must not mint colliding task ids");
    }

    private static void ImportOnce(IRenderedComponent<TmGantt> cut)
    {
        cut.Find("[data-testid='gantt-import-btn']").Click();
        cut.FindComponent<InputFile>().UploadFiles(InputFileContent.CreateFromBinary(
            XlsxBytes(Sheet()), "tasks.xlsx"));
        cut.Find("[data-testid='import-submit']").Click();
        cut.Find("[data-testid='mapping-submit']").Click();
    }

    /// <summary>The 1-based data-row ordinal at the end of a dialog-minted task id.</summary>
    private static int Ordinal(string taskId) => int.Parse(
        taskId[(taskId.LastIndexOf('-') + 1)..], System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>The dialog-minted task id: a fresh per-import prefix plus the data-row ordinal.</summary>
    private static readonly System.Text.RegularExpressions.Regex TaskIdPattern =
        new(@"^xlsx-[0-9a-f]{8}-row-\d+$", System.Text.RegularExpressions.RegexOptions.Compiled);

    // ── Fixtures ──────────────────────────────────────────────────

    private static readonly string[] SheetHeaders = ["Task Name", "Start", "Finish", "predecessors"];

    private static (string[][] Rows, string[] Headers) Sheet(string[]? predecessors = null) => (
        Rows:
        [
            ["Foundation", "2024-06-03", "2024-06-05", predecessors is { Length: > 0 } ? predecessors[0] : string.Empty],
            ["Walls", "2024-06-05", "2024-06-12", predecessors is { Length: > 1 } ? predecessors[1] : "1"],
        ],
        Headers: SheetHeaders);

    private IRenderedComponent<TmGanttImportDialog> RenderDialog(
        Action<ComponentParameterCollectionBuilder<TmGanttImportDialog>>? build = null)
        => Render<TmGanttImportDialog>(p =>
        {
            p.Add(x => x.IsOpen, true);
            build?.Invoke(p);
        });

    private static void Upload(IRenderedComponent<TmGanttImportDialog> cut, byte[] xlsx)
        => cut.FindComponent<InputFile>().UploadFiles(InputFileContent.CreateFromBinary(xlsx, "tasks.xlsx"));

    private static byte[] XlsxBytes((string[][] Rows, string[] Headers) sheet)
    {
        var ms = new MemoryStream();
        using (var doc = SpreadsheetDocument.Create(ms, SpreadsheetDocumentType.Workbook, true))
        {
            var wbp = doc.AddWorkbookPart();
            wbp.Workbook = new Workbook();

            var wsp = wbp.AddNewPart<WorksheetPart>();
            var sd = new SheetData();
            wsp.Worksheet = new Worksheet(sd);

            sd.Append(Row(1U, sheet.Headers.Select(h => (h, (uint)Array.IndexOf(sheet.Headers, h)))));
            var ordinal = 2U;
            foreach (var row in sheet.Rows)
            {
                sd.Append(Row(ordinal, row.Select((v, i) => (v, (uint)i))));
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

        return ms.ToArray();
    }

    private static Row Row(uint rowNumber, IEnumerable<(string Value, uint Column)> cells)
    {
        var row = new Row { RowIndex = rowNumber };
        foreach (var (value, column) in cells)
        {
            row.Append(new Cell
            {
                CellReference = $"{(char)('A' + column)}{rowNumber}",
                CellValue = new CellValue(value),
                DataType = CellValues.InlineString,
            });
        }

        return row;
    }
}
