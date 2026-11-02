using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using NSubstitute;
using Tempo.Blazor.Components.Dropdowns;
using Tempo.Blazor.Components.NotionEditor.Blocks.Database;
using Tempo.Blazor.Components.NotionEditor.Services;
using Tempo.Blazor.NotionEditor.Enums;
using Tempo.Blazor.NotionEditor.Interfaces;
using Tempo.Blazor.NotionEditor.Models;
using Tempo.Blazor.Tests.Localization;
using Xunit;

namespace Tempo.Blazor.Tests.Components.NotionEditor;

/// <summary>
/// Characterization net for the six database views. A later markup refactor must still render each
/// view's root and the one seeded record, and must still raise the record callback.
/// </summary>
public class TmNotionDbViewCharacterizationTests : LocalizationTestBase
{
    [Fact]
    public void TableView_RendersTheSeededRecord()
    {
        var (fields, records) = Seed();
        var cut = Render<TmNotionDbTableView>(p => p
            .Add(x => x.Fields, fields)
            .Add(x => x.Records, records));

        cut.Find(".tm-dbt").Should().NotBeNull();
        cut.Markup.Should().Contain("Alpha");
    }

    [Fact]
    public void ListView_RendersTheSeededRecord()
    {
        var (fields, records) = Seed();
        var cut = Render<TmNotionDbListView>(p => p
            .Add(x => x.Fields, fields)
            .Add(x => x.Records, records));

        cut.Find(".tm-dblv").Should().NotBeNull();
        cut.Markup.Should().Contain("Alpha");
    }

    [Fact]
    public void GalleryView_RendersTheSeededRecord()
    {
        var (fields, records) = Seed();
        var cut = Render<TmNotionDbGalleryView>(p => p
            .Add(x => x.Fields, fields)
            .Add(x => x.Records, records));

        cut.Find(".tm-dbg").Should().NotBeNull();
        cut.Markup.Should().Contain("Alpha");
    }

    [Fact]
    public void BoardView_RendersTheSeededRecord()
    {
        var (fields, records) = Seed();
        var cut = Render<TmNotionDbBoardView>(p => p
            .Add(x => x.Fields, fields)
            .Add(x => x.Records, records)
            .Add(x => x.GroupByField, fields[0]));

        cut.Find(".tm-dbb").Should().NotBeNull();
        cut.Markup.Should().Contain("Alpha");
    }

    [Fact]
    public void CalendarView_RendersTheMonthGrid()
    {
        var (fields, records) = Seed();
        var cut = Render<TmNotionDbCalendarView>(p => p
            .Add(x => x.Fields, fields)
            .Add(x => x.Records, records)
            .Add(x => x.DateFieldId, fields[0].Id));

        cut.Find(".tm-dbcal").Should().NotBeNull();
        cut.FindAll(".tm-dbcal__cell, .tm-dbcal__grid").Should().NotBeEmpty();
    }

    [Fact]
    public void TimelineView_RendersTheGantt()
    {
        var (fields, records) = Seed();
        var cut = Render<TmNotionDbTimelineView>(p => p
            .Add(x => x.Fields, fields)
            .Add(x => x.Records, records)
            .Add(x => x.StartDateFieldId, fields[0].Id));

        cut.Find(".tm-dbt").Should().NotBeNull();
        cut.Markup.Should().Contain("Alpha");
    }

    [Fact]
    public void TableView_OnRecordClicked_FiresFromTheSpacer()
    {
        var (fields, records) = Seed();
        var clicked = new List<IDatabaseRecord>();
        var cut = Render<TmNotionDbTableView>(p => p
            .Add(x => x.Fields, fields)
            .Add(x => x.Records, records)
            .Add(x => x.OnRecordClicked, (IDatabaseRecord r) => clicked.Add(r)));

        cut.Find(".tm-dbt__cell--spacer").Click();
        clicked.Should().ContainSingle().Which.Id.Should().Be(records[0].Id);
    }

    [Fact]
    public void TableView_OnRecordUpdated_FiresFromAnInlineEdit()
    {
        var (fields, records) = Seed();
        var updated = new List<IDatabaseRecord>();
        var cut = Render<TmNotionDbTableView>(p => p
            .Add(x => x.Fields, fields)
            .Add(x => x.Records, records)
            .Add(x => x.OnRecordUpdated, (IDatabaseRecord r) => updated.Add(r)));

        cut.Find(".tm-dbc").Click();
        cut.Find(".tm-dbc__input").Input("Beta");
        cut.Find(".tm-dbc__input").Blur();

        updated.Should().ContainSingle();
        updated[0].Fields[fields[0].Id.ToString()].Should().Be("Beta");
    }

    [Fact]
    public async Task TableView_OnFieldResized_FiresFromOnColumnResized()
    {
        var (fields, records) = Seed();
        (Guid FieldId, int Width)? resized = null;
        var cut = Render<TmNotionDbTableView>(p => p
            .Add(x => x.Fields, fields)
            .Add(x => x.Records, records)
            .Add(x => x.OnFieldResized, EventCallback.Factory.Create<(Guid FieldId, int Width)>(this, v => resized = v)));

        await cut.InvokeAsync(() => cut.Instance.OnColumnResized(fields[0].Id.ToString(), 180));

        resized.Should().NotBeNull();
        resized!.Value.FieldId.Should().Be(fields[0].Id);
        resized.Value.Width.Should().Be(180);
        cut.Find("[data-testid=notion-db-table-footer]").Should().NotBeNull();
    }

    [Fact]
    public void ListView_OnRecordClicked_FiresFromTheRow()
    {
        var (fields, records) = Seed();
        var clicked = new List<IDatabaseRecord>();
        var cut = Render<TmNotionDbListView>(p => p
            .Add(x => x.Fields, fields)
            .Add(x => x.Records, records)
            .Add(x => x.OnRecordClicked, (IDatabaseRecord r) => clicked.Add(r)));

        cut.Find("[data-testid=notion-db-list-row]").Click();
        clicked.Should().ContainSingle().Which.Id.Should().Be(records[0].Id);
    }

    [Fact]
    public void GalleryView_OnRecordClicked_FiresFromTheCard()
    {
        var (fields, records) = Seed();
        var clicked = new List<IDatabaseRecord>();
        var cut = Render<TmNotionDbGalleryView>(p => p
            .Add(x => x.Fields, fields)
            .Add(x => x.Records, records)
            .Add(x => x.OnRecordClicked, (IDatabaseRecord r) => clicked.Add(r)));

        cut.Find("[data-testid=notion-db-gallery-card]").Click();
        clicked.Should().ContainSingle().Which.Id.Should().Be(records[0].Id);
    }

    [Fact]
    public void BoardView_OnRecordClicked_FiresFromTheCard()
    {
        var (fields, records) = Seed();
        var clicked = new List<IDatabaseRecord>();
        var cut = Render<TmNotionDbBoardView>(p => p
            .Add(x => x.Fields, fields)
            .Add(x => x.Records, records)
            .Add(x => x.GroupByField, fields[0])
            .Add(x => x.OnRecordClicked, (IDatabaseRecord r) => clicked.Add(r)));

        cut.Find("[data-testid=notion-db-board-card]").Click();
        clicked.Should().ContainSingle().Which.Id.Should().Be(records[0].Id);
        cut.FindAll("[data-testid=notion-db-board-group]").Should().NotBeEmpty();
    }

    [Fact]
    public async Task BoardView_OnRecordUpdated_FiresFromJsDrop()
    {
        var status = new DatabaseField
        {
            Id = Guid.NewGuid(),
            Name = "Status",
            Type = DatabaseFieldType.Select
        };
        var record = new DatabaseRecord
        {
            Id = Guid.NewGuid(),
            Fields = new Dictionary<string, object?> { [status.Id.ToString()] = "Todo" }
        };
        var updated = new List<IDatabaseRecord>();
        var cut = Render<TmNotionDbBoardView>(p => p
            .Add(x => x.Fields, new[] { status })
            .Add(x => x.Records, new[] { record })
            .Add(x => x.GroupByField, status)
            .Add(x => x.OnRecordUpdated, (IDatabaseRecord r) => updated.Add(r)));

        cut.Find("[data-testid=notion-db-board-card]").GetAttribute("data-record-id").Should().Be(record.Id.ToString());
        await cut.InvokeAsync(() => cut.Instance.JsDrop("Done", record.Id.ToString(), "Todo"));

        updated.Should().ContainSingle();
        updated[0].Fields[status.Id.ToString()].Should().Be("Done");
    }

    [Fact]
    public void CalendarView_OnRecordClicked_FiresFromTheEvent()
    {
        var dateField = new DatabaseField { Id = Guid.NewGuid(), Name = "When", Type = DatabaseFieldType.Date, IsPrimary = true };
        var record = new DatabaseRecord
        {
            Id = Guid.NewGuid(),
            Fields = new Dictionary<string, object?> { [dateField.Id.ToString()] = DateTime.Today }
        };
        var clicked = new List<IDatabaseRecord>();
        var cut = Render<TmNotionDbCalendarView>(p => p
            .Add(x => x.Fields, new[] { dateField })
            .Add(x => x.Records, new[] { record })
            .Add(x => x.DateFieldId, dateField.Id)
            .Add(x => x.OnRecordClicked, (IDatabaseRecord r) => clicked.Add(r)));

        cut.Find("[data-testid=notion-db-calendar-event]").Click();
        clicked.Should().ContainSingle().Which.Id.Should().Be(record.Id);
        cut.FindAll("[data-testid=notion-db-calendar-cell]").Should().NotBeEmpty();
    }

    [Fact]
    public void TimelineView_OnRecordClicked_FiresFromTheBar()
    {
        var (fields, records) = Seed();
        var clicked = new List<IDatabaseRecord>();
        var cut = Render<TmNotionDbTimelineView>(p => p
            .Add(x => x.Fields, fields)
            .Add(x => x.Records, records)
            .Add(x => x.StartDateFieldId, fields[0].Id)
            .Add(x => x.OnRecordClicked, (IDatabaseRecord r) => clicked.Add(r)));

        cut.Find("[data-testid=notion-db-timeline-bar]").Click();
        clicked.Should().ContainSingle().Which.Id.Should().Be(records[0].Id);
    }

    [Fact]
    public void TableView_RowDelete_ConfirmsBeforeRaising()
    {
        var (fields, records) = Seed();
        var deleted = new List<IDatabaseRecord>();
        var cut = Render<TmNotionDbTableView>(p => p
            .Add(x => x.Fields, fields)
            .Add(x => x.Records, records)
            .Add(x => x.OnRecordDeleted, (IDatabaseRecord r) => deleted.Add(r)));

        cut.Find("[data-testid=notion-db-table-row-delete] .tm-dropdown-trigger").Click();
        cut.InvokeAsync(() => cut.FindComponent<TmDropdown>().Instance.SelectItemAsync("delete")).Wait();

        cut.Find(".tm-modal--visible").Should().NotBeNull();
        deleted.Should().BeEmpty();

        cut.Find(".tm-dialog-btn-cancel").Click();
        deleted.Should().BeEmpty();

        cut.Find("[data-testid=notion-db-table-row-delete] .tm-dropdown-trigger").Click();
        cut.InvokeAsync(() => cut.FindComponent<TmDropdown>().Instance.SelectItemAsync("delete")).Wait();
        cut.Find(".tm-dialog-btn-ok").Click();
        deleted.Should().ContainSingle().Which.Id.Should().Be(records[0].Id);
    }

    [Fact]
    public void DatabaseBlock_ViewTab_SwitchesTheRenderedView()
    {
        var dbId = Guid.NewGuid();
        var table = new DatabaseView { Id = Guid.NewGuid(), Name = "Table", Type = DatabaseViewType.Table };
        var list = new DatabaseView { Id = Guid.NewGuid(), Name = "List", Type = DatabaseViewType.List };
        var provider = Substitute.For<INotionDatabaseProvider>();
        provider.GetFieldsAsync(Arg.Any<string>()).Returns(Array.Empty<IDatabaseField>());
        provider.GetViewsAsync(Arg.Any<string>()).Returns(new IDatabaseView[] { table, list });
        provider.GetRecordsAsync(Arg.Any<string>(), Arg.Any<INotionDatabaseFilter?>(),
                Arg.Any<IEnumerable<NotionDatabaseSort>?>(), Arg.Any<NotionDatabaseGrouping?>(),
                Arg.Any<int>(), Arg.Any<int>())
            .Returns(new PagedResult<IDatabaseRecord> { Items = [], TotalCount = 0, Page = 1, PageSize = 50 });

        var cut = Render<TmNotionDatabaseBlock>(p => p
            .Add(x => x.Block, new PageBlock
            {
                Id = Guid.NewGuid(),
                Type = BlockType.InlineDatabase,
                Content = new InlineDatabaseBlockContent { DatabaseId = dbId }
            })
            .AddCascadingValue(new NotionEditorContext
            {
                DataProvider = Substitute.For<INotionDataProvider>(),
                BlockService = Substitute.For<INotionEditorBlockService>(),
                DatabaseProvider = provider
            }));

        cut.WaitForAssertion(() => cut.FindAll("[data-testid=notion-db-view-tab]").Should().HaveCount(2));
        cut.Find(".tm-dbt").Should().NotBeNull();
        cut.FindAll("[data-testid=notion-db-view-tab]")[1].Click();
        cut.WaitForAssertion(() => cut.Find(".tm-dblv").Should().NotBeNull());
    }

    private static (DatabaseField[] Fields, DatabaseRecord[] Records) Seed()
    {
        var field = new DatabaseField
        {
            Id = Guid.NewGuid(),
            Name = "Title",
            Type = DatabaseFieldType.Text,
            IsPrimary = true
        };
        var record = new DatabaseRecord
        {
            Id = Guid.NewGuid(),
            Fields = new Dictionary<string, object?> { [field.Id.ToString()] = "Alpha" }
        };
        return ([field], [record]);
    }
}
