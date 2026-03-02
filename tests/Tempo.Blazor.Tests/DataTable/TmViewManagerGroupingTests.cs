using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using NSubstitute;
using Tempo.Blazor.Components.DataTable;
using Tempo.Blazor.Interfaces;
using Tempo.Blazor.Models;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.DataTable;

public class TmViewManagerGroupingTests : LocalizationTestBase
{
    private const string TestViewContext = "test-context";
    private const string TestUserId = "test-user";

    private static List<ViewColumnInfo> AvailableColumns =>
    [
        new() { Key = "Name", Title = "Name", Visible = true },
        new() { Key = "Department", Title = "Department", Visible = true },
        new() { Key = "Status", Title = "Status", Visible = true }
    ];

    private static List<ViewColumnInfo> GroupableColumns =>
    [
        new() { Key = "Department", Title = "Department" },
        new() { Key = "Status", Title = "Status" }
    ];

    private IDataTableViewProvider BuildProvider(List<DataTableView>? views = null)
    {
        var provider = Substitute.For<IDataTableViewProvider>();
        var viewList = views ?? [];
        provider.GetViewsAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<IEnumerable<DataTableView>>(viewList));
        provider.SaveViewAsync(Arg.Any<string>(), Arg.Any<DataTableView>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(ci =>
                {
                    var v = ci.Arg<DataTableView>();
                    v.Id ??= Guid.NewGuid().ToString();
                    return Task.FromResult(v);
                });
        return provider;
    }

    // --- Groupable columns in modal ---

    [Fact]
    public async Task ViewManager_Modal_ShowsGroupableColumns()
    {
        var provider = BuildProvider();
        var cut = RenderComponent<TmViewManager>(p => p
            .Add(c => c.Provider, provider)
            .Add(c => c.ViewContext, TestViewContext)
            .Add(c => c.CurrentUserId, TestUserId)
            .Add(c => c.AvailableColumns, AvailableColumns)
            .Add(c => c.AvailableGroupableColumns, GroupableColumns));

        await cut.InvokeAsync(() => { });

        // Open create modal
        cut.Find(".tm-view-manager-toggle").Click();
        cut.Find(".tm-btn-primary").Click();

        // Should show grouping section
        cut.FindAll(".tm-view-group-columns").Should().HaveCount(1);
    }

    [Fact]
    public async Task ViewManager_Modal_NoGroupableColumns_NoGroupSection()
    {
        var provider = BuildProvider();
        var cut = RenderComponent<TmViewManager>(p => p
            .Add(c => c.Provider, provider)
            .Add(c => c.ViewContext, TestViewContext)
            .Add(c => c.CurrentUserId, TestUserId)
            .Add(c => c.AvailableColumns, AvailableColumns));

        await cut.InvokeAsync(() => { });

        // Open create modal
        cut.Find(".tm-view-manager-toggle").Click();
        cut.Find(".tm-btn-primary").Click();

        // No grouping section when no groupable columns
        cut.FindAll(".tm-view-group-columns").Should().BeEmpty();
    }

    // --- Save includes GroupByColumns ---

    [Fact]
    public async Task ViewManager_SaveView_IncludesGroupByColumns()
    {
        var provider = BuildProvider();
        var cut = RenderComponent<TmViewManager>(p => p
            .Add(c => c.Provider, provider)
            .Add(c => c.ViewContext, TestViewContext)
            .Add(c => c.CurrentUserId, TestUserId)
            .Add(c => c.AvailableColumns, AvailableColumns)
            .Add(c => c.AvailableGroupableColumns, GroupableColumns));

        await cut.InvokeAsync(() => { });

        // Open create modal
        cut.Find(".tm-view-manager-toggle").Click();
        cut.Find(".tm-btn-primary").Click();

        // Fill in name
        cut.Find("input[type=text]").Change("Grouped View");

        // Select a group column
        var groupCheckboxes = cut.FindAll(".tm-view-group-columns input[type='checkbox']");
        groupCheckboxes.First().Change(true);

        // Save
        cut.FindAll(".tm-view-modal-footer button")[1].Click();
        await cut.InvokeAsync(() => { });

        await provider.Received(1).SaveViewAsync(
            Arg.Any<string>(),
            Arg.Is<DataTableView>(v => v.GroupByColumns.Contains("Department")),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    // --- Edit view pre-loads GroupByColumns ---

    [Fact]
    public async Task ViewManager_EditView_PreloadsGroupByColumns()
    {
        var existingView = new DataTableView
        {
            Id = "v1",
            Name = "Grouped",
            Scope = ViewScope.Personal,
            CreatedBy = TestUserId,
            VisibleColumns = ["Name", "Department"],
            GroupByColumns = ["Department"]
        };
        var provider = BuildProvider([existingView]);

        var cut = RenderComponent<TmViewManager>(p => p
            .Add(c => c.Provider, provider)
            .Add(c => c.ViewContext, TestViewContext)
            .Add(c => c.CurrentUserId, TestUserId)
            .Add(c => c.AvailableColumns, AvailableColumns)
            .Add(c => c.AvailableGroupableColumns, GroupableColumns));

        await cut.InvokeAsync(() => { });

        // Open dropdown and click edit
        cut.Find(".tm-view-manager-toggle").Click();
        cut.Find(".tm-view-edit-btn").Click();

        // The Department group checkbox should be checked
        var groupCheckboxes = cut.FindAll(".tm-view-group-columns input[type='checkbox']");
        groupCheckboxes.Should().Contain(cb => cb.HasAttribute("checked"));
    }

    // --- Apply view with GroupByColumns fires callback ---

    [Fact]
    public async Task ViewManager_ApplyView_PassesGroupByColumns()
    {
        var existingView = new DataTableView
        {
            Id = "v1",
            Name = "Grouped",
            Scope = ViewScope.Personal,
            CreatedBy = TestUserId,
            VisibleColumns = ["Name", "Department"],
            GroupByColumns = ["Department", "Status"]
        };
        var provider = BuildProvider([existingView]);

        DataTableView? applied = null;
        var cut = RenderComponent<TmViewManager>(p => p
            .Add(c => c.Provider, provider)
            .Add(c => c.ViewContext, TestViewContext)
            .Add(c => c.CurrentUserId, TestUserId)
            .Add(c => c.AvailableColumns, AvailableColumns)
            .Add(c => c.AvailableGroupableColumns, GroupableColumns)
            .Add(c => c.OnViewApplied,
                EventCallback.Factory.Create<DataTableView>(this, v => applied = v)));

        await cut.InvokeAsync(() => { });

        // Open dropdown and apply view
        cut.Find(".tm-view-manager-toggle").Click();
        cut.Find(".tm-view-item").Click();

        applied.Should().NotBeNull();
        applied!.GroupByColumns.Should().BeEquivalentTo(["Department", "Status"]);
    }
}
