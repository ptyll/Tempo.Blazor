using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using System.Linq;
using Tempo.Blazor.Abstractions.Shared;
using Tempo.Blazor.Interfaces;
using Tempo.Blazor.Components.NotionEditor.UI;
using Tempo.Blazor.Models;
using NSubstitute;
using Tempo.Blazor.Components.NotionEditor.Blocks.Database;
using Tempo.Blazor.Components.NotionEditor.Page;
using Tempo.Blazor.NotionEditor.Enums;
using Tempo.Blazor.NotionEditor.Interfaces;
using Tempo.Blazor.NotionEditor.Models;
using Tempo.Blazor.Tests.Localization;
using Xunit;

namespace Tempo.Blazor.Tests.Components.NotionEditor;

/// <summary>
/// Pins the phase-1 fixes that are observable from markup: unique ids across two instances,
/// the locked public-share fields, the calendar overflow day list, the table footer and the
/// empty board group. Each one was a reported bug, so a regression here is the bug returning.
/// </summary>
public class TmNotionPhase1FixTests : LocalizationTestBase
{
    [Fact]
    public void FieldEditor_TwoInstances_HaveUniqueIds()
    {
        var field = new DatabaseField { Id = Guid.NewGuid(), Name = "Title", Type = DatabaseFieldType.Text, IsPrimary = true };
        var first = Render<TmNotionDbFieldEditor>(p => p.Add(x => x.Field, field).Add(x => x.Fields, new[] { field }));
        var second = Render<TmNotionDbFieldEditor>(p => p.Add(x => x.Field, field).Add(x => x.Fields, new[] { field }));

        var firstId = first.Find("input").Id;
        var secondId = second.Find("input").Id;

        firstId.Should().NotBe(secondId);
        first.Find("label").GetAttribute("for").Should().Be(firstId);
        second.Find("label").GetAttribute("for").Should().Be(secondId);
    }

    [Fact]
    public void ImportExport_TwoInstances_HaveUniqueIdsAndNames()
    {
        var view = new DatabaseView { Id = Guid.NewGuid(), Name = "All", Type = DatabaseViewType.Table };
        var first = Render<TmNotionDbImportExport>(p => p
            .Add(x => x.DatabaseId, Guid.NewGuid())
            .Add(x => x.Fields, Array.Empty<IDatabaseField>())
            .Add(x => x.Views, new[] { view }));
        var second = Render<TmNotionDbImportExport>(p => p
            .Add(x => x.DatabaseId, Guid.NewGuid())
            .Add(x => x.Fields, Array.Empty<IDatabaseField>())
            .Add(x => x.Views, new[] { view }));

        var firstId = first.Find("input[type=file]").Id;
        var secondId = second.Find("input[type=file]").Id;
        firstId.Should().NotBe(secondId);

        // The radio group lives on the export tab; the file input above is the import tab.
        first.FindAll("button[role=tab]").Last().Click();
        second.FindAll("button[role=tab]").Last().Click();
        var firstName = first.Find("input[type=radio]").GetAttribute("name");
        var secondName = second.Find("input[type=radio]").GetAttribute("name");
        firstName.Should().NotBe(secondName);
    }

    [Fact]
    public async Task ShareDialog_ActiveShare_LocksCommentsAndExpiry()
    {
        var provider = Substitute.For<INotionPublicShareProvider>();
        provider.GetShareAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new PublicShareDto { PageId = Guid.NewGuid(), Token = "tok", IsEnabled = true, AllowComments = true });

        var cut = Render<TmNotionShareDialog>(p => p
            .Add(x => x.Visible, true)
            .Add(x => x.PageId, Guid.NewGuid())
            .Add(x => x.Provider, provider));

        cut.WaitForState(() => cut.FindAll("[data-testid=notion-share-locked-hint]").Count == 1);

        cut.Find("[data-testid=notion-share-allow-comments]").HasAttribute("disabled").Should().BeTrue();
        cut.Find("[data-testid=notion-share-expires]").HasAttribute("disabled").Should().BeTrue();
        cut.Markup.Should().Contain("Comments and expiry are fixed");
    }

    [Fact]
    public void CalendarOverflow_Click_OpensTheDayList()
    {
        var dateField = new DatabaseField { Id = Guid.NewGuid(), Name = "Due", Type = DatabaseFieldType.Date, IsPrimary = false };
        var title = new DatabaseField { Id = Guid.NewGuid(), Name = "Name", Type = DatabaseFieldType.Text, IsPrimary = true };
        // The view opens on the current month and the month is not a parameter, so the records
        // have to land in whatever month the component shows.
        var day = new DateOnly(DateTime.Today.Year, DateTime.Today.Month, 4);
        var records = Enumerable.Range(1, 6)
            .Select(i => (IDatabaseRecord)new DatabaseRecord
            {
                Id = Guid.NewGuid(),
                Fields = new Dictionary<string, object?>
                {
                    [dateField.Id.ToString()] = day.ToDateTime(TimeOnly.MinValue),
                    [title.Id.ToString()] = $"Record {i}",
                },
            })
            .ToList();

        var cut = Render<TmNotionDbCalendarView>(p => p
            .Add(x => x.Fields, new IDatabaseField[] { title, dateField })
            .Add(x => x.Records, records)
            .Add(x => x.DateFieldId, dateField.Id));

        cut.Find("[data-testid=notion-db-calendar-overflow]").Click();

        var list = cut.Find("[data-testid=notion-db-calendar-day-list]");
        list.TextContent.Should().Contain("Record 6");
    }

    [Fact]
    public void TableView_RendersRecordCountFooter()
    {
        var field = new DatabaseField { Id = Guid.NewGuid(), Name = "Name", Type = DatabaseFieldType.Text, IsPrimary = true };
        var records = new IDatabaseRecord[]
        {
            new DatabaseRecord { Id = Guid.NewGuid(), Fields = new Dictionary<string, object?>() },
            new DatabaseRecord { Id = Guid.NewGuid(), Fields = new Dictionary<string, object?>() },
        };

        var cut = Render<TmNotionDbTableView>(p => p
            .Add(x => x.Fields, new[] { field })
            .Add(x => x.Records, records));

        cut.Find("[data-testid=notion-db-table-footer]").TextContent.Should().Contain("2 records");
    }

    [Fact]
    public void BoardView_EmptyGroup_ShowsPlaceholder()
    {
        var field = new DatabaseField
        {
            Id = Guid.NewGuid(),
            Name = "Status",
            Type = DatabaseFieldType.Select,
            Config = new SelectFieldConfig
            {
                Options = new[] { new SelectFieldOption("todo", "To do", "gray"), new SelectFieldOption("done", "Done", "green") },
            },
        };

        var cut = Render<TmNotionDbBoardView>(p => p
            .Add(x => x.Fields, new[] { field })
            .Add(x => x.Records, Array.Empty<IDatabaseRecord>())
            .Add(x => x.GroupByField, field));

        cut.FindAll("[data-testid=notion-db-board-empty]").Should().NotBeEmpty();
        cut.Markup.Should().Contain("No records");
    }

    [Fact]
    public void AuditCloseButton_ClosesPanel()
    {
        var closed = false;
        var provider = Substitute.For<ITmActivityProvider>();
        provider.QueryAsync(Arg.Any<TmActivityQuery>(), Arg.Any<CancellationToken>())
            .Returns(new Tempo.Blazor.Models.PagedResult<TmActivityEntry> { Items = [], TotalCount = 0, Page = 1, PageSize = 10 });

        var cut = Render<TmNotionAuditLogPanel>(p => p
            .Add(x => x.ActivityProvider, provider)
            .Add(x => x.OnClosed, EventCallback.Factory.Create(this, () => closed = true)));

        cut.Find(".tm-notion-panel-header__close").Click();
        closed.Should().BeTrue();
    }

    [Fact]
    public void ActiveShare_AllowCommentsAndExpiry_AreDisabledWithHint()
    {
        var provider = Substitute.For<INotionPublicShareProvider>();
        provider.GetShareAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new PublicShareDto
            {
                PageId = Guid.NewGuid(),
                Token = "tok",
                IsEnabled = true,
                AllowComments = true
            });

        var cut = Render<TmNotionShareDialog>(p => p
            .Add(x => x.Visible, true)
            .Add(x => x.PageId, Guid.NewGuid())
            .Add(x => x.Provider, provider));

        cut.WaitForAssertion(() =>
        {
            cut.Find("[data-testid=notion-share-locked-hint]").Should().NotBeNull();
            cut.Find("[data-testid=notion-share-allow-comments]").HasAttribute("disabled").Should().BeTrue();
            cut.Find("[data-testid=notion-share-expires]").HasAttribute("disabled").Should().BeTrue();
        });
    }

    [Fact]
    public void MentionMenu_PointerMove_ChangesSelection_MouseEnterDoesNot()
    {
        var ctx = new NotionEditorContext
        {
            DataProvider = Substitute.For<INotionDataProvider>(),
            BlockService = Substitute.For<INotionEditorBlockService>()
        };
        var cut = Render<TmNotionMentionMenu>(p => p
            .Add(x => x.Visible, true)
            .Add(x => x.Top, 10)
            .Add(x => x.Left, 10)
            .AddCascadingValue(ctx));

        cut.Markup.Should().NotContain("onmouseenter");
        cut.FindAll("button, [role=tab]").First(e => e.TextContent.Contains("Date")).Click();
        cut.WaitForAssertion(() => cut.FindAll(".tm-nmm__item").Count.Should().BeGreaterThan(1));
        cut.FindAll(".tm-nmm__item")[1].TriggerEvent("onpointermove", new PointerEventArgs());
        cut.FindAll(".tm-nmm__item")[1].ClassList.Should().Contain("tm-nmm__item--selected");
    }

    [Fact]
    public void TokenDropdown_PointerMove_ChangesSelection_MouseEnterDoesNot()
    {
        var tokens = Substitute.For<ITokenDataProvider>();
        tokens.SearchTokensAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new IToken[]
            {
                new NamedToken("a", "Alpha"),
                new NamedToken("b", "Beta")
            });
        var ctx = new NotionEditorContext
        {
            DataProvider = Substitute.For<INotionDataProvider>(),
            BlockService = Substitute.For<INotionEditorBlockService>(),
            TokenProvider = tokens
        };
        var cut = Render<TmNotionTokenDropdown>(p => p
            .Add(x => x.Visible, true)
            .AddCascadingValue(ctx));

        cut.WaitForAssertion(() => cut.FindAll(".tm-notion-token-dropdown__item").Count.Should().Be(2));
        cut.Markup.Should().NotContain("onmouseenter");
        cut.FindAll(".tm-notion-token-dropdown__item")[1].TriggerEvent("onpointermove", new PointerEventArgs());
        cut.FindAll(".tm-notion-token-dropdown__item")[1].ClassList
            .Should().Contain("tm-notion-token-dropdown__item--selected");
    }

    private sealed record NamedToken(string Key, string DisplayName) : IToken
    {
        public string? Description => null;
        public string? Category => null;
        public string? Icon => null;
        public string? ColorClass => null;
        public string? TypeLabel => null;
    }

    private sealed class DatabaseView : IDatabaseView
    {
        public Guid Id { get; init; }
        public string Name { get; init; } = string.Empty;
        public DatabaseViewType Type { get; init; }
        public INotionDatabaseFilter? Filter => null;
        public IReadOnlyList<NotionDatabaseSort> Sorts => [];
        public NotionDatabaseGrouping? Grouping => null;
        public IReadOnlyList<Guid> VisibleFieldIds => [];
        public IDatabaseViewConfig? Config => null;
    }
}
