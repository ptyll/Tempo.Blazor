using System.Text;
using Bunit;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using Tempo.Blazor.Components.DataTable;
using Tempo.Blazor.Configuration;
using Tempo.Blazor.Export;
using Tempo.Blazor.Interfaces;
using Tempo.Blazor.Models;
using Tempo.Blazor.Tests.Localization;
using Xunit;

namespace Tempo.Blazor.Tests.DataTable;

/// <summary>Export: CSV exporter, XLSX exporter, and the component's all-rows export snapshot.</summary>
public class TmDataTableExportTests : LocalizationTestBase
{
    public record Person(string Name, int Age);

    private sealed class RecordingProvider(IReadOnlyList<Person> allRows) : IDataTableDataProvider<Person>
    {
        public List<DataTableQuery> Queries { get; } = [];

        public Task<PagedResult<Person>> GetDataAsync(DataTableQuery query, CancellationToken ct = default)
        {
            Queries.Add(query);
            var rows = query.PageSize == int.MaxValue ? allRows : allRows.Take(query.PageSize).ToList();
            return Task.FromResult(new PagedResult<Person>
            {
                Items = rows,
                TotalCount = allRows.Count,
                Page = query.Page,
                PageSize = query.PageSize
            });
        }
    }

    // ── CSV exporter ──────────────────────────────────────────────────────────

    [Fact]
    public void Csv_QuotesFieldsWithDelimitersAndNewlines()
    {
        var exporter = new CsvDataTableExporter(writeBom: false);
        var data = new DataTableExportData
        {
            Headers = ["Name", "Note"],
            Rows = [["Ann", "hi, there"], ["Bob", "line\nbreak"], ["Quote", "she said \"hi\""]]
        };

        var text = Encoding.UTF8.GetString(exporter.Export(data));

        text.Should().Contain("Name,Note");
        text.Should().Contain("\"hi, there\"");
        text.Should().Contain("\"line\nbreak\"");
        text.Should().Contain("\"she said \"\"hi\"\"\"");
    }

    [Fact]
    public void Csv_WritesBomWhenRequested()
    {
        var bytes = new CsvDataTableExporter(writeBom: true).Export(new DataTableExportData { Headers = ["A"], Rows = [] });
        bytes.Take(3).Should().Equal(0xEF, 0xBB, 0xBF);
    }

    // ── XLSX exporter ─────────────────────────────────────────────────────────

    [Fact]
    public void Xlsx_ProducesWellFormedWorkbook()
    {
        var exporter = new XlsxDataTableExporter();
        exporter.FileExtension.Should().Be("xlsx");

        var bytes = exporter.Export(new DataTableExportData
        {
            Name = "People",
            Headers = ["Name", "Age"],
            Rows = [["Ann", "30"], ["Bob", "25"]]
        });

        bytes.Length.Should().BeGreaterThan(0);

        using var ms = new MemoryStream(bytes);
        using var doc = SpreadsheetDocument.Open(ms, false);
        var worksheet = doc.WorkbookPart!.WorksheetParts.First().Worksheet;
        var rows = worksheet.Descendants<Row>().ToList();
        rows.Should().HaveCount(3); // header + 2 data rows
        var firstCell = rows[0].Descendants<Cell>().First();
        firstCell.InnerText.Should().Contain("Name");
    }

    // ── Component export snapshot ─────────────────────────────────────────────

    private IRenderedComponent<TmDataTable<Person>> RenderTable(List<Person> items, int pageSize = 10)
        => Render<TmDataTable<Person>>(p =>
        {
            p.Add(c => c.ViewContext, "export-test");
            p.Add(c => c.Items, items);
            p.Add(c => c.DefaultPageSize, pageSize);
            p.AddChildContent(b =>
            {
                var seq = 0;
                AddCol(b, ref seq, "Name", x => x.Name);
                AddCol(b, ref seq, "Age", x => x.Age);
            });
        });

    private static void AddCol(RenderTreeBuilder b, ref int seq, string title, Func<Person, object?> field)
    {
        b.OpenComponent<TmDataTableColumn<Person>>(seq++);
        b.AddAttribute(seq++, "Title", title);
        b.AddAttribute(seq++, "PropertyName", title);
        b.AddAttribute(seq++, "Sortable", true);
        b.AddAttribute(seq++, "Field", field);
        b.CloseComponent();
    }

    [Fact]
    public async Task BuildExportData_ReturnsAllRows_NotJustCurrentPage()
    {
        var items = Enumerable.Range(1, 50).Select(i => new Person($"P{i:D2}", i)).ToList();
        var cut = RenderTable(items, pageSize: 10);
        await cut.InvokeAsync(() => { });

        DataTableExportData? data = null;
        await cut.InvokeAsync(async () => data = await cut.Instance.BuildExportDataAsync());

        data.Should().NotBeNull();
        data!.Headers.Should().Equal("Name", "Age");
        data.Rows.Should().HaveCount(50); // all pages, not the 10-row page
    }

    [Fact]
    public async Task BuildExportData_RespectsCurrentSort()
    {
        var items = new List<Person> { new("C", 3), new("A", 1), new("B", 2) };
        var cut = RenderTable(items);
        await cut.InvokeAsync(() => { });

        cut.FindAll("th[data-sortable='true']")[0].Click(); // sort by Name asc

        DataTableExportData? data = null;
        await cut.InvokeAsync(async () => data = await cut.Instance.BuildExportDataAsync());

        data!.Rows.Select(r => r[0]).Should().Equal("A", "B", "C");
    }

    [Fact]
    public async Task BuildExportData_DataProviderRequestsCompleteCurrentResultSet()
    {
        var provider = new RecordingProvider(Enumerable.Range(1, 30).Select(i => new Person($"P{i}", i)).ToList());
        var cut = Render<TmDataTable<Person>>(p =>
        {
            p.Add(c => c.ViewContext, "provider-export");
            p.Add(c => c.DataProvider, provider);
            p.Add(c => c.DefaultPageSize, 5);
            p.Add(c => c.SearchText, "P");
            p.AddChildContent(b =>
            {
                var seq = 0;
                AddCol(b, ref seq, "Name", x => x.Name);
            });
        });

        var data = await cut.InvokeAsync(cut.Instance.BuildExportDataAsync);

        data.Rows.Should().HaveCount(30);
        provider.Queries.Last().Should().Match<DataTableQuery>(query =>
            query.Page == 1 && query.PageSize == int.MaxValue && query.SearchText == "P");
    }

    [Fact]
    public async Task ExportAsync_DoesNotThrow_WhenJsUnavailable()
    {
        var cut = RenderTable([new Person("Ann", 30)]);
        await cut.InvokeAsync(() => { });

        var act = async () => await cut.InvokeAsync(() => cut.Instance.ExportAsync(new CsvDataTableExporter()));
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task BuildExportData_UsesVisibleColumnOrder_AndCurrentSearchFilterSort()
    {
        var items = new List<Person>
        {
            new("Zoe, \"Lead\"", 42),
            new("Amy\nSmith", 42),
            new("Ignored", 18)
        };
        var cut = Render<TmDataTable<Person>>(p =>
        {
            p.Add(c => c.ViewContext, "export-test");
            p.Add(c => c.Items, items);
            p.Add(c => c.SearchText, "42");
            p.AddChildContent(b =>
            {
                b.OpenComponent<TmDataTableColumn<Person>>(0);
                b.AddAttribute(1, "Title", "Hidden");
                b.AddAttribute(2, "PropertyName", "Hidden");
                b.AddAttribute(3, "Field", (Func<Person, object?>)(x => x.Name));
                b.AddAttribute(4, "HiddenByDefault", true);
                b.AddAttribute(5, "Order", 0);
                b.CloseComponent();
                b.OpenComponent<TmDataTableColumn<Person>>(6);
                b.AddAttribute(7, "Title", "Age");
                b.AddAttribute(8, "PropertyName", "Age");
                b.AddAttribute(9, "Field", (Func<Person, object?>)(x => x.Age));
                b.AddAttribute(10, "Filterable", true);
                b.AddAttribute(11, "Order", 1);
                b.CloseComponent();
                b.OpenComponent<TmDataTableColumn<Person>>(12);
                b.AddAttribute(13, "Title", "Name");
                b.AddAttribute(14, "PropertyName", "Name");
                b.AddAttribute(15, "Field", (Func<Person, object?>)(x => x.Name));
                b.AddAttribute(16, "Sortable", true);
                b.AddAttribute(17, "Order", 2);
                b.CloseComponent();
            });
        });

        cut.Find(".tm-col-filter-input").Input("42");
        cut.FindAll("th[data-sortable='true']").Single().Click();
        var data = await cut.InvokeAsync(cut.Instance.BuildExportDataAsync);

        data.Headers.Should().Equal("Age", "Name");
        data.Rows.Should().HaveCount(2);
        data.Rows.Select(row => row[1]).Should().Equal("Amy\nSmith", "Zoe, \"Lead\"");
    }

    [Fact]
    public async Task BuiltInCsvExport_UsesDelimiterBom_StreamDownload_AndReportsRowCount()
    {
        DataTableExportResult? completed = null;
        var cut = Render<TmDataTable<Person>>(p =>
        {
            p.Add(c => c.ViewContext, "people");
            p.Add(c => c.Items, new List<Person> { new("Ann; \"A\"", 30), new("Bob\nB", 40) });
            p.Add(c => c.ShowSearch, false);
            p.Add(c => c.ShowColumnPicker, false);
            p.Add(c => c.ShowExport, true);
            p.Add(c => c.CsvDelimiter, ";");
            p.Add(c => c.OnExportCompleted,
                EventCallback.Factory.Create<DataTableExportResult>(this, result => completed = result));
            p.AddChildContent(b =>
            {
                var seq = 0;
                AddCol(b, ref seq, "Name", x => x.Name);
                AddCol(b, ref seq, "Age", x => x.Age);
            });
        });

        cut.Find(".tm-data-table__export .tm-dropdown-trigger").TextContent.Should().Contain("Export");
        cut.Find(".tm-data-table__export .tm-dropdown-trigger").Click();
        cut.FindAll("[role='menuitem']").Should().ContainSingle(item => item.TextContent.Contains("CSV"));
        cut.FindAll("[role='menuitem']").Should().NotContain(item => item.TextContent.Contains("XLSX"));
        await cut.Find("[data-export-format='csv']").ClickAsync(new());

        var invocation = JSInterop.Invocations.Should().ContainSingle(i =>
            i.Identifier == "TempoFileManager.downloadFileFromStream").Subject;
        invocation.Arguments[0].Should().Be("people.csv");
        invocation.Arguments[1].Should().BeOfType<DotNetStreamReference>();
        completed.Should().Be(new DataTableExportResult(DataTableExportFormat.Csv, "people.csv", 2, DataTableExportScope.All));

        var csv = new CsvDataTableExporter(";", writeBom: true).Export(await cut.InvokeAsync(() => cut.Instance.BuildExportDataAsync()));
        csv.Take(3).Should().Equal(0xEF, 0xBB, 0xBF);
        Encoding.UTF8.GetString(csv).Should().Contain("\"Ann; \"\"A\"\"\"");
        Encoding.UTF8.GetString(csv).Should().Contain("\"Bob\nB\"");
    }

    [Fact]
    public void ExportMenu_ShowsCsvAndXlsxLabelsAndDescriptions()
    {
        Services.AddTempoBlazorDataTableXlsx();
        var cut = Render<TmDataTable<Person>>(p =>
        {
            p.Add(c => c.ViewContext, "export-menu");
            p.Add(c => c.Items, new List<Person> { new("Ann", 30) });
            p.Add(c => c.ShowExport, true);
            p.Add(c => c.Appearance, DataTableAppearance.Card);
            p.AddChildContent(b =>
            {
                var seq = 0;
                AddCol(b, ref seq, "Name", x => x.Name);
            });
        });
        cut.Find(".tm-data-table__export .tm-dropdown-trigger").Click();

        var csv = cut.Find("[data-export-format='csv']");
        csv.QuerySelector(".tm-data-table__export-label")!.TextContent.Should().Contain("CSV");
        csv.QuerySelector(".tm-data-table__export-description")!.TextContent.Should().NotBeNullOrWhiteSpace();

        var xlsx = cut.Find("[data-export-format='xlsx']");
        xlsx.QuerySelector(".tm-data-table__export-label")!.TextContent.Should().Contain("Excel (XLSX)");
        xlsx.QuerySelector(".tm-data-table__export-description")!.TextContent.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task RegisteredXlsxExporter_AddsMenuItem_DownloadsAndReportsCompletion()
    {
        Services.AddTempoBlazorDataTableXlsx();
        DataTableExportResult? completed = null;
        var cut = Render<TmDataTable<Person>>(p =>
        {
            p.Add(c => c.ViewContext, "people");
            p.Add(c => c.Items, new List<Person> { new("Ann", 30) });
            p.Add(c => c.ShowSearch, false);
            p.Add(c => c.ShowColumnPicker, false);
            p.Add(c => c.ShowExport, true);
            p.Add(c => c.OnExportCompleted,
                EventCallback.Factory.Create<DataTableExportResult>(this, result => completed = result));
            p.AddChildContent(b =>
            {
                var seq = 0;
                AddCol(b, ref seq, "Name", x => x.Name);
                AddCol(b, ref seq, "Age", x => x.Age);
            });
        });

        cut.Find(".tm-data-table__export .tm-dropdown-trigger").Click();
        await cut.Find("[data-export-format='xlsx']").ClickAsync(new());

        var invocation = JSInterop.Invocations.Should().ContainSingle(i =>
            i.Identifier == "TempoFileManager.downloadFileFromStream").Subject;
        invocation.Arguments[0].Should().Be("people.xlsx");
        completed.Should().Be(new DataTableExportResult(DataTableExportFormat.Xlsx, "people.xlsx", 1, DataTableExportScope.All));
    }

    // ── Selected-only export (AllowExportSelected) ────────────────────────────

    private IRenderedComponent<TmDataTable<Person>> RenderSelectableExport(
        List<Person> items,
        bool allowExportSelected = true,
        bool selectable = true,
        Action<DataTableExportResult>? onCompleted = null,
        IDataTableXlsxExporter? xlsx = null)
    {
        if (xlsx is not null)
        {
            Services.AddSingleton(xlsx);
        }

        return Render<TmDataTable<Person>>(p =>
        {
            p.Add(c => c.ViewContext, "people");
            p.Add(c => c.Items, items);
            p.Add(c => c.ShowSearch, false);
            p.Add(c => c.ShowColumnPicker, false);
            p.Add(c => c.ShowPagination, false);
            p.Add(c => c.ShowExport, true);
            p.Add(c => c.Selectable, selectable);
            p.Add(c => c.AllowExportSelected, allowExportSelected);
            if (onCompleted is not null)
            {
                p.Add(c => c.OnExportCompleted,
                    EventCallback.Factory.Create<DataTableExportResult>(this, onCompleted));
            }

            p.AddChildContent(b =>
            {
                var seq = 0;
                AddCol(b, ref seq, "Name", x => x.Name);
                AddCol(b, ref seq, "Age", x => x.Age);
            });
        });
    }

    [Fact]
    public void ExportScope_EmptySelection_DisablesSelected_AndShowsDescription()
    {
        var people = Enumerable.Range(1, 5).Select(i => new Person($"P{i}", i)).ToList();
        var cut = RenderSelectableExport(people);

        cut.Find(".tm-data-table__export .tm-dropdown-trigger").Click();

        cut.FindAll("[data-export-scope]").Should().HaveCount(2);
        var selected = cut.Find("[data-export-scope='selected']");
        selected.HasAttribute("disabled").Should().BeFalse("an empty selection stays focusable");
        selected.GetAttribute("aria-disabled").Should().Be("true");
        selected.ClassList.Should().Contain("tm-dropdown-item-disabled");
        selected.GetAttribute("role").Should().Be("menuitemradio");
        selected.GetAttribute("aria-checked").Should().Be("false");
        var describedBy = selected.GetAttribute("aria-describedby");
        describedBy.Should().StartWith("tm-data-table-export-scope-empty-");
        selected.TextContent.Should().Contain("Selected rows (0)");
        var description = selected.QuerySelector(".tm-data-table__export-description")!;
        description.GetAttribute("id").Should().Be(describedBy);
        description.TextContent.Should().Contain("Select rows to export them");
        var all = cut.Find("[data-export-scope='all']");
        all.HasAttribute("disabled").Should().BeFalse();
        all.GetAttribute("aria-checked").Should().Be("true");
        all.TextContent.Should().Contain("All rows (5)");
    }

    [Fact]
    public async Task ExportScope_TwoOfFive_CsvContainsHeaderAndThoseTwoRowsInOrder()
    {
        var people = Enumerable.Range(1, 5).Select(i => new Person($"P{i}", i)).ToList();
        var cut = RenderSelectableExport(people);

        cut.FindAll("tbody tr td input[type='checkbox']")[1].Change(true);
        cut.FindAll("tbody tr td input[type='checkbox']")[3].Change(true);

        cut.Find(".tm-data-table__export .tm-dropdown-trigger").Click();
        var selected = cut.Find("[data-export-scope='selected']");
        selected.HasAttribute("disabled").Should().BeFalse();
        selected.TextContent.Should().Contain("Selected rows (2)");

        await cut.Find("[data-export-scope='selected']").ClickAsync(new());
        cut.FindAll(".tm-dropdown-menu").Should().NotBeEmpty("a scope choice leaves the menu open");
        await cut.Find("[data-export-format='csv']").ClickAsync(new());

        var data = await cut.InvokeAsync(() => cut.Instance.BuildExportDataAsync(DataTableExportScope.Selected));
        var csv = Encoding.UTF8.GetString(new CsvDataTableExporter(writeBom: false).Export(data));
        var lines = csv.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd('\n').Split('\n');
        lines.Should().Equal("Name,Age", "P2,2", "P4,4");
    }

    [Fact]
    public async Task ExportScope_TwoOfFive_XlsxExporterReceivesTwoRows()
    {
        var people = Enumerable.Range(1, 5).Select(i => new Person($"P{i}", i)).ToList();
        var xlsx = new RecordingXlsxExporter();
        var cut = RenderSelectableExport(people, xlsx: xlsx);

        cut.FindAll("tbody tr td input[type='checkbox']")[0].Change(true);
        cut.FindAll("tbody tr td input[type='checkbox']")[4].Change(true);

        await cut.InvokeAsync(() => cut.Instance.ExportAsync(DataTableExportFormat.Xlsx, DataTableExportScope.Selected));

        xlsx.Received.Should().ContainSingle();
        xlsx.Received[0].Rows.Should().HaveCount(2);
        xlsx.Received[0].Rows.Select(row => row[0]).Should().Equal("P1", "P5");
    }

    private sealed class RecordingJsonExporter : IDataTableExporter
    {
        public List<DataTableExportData> Received { get; } = [];

        public string Format => "json";
        public string ContentType => "application/json";
        public string FileExtension => "json";

        public byte[] Export(DataTableExportData data)
        {
            Received.Add(data);
            return Encoding.UTF8.GetBytes("{}");
        }
    }

    [Fact]
    public async Task ExportScope_CustomExporterWithSelectedScope_ReceivesTwoOfFiveRows()
    {
        var people = Enumerable.Range(1, 5).Select(i => new Person($"P{i}", i)).ToList();
        var exporter = new RecordingJsonExporter();
        var cut = RenderSelectableExport(people);

        cut.FindAll("tbody tr td input[type='checkbox']")[1].Change(true);
        cut.FindAll("tbody tr td input[type='checkbox']")[3].Change(true);

        await cut.InvokeAsync(() => cut.Instance.ExportAsync(exporter, DataTableExportScope.Selected));

        exporter.Received.Should().ContainSingle();
        exporter.Received[0].Rows.Should().HaveCount(2, "the custom exporter sees only the checked rows");
        exporter.Received[0].Rows.Select(row => row[0]).Should().Equal("P2", "P4");
    }

    [Fact]
    public async Task ExportScope_CustomExporterWithoutScope_DelegatesToAllRows()
    {
        var people = Enumerable.Range(1, 5).Select(i => new Person($"P{i}", i)).ToList();
        var exporter = new RecordingJsonExporter();
        var cut = RenderSelectableExport(people);

        cut.FindAll("tbody tr td input[type='checkbox']")[1].Change(true);

        await cut.InvokeAsync(() => cut.Instance.ExportAsync(exporter, "people.json"));

        exporter.Received.Should().ContainSingle();
        exporter.Received[0].Rows.Should().HaveCount(5, "the exporter-less scope defaults to All");
    }

    [Fact]
    public void ExportScope_AllowExportSelectedFalse_HasNoScopeSection()
    {
        var people = Enumerable.Range(1, 5).Select(i => new Person($"P{i}", i)).ToList();
        var cut = RenderSelectableExport(people, allowExportSelected: false);

        cut.Find(".tm-data-table__export .tm-dropdown-trigger").Click();

        cut.FindAll("[data-export-scope]").Should().BeEmpty();
        cut.FindAll("[role='separator']").Should().BeEmpty();
        cut.Find("[data-export-format='csv']").TextContent.Should().Contain("CSV file");
    }

    [Fact]
    public void ExportScope_SelectableFalse_HasNoScopeSection()
    {
        var people = Enumerable.Range(1, 5).Select(i => new Person($"P{i}", i)).ToList();
        var cut = RenderSelectableExport(people, selectable: false);

        cut.Find(".tm-data-table__export .tm-dropdown-trigger").Click();

        cut.FindAll("[data-export-scope]").Should().BeEmpty();
        cut.FindAll("tbody tr td input[type='checkbox']").Should().BeEmpty();
    }

    [Fact]
    public async Task ExportScope_OnExportCompleted_ReportsSelected()
    {
        DataTableExportResult? completed = null;
        var people = Enumerable.Range(1, 5).Select(i => new Person($"P{i}", i)).ToList();
        var cut = RenderSelectableExport(people, onCompleted: result => completed = result);

        cut.FindAll("tbody tr td input[type='checkbox']")[2].Change(true);
        await cut.InvokeAsync(() => cut.Instance.ExportAsync(DataTableExportFormat.Csv, DataTableExportScope.Selected));

        completed.Should().NotBeNull();
        completed!.Scope.Should().Be(DataTableExportScope.Selected);
        completed.RowCount.Should().Be(1);
        completed.Format.Should().Be(DataTableExportFormat.Csv);
    }

    [Fact]
    public async Task ExportScope_ClearingTheSelection_ReturnsToAll()
    {
        var people = Enumerable.Range(1, 3).Select(i => new Person($"P{i}", i)).ToList();
        var cut = RenderSelectableExport(people);

        cut.FindAll("tbody tr td input[type='checkbox']")[0].Change(true);
        cut.Find(".tm-data-table__export .tm-dropdown-trigger").Click();
        await cut.Find("[data-export-scope='selected']").ClickAsync(new());

        cut.FindAll("tbody tr td input[type='checkbox']")[0].Change(false);
        cut.FindAll(".tm-dropdown-menu").Should().NotBeEmpty("a scope choice leaves the menu open");

        cut.Find("[data-export-scope='all']").GetAttribute("aria-checked").Should().Be("true");
        cut.Find("[data-export-scope='selected']").GetAttribute("aria-checked").Should().Be("false");
    }

    [Fact]
    public async Task ExportAsync_FormatOnly_StillExportsAllRows()
    {
        var people = Enumerable.Range(1, 5).Select(i => new Person($"P{i}", i)).ToList();
        DataTableExportResult? completed = null;
        var cut = RenderSelectableExport(people, onCompleted: result => completed = result);
        cut.FindAll("tbody tr td input[type='checkbox']")[0].Change(true);

        await cut.InvokeAsync(() => cut.Instance.ExportAsync(DataTableExportFormat.Csv));

        completed!.Scope.Should().Be(DataTableExportScope.All);
        completed.RowCount.Should().Be(5);
    }

    [Fact]
    public void ExportScope_PagedClientData_AllRowsCountIsFilteredTotal_NotThePage()
    {
        var people = Enumerable.Range(1, 10).Select(i => new Person($"P{i}", i)).ToList();
        var cut = Render<TmDataTable<Person>>(p =>
        {
            p.Add(c => c.Items, people);
            p.Add(c => c.ShowSearch, false);
            p.Add(c => c.ShowColumnPicker, false);
            p.Add(c => c.ShowPagination, true);
            p.Add(c => c.DefaultPageSize, 5);
            p.Add(c => c.ShowExport, true);
            p.Add(c => c.Selectable, true);
            p.Add(c => c.AllowExportSelected, true);
            p.AddChildContent(b =>
            {
                var seq = 0;
                AddCol(b, ref seq, "Name", x => x.Name);
            });
        });

        cut.Find(".tm-data-table__export .tm-dropdown-trigger").Click();

        cut.Find("[data-export-scope='all']").TextContent.Should().Contain("All rows (10)");
        cut.FindAll("tbody tr").Should().HaveCount(5, "the table itself still pages");
    }

    [Fact]
    public async Task ExportScope_CheckedRowHiddenBySearch_IsStillExportedAfterTheVisibleOnes()
    {
        var people = Enumerable.Range(1, 4).Select(i => new Person($"P{i}", i)).ToList();
        var search = string.Empty;
        var cut = Render<TmDataTable<Person>>(p =>
        {
            p.Add(c => c.Items, people);
            p.Add(c => c.SearchText, search);
            p.Add(c => c.ShowSearch, false);
            p.Add(c => c.ShowColumnPicker, false);
            p.Add(c => c.ShowPagination, false);
            p.Add(c => c.ShowExport, true);
            p.Add(c => c.Selectable, true);
            p.Add(c => c.AllowExportSelected, true);
            p.AddChildContent(b =>
            {
                var seq = 0;
                AddCol(b, ref seq, "Name", x => x.Name);
            });
        });

        cut.FindAll("tbody tr td input[type='checkbox']")[0].Change(true);
        cut.FindAll("tbody tr td input[type='checkbox']")[2].Change(true);

        search = "P2";
        cut.Render(p => p.Add(c => c.SearchText, search));
        cut.WaitForAssertion(() => cut.FindAll("tbody tr").Should().HaveCount(1));

        var data = await cut.InvokeAsync(() => cut.Instance.BuildExportDataAsync(DataTableExportScope.Selected));

        data.Rows.Select(row => row[0]).Should().Equal("P1", "P3");
    }

    [Fact]
    public async Task ExportScope_ProviderReturnsNewInstances_StillExportsTheCheckedRows()
    {
        var people = Enumerable.Range(1, 4).Select(i => new Person($"P{i}", i)).ToList();
        var provider = new FreshInstanceProvider(people);
        var cut = Render<TmDataTable<Person>>(p =>
        {
            p.Add(c => c.DataProvider, provider);
            p.Add(c => c.ShowSearch, false);
            p.Add(c => c.ShowColumnPicker, false);
            p.Add(c => c.ShowPagination, false);
            p.Add(c => c.ShowExport, true);
            p.Add(c => c.Selectable, true);
            p.Add(c => c.AllowExportSelected, true);
            p.AddChildContent(b =>
            {
                var seq = 0;
                AddCol(b, ref seq, "Name", x => x.Name);
                AddCol(b, ref seq, "Age", x => x.Age);
            });
        });

        cut.WaitForState(() => cut.FindAll("tbody tr").Count == 4);
        cut.FindAll("tbody tr td input[type='checkbox']")[1].Change(true);
        cut.FindAll("tbody tr td input[type='checkbox']")[3].Change(true);

        var data = await cut.InvokeAsync(() => cut.Instance.BuildExportDataAsync(DataTableExportScope.Selected));

        provider.Queries.Should().NotContain(query => query.PageSize == int.MaxValue,
            "a selected export must not re-query the provider");
        data.Rows.Select(row => row[0]).Should().Equal("P2", "P4");
    }

    /// <summary>Every query returns new record instances, the way a real provider materializes rows.</summary>
    private sealed class FreshInstanceProvider(IReadOnlyList<Person> allRows) : IDataTableDataProvider<Person>
    {
        public List<DataTableQuery> Queries { get; } = [];

        public Task<PagedResult<Person>> GetDataAsync(DataTableQuery query, CancellationToken ct = default)
        {
            Queries.Add(query);
            var rows = (query.PageSize == int.MaxValue ? allRows : allRows.Take(query.PageSize))
                .Select(row => row with { })
                .ToList();
            return Task.FromResult(new PagedResult<Person>
            {
                Items = rows,
                TotalCount = allRows.Count,
                Page = query.Page,
                PageSize = query.PageSize
            });
        }
    }

    private sealed class RecordingXlsxExporter : IDataTableXlsxExporter
    {
        public List<DataTableExportData> Received { get; } = [];

        public byte[] Export(DataTableExportData data)
        {
            Received.Add(data);
            return [1, 2, 3];
        }
    }
}
