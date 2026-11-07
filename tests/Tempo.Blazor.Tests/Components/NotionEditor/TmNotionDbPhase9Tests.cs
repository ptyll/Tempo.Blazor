using System.Globalization;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.Components.Feedback;
using Tempo.Blazor.Components.NotionEditor.Blocks.Database;
using Tempo.Blazor.NotionEditor.Enums;
using Tempo.Blazor.NotionEditor.Interfaces;
using Tempo.Blazor.NotionEditor.Models;
using Tempo.Blazor.Tests.Localization;
using Xunit;

namespace Tempo.Blazor.Tests.Components.NotionEditor;

/// <summary>
/// Phase 9 (DB-7 + DB-8): board Move-to, calendar day sheet and culture week start,
/// timeline header, keyboard move/resize, hidden sidebar and date sheet.
/// </summary>
public class TmNotionDbPhase9Tests : LocalizationTestBase
{
    public TmNotionDbPhase9Tests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void BoardView_MoveToMenu_InvokesRecordUpdatedWithNewGroup()
    {
        var status = StatusField();
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

        var dropdown = cut.FindComponent<Tempo.Blazor.Components.Dropdowns.TmDropdown>();
        dropdown.InvokeAsync(() => dropdown.Instance.OpenAsync()).GetAwaiter().GetResult();
        var target = cut.Find("[data-testid=notion-db-board-move-target][data-group-value=Done]");
        target.GetAttribute("aria-disabled").Should().NotBe("true");
        dropdown.InvokeAsync(() => dropdown.Instance.SelectItemAsync("Done")).GetAwaiter().GetResult();

        updated.Should().ContainSingle();
        updated[0].Fields[status.Id.ToString()].Should().Be("Done");
    }

    [Fact]
    public void BoardView_Mobile_LaneHasSnapClass()
    {
        var status = StatusField();
        var cut = Render<TmNotionDbBoardView>(p => p
            .Add(x => x.Fields, new[] { status })
            .Add(x => x.Records, Array.Empty<IDatabaseRecord>())
            .Add(x => x.GroupByField, status)
            .AddCascadingValue(new TmLayoutContext(TmLayoutMode.Mobile, TmLayoutMode.Mobile)));

        cut.Find(".tm-dbb").GetAttribute("data-layout").Should().Be("mobile");
        cut.Find(".tm-dbb__scroll").ClassList.Should().Contain("tm-dbb__scroll--snap");
        cut.Find("[data-testid=notion-db-board-add]").Should().NotBeNull();
    }

    [Fact]
    public void CalendarView_DayTap_OpensDaySheet()
    {
        var (title, when, records) = CalendarSeed(6);
        var cut = Render<TmNotionDbCalendarView>(p => p
            .Add(x => x.Fields, new IDatabaseField[] { title, when })
            .Add(x => x.Records, records)
            .Add(x => x.DateFieldId, when.Id)
            .AddCascadingValue(new TmLayoutContext(TmLayoutMode.Mobile, TmLayoutMode.Mobile)));

        cut.Find("[data-testid=notion-db-calendar-overflow]").Click();

        var sheet = cut.Find("[data-testid=notion-db-calendar-day-sheet]");
        sheet.TextContent.Should().Contain("Record 6");
        cut.FindComponent<TmModal>().Instance.Show.Should().BeTrue();
    }

    [Fact]
    public void CalendarView_FirstDayOfWeek_FollowsCulture()
    {
        var previous = CultureInfo.CurrentCulture;
        var previousUi = CultureInfo.CurrentUICulture;
        try
        {
            var us = CultureInfo.GetCultureInfo("en-US");
            CultureInfo.CurrentCulture = us;
            CultureInfo.CurrentUICulture = us;
            var (title, when, records) = CalendarSeed(1);
            var american = Render<TmNotionDbCalendarView>(p => p
                .Add(x => x.Fields, new IDatabaseField[] { title, when })
                .Add(x => x.Records, records)
                .Add(x => x.DateFieldId, when.Id));
            american.Find(".tm-dbcal__weekday").TextContent.Trim()
                .Should().Be(us.DateTimeFormat.AbbreviatedDayNames[(int)DayOfWeek.Sunday]);

            var cs = CultureInfo.GetCultureInfo("cs-CZ");
            CultureInfo.CurrentCulture = cs;
            CultureInfo.CurrentUICulture = cs;
            var czech = Render<TmNotionDbCalendarView>(p => p
                .Add(x => x.Fields, new IDatabaseField[] { title, when })
                .Add(x => x.Records, records)
                .Add(x => x.DateFieldId, when.Id));
            czech.Find(".tm-dbcal__weekday").TextContent.Trim()
                .Should().Be(cs.DateTimeFormat.AbbreviatedDayNames[(int)DayOfWeek.Monday]);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
            CultureInfo.CurrentUICulture = previousUi;
        }
    }

    [Fact]
    public void TimelineView_Header_RendersMonthAndWeekRows()
    {
        var (fields, records) = TimelineSeed();
        var cut = Render<TmNotionDbTimelineView>(p => p
            .Add(x => x.Fields, fields)
            .Add(x => x.Records, records)
            .Add(x => x.StartDateFieldId, fields[1].Id)
            .Add(x => x.EndDateFieldId, fields[2].Id));

        cut.Find(".tm-dbtl").Should().NotBeNull();
        cut.Find(".tm-dbtl__head-months").Should().NotBeNull();
        cut.Find(".tm-dbtl__head-weeks").Should().NotBeNull();
        cut.Find("[data-testid=notion-db-timeline-today]").Should().NotBeNull();
        cut.Find("[data-testid=notion-db-timeline-footer]").TextContent.Should().Contain("1");
    }

    [Fact]
    public async Task TimelineView_KeyboardShift_MovesBarByOneUnit()
    {
        var (fields, records) = TimelineSeed();
        var updated = new List<IDatabaseRecord>();
        var cut = Render<TmNotionDbTimelineView>(p => p
            .Add(x => x.Fields, fields)
            .Add(x => x.Records, records)
            .Add(x => x.StartDateFieldId, fields[1].Id)
            .Add(x => x.EndDateFieldId, fields[2].Id)
            .Add(x => x.OnRecordUpdated, (IDatabaseRecord r) => updated.Add(r)));

        var start = (DateTime)records[0].Fields[fields[1].Id.ToString()]!;
        await cut.InvokeAsync(() => cut.Instance.ApplyKeyboardStepAsync(records[0].Id, days: 1, resize: false));

        updated.Should().ContainSingle();
        ((DateTime)updated[0].Fields[fields[1].Id.ToString()]!).Date.Should().Be(start.Date.AddDays(1));
        ((DateTime)updated[0].Fields[fields[2].Id.ToString()]!).Date.Should().Be(start.Date.AddDays(8));
    }

    [Fact]
    public async Task TimelineView_AltArrowRight_DoesNotRaiseOnRecordUpdated()
    {
        var (fields, records) = TimelineSeed();
        var updated = 0;
        var cut = Render<TmNotionDbTimelineView>(p => p
            .Add(x => x.Fields, fields)
            .Add(x => x.Records, records)
            .Add(x => x.StartDateFieldId, fields[1].Id)
            .Add(x => x.EndDateFieldId, fields[2].Id)
            .Add(x => x.OnRecordUpdated, () => updated++));

        var bar = cut.Find("[data-timeline-bar]");
        bar.GetAttribute("aria-keyshortcuts").Should().Be(
            "Alt+ArrowLeft Alt+ArrowRight Alt+Shift+ArrowLeft Alt+Shift+ArrowRight");
        await cut.InvokeAsync(() => bar.KeyDown(new KeyboardEventArgs { Key = "ArrowRight", AltKey = true }));

        updated.Should().Be(0, "Alt+Arrow is owned by notion-db-timeline.js");
    }

    [Fact]
    public async Task TimelineView_KeyboardAlt_ResizesBar()
    {
        var (fields, records) = TimelineSeed();
        var updated = new List<IDatabaseRecord>();
        var cut = Render<TmNotionDbTimelineView>(p => p
            .Add(x => x.Fields, fields)
            .Add(x => x.Records, records)
            .Add(x => x.StartDateFieldId, fields[1].Id)
            .Add(x => x.EndDateFieldId, fields[2].Id)
            .Add(x => x.OnRecordUpdated, (IDatabaseRecord r) => updated.Add(r)));

        var end = (DateTime)records[0].Fields[fields[2].Id.ToString()]!;
        await cut.InvokeAsync(() => cut.Instance.ApplyKeyboardStepAsync(records[0].Id, days: 1, resize: true));

        updated.Should().ContainSingle();
        ((DateTime)updated[0].Fields[fields[2].Id.ToString()]!).Date.Should().Be(end.Date.AddDays(1));
        ((DateTime)updated[0].Fields[fields[1].Id.ToString()]!).Date
            .Should().Be(((DateTime)records[0].Fields[fields[1].Id.ToString()]!).Date);
    }

    [Fact]
    public void TimelineView_NarrowContainer_HidesSidebarByDefault()
    {
        var (fields, records) = TimelineSeed();
        var cut = Render<TmNotionDbTimelineView>(p => p
            .Add(x => x.Fields, fields)
            .Add(x => x.Records, records)
            .Add(x => x.StartDateFieldId, fields[1].Id)
            .AddCascadingValue(new TmLayoutContext(TmLayoutMode.Mobile, TmLayoutMode.Mobile)));

        cut.Find(".tm-dbtl").GetAttribute("data-layout").Should().Be("mobile");
        cut.FindAll(".tm-dbtl__sidebar").Should().BeEmpty();
    }

    [Fact]
    public void TimelineView_TapBar_OpensDateSheet()
    {
        var (fields, records) = TimelineSeed();
        var cut = Render<TmNotionDbTimelineView>(p => p
            .Add(x => x.Fields, fields)
            .Add(x => x.Records, records)
            .Add(x => x.StartDateFieldId, fields[1].Id)
            .Add(x => x.EndDateFieldId, fields[2].Id)
            .AddCascadingValue(new TmLayoutContext(TmLayoutMode.Mobile, TmLayoutMode.Mobile)));

        cut.Find("[data-testid=notion-db-timeline-bar]").Click();

        var sheet = cut.Find("[data-testid=notion-db-timeline-date-sheet]");
        cut.Find(".tm-modal-title, .tm-modal__title, [class*=modal-title]").TextContent.Should().Contain("Alpha");
        sheet.Should().NotBeNull();
        cut.FindComponent<TmModal>().Instance.Show.Should().BeTrue();
    }

    [Theory]
    [InlineData(TimelineZoomLevel.Day, 44, 1)]
    [InlineData(TimelineZoomLevel.Week, 100.0 / 7.0, 7)]
    [InlineData(TimelineZoomLevel.Month, 110.0 / 30.0, 30)]
    public void TimelineGeometry_PixelsMapToDays(TimelineZoomLevel zoom, double pxPerDay, int expectedDays)
    {
        var days = TimelineGeometry.DaysForPixels(pxPerDay * expectedDays, zoom);
        days.Should().Be(expectedDays);
        TimelineGeometry.PixelsPerDay(zoom).Should().BeApproximately(pxPerDay, 0.001);
    }

    private static DatabaseField StatusField() => new()
    {
        Id = Guid.NewGuid(),
        Name = "Status",
        Type = DatabaseFieldType.Select,
        Config = new SelectFieldConfig
        {
            Options =
            [
                new SelectFieldOption("todo", "Todo", "gray"),
                new SelectFieldOption("done", "Done", "green")
            ]
        }
    };

    private static (DatabaseField Title, DatabaseField When, IDatabaseRecord[] Records) CalendarSeed(int count)
    {
        var title = new DatabaseField { Id = Guid.NewGuid(), Name = "Name", Type = DatabaseFieldType.Text, IsPrimary = true };
        var when = new DatabaseField { Id = Guid.NewGuid(), Name = "When", Type = DatabaseFieldType.Date };
        var day = new DateOnly(DateTime.Today.Year, DateTime.Today.Month, 4);
        var records = Enumerable.Range(1, count).Select(i => (IDatabaseRecord)new DatabaseRecord
        {
            Id = Guid.NewGuid(),
            Fields = new Dictionary<string, object?>
            {
                [title.Id.ToString()] = $"Record {i}",
                [when.Id.ToString()] = day.ToDateTime(TimeOnly.MinValue)
            }
        }).ToArray();
        return (title, when, records);
    }

    private static (IDatabaseField[] Fields, IDatabaseRecord[] Records) TimelineSeed()
    {
        var title = new DatabaseField { Id = Guid.NewGuid(), Name = "Name", Type = DatabaseFieldType.Text, IsPrimary = true };
        var start = new DatabaseField { Id = Guid.NewGuid(), Name = "Start", Type = DatabaseFieldType.Date };
        var end = new DatabaseField { Id = Guid.NewGuid(), Name = "End", Type = DatabaseFieldType.Date };
        var day = DateTime.Today;
        var record = new DatabaseRecord
        {
            Id = Guid.NewGuid(),
            Fields = new Dictionary<string, object?>
            {
                [title.Id.ToString()] = "Alpha",
                [start.Id.ToString()] = day,
                [end.Id.ToString()] = day.AddDays(7)
            }
        };
        return ([title, start, end], [record]);
    }
}
