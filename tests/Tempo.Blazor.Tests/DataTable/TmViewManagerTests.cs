using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using NSubstitute;
using Tempo.Blazor.Components.DataTable;
using Tempo.Blazor.Interfaces;
using Tempo.Blazor.Models;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.DataTable;

public class TmViewManagerTests : LocalizationTestBase
{
    private static List<DataTableView> SampleViews =>
    [
        new() { Id = "v1", Name = "All Items", Scope = ViewScope.Personal, IsDefault = true, CreatedBy = "test-user" },
        new() { Id = "v2", Name = "Active Only", Scope = ViewScope.Personal, IsDefault = false, CreatedBy = "test-user" },
    ];

    private const string TestViewContext = "test-context";
    private const string TestUserId = "test-user";

    private IDataTableViewProvider BuildProvider()
    {
        var provider = Substitute.For<IDataTableViewProvider>();
        provider.GetViewsAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<IEnumerable<DataTableView>>(SampleViews));
        provider.SaveViewAsync(Arg.Any<string>(), Arg.Any<DataTableView>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(ci =>
                {
                    var v = ci.Arg<DataTableView>();
                    v.Id ??= Guid.NewGuid().ToString();
                    return Task.FromResult(v);
                });
        provider.DeleteViewAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(Task.CompletedTask);
        return provider;
    }

    [Fact]
    public async Task ViewManager_LoadsViewsFromProvider()
    {
        var provider = BuildProvider();
        var cut = RenderComponent<TmViewManager>(p => p
            .Add(c => c.Provider, provider)
            .Add(c => c.ViewContext, TestViewContext)
            .Add(c => c.CurrentUserId, TestUserId));

        await cut.InvokeAsync(() => { });

        cut.Find(".tm-view-manager-toggle").Click();
        cut.FindAll(".tm-view-item").Count.Should().Be(2);
    }

    [Fact]
    public async Task ViewManager_SelectView_FiresOnViewApplied()
    {
        var provider = BuildProvider();
        DataTableView? applied = null;
        var cut = RenderComponent<TmViewManager>(p => p
            .Add(c => c.Provider, provider)
            .Add(c => c.ViewContext, TestViewContext)
            .Add(c => c.CurrentUserId, TestUserId)
            .Add(c => c.OnViewApplied,
                EventCallback.Factory.Create<DataTableView>(this, v => applied = v)));

        await cut.InvokeAsync(() => { });
        cut.Find(".tm-view-manager-toggle").Click();
        cut.FindAll(".tm-view-item").First().Click();

        applied.Should().NotBeNull();
        applied!.Name.Should().Be("All Items");
    }

    [Fact]
    public async Task ViewManager_DefaultView_MarkedWithStar()
    {
        var provider = BuildProvider();
        var cut = RenderComponent<TmViewManager>(p => p
            .Add(c => c.Provider, provider)
            .Add(c => c.ViewContext, TestViewContext)
            .Add(c => c.CurrentUserId, TestUserId));

        await cut.InvokeAsync(() => { });
        cut.Find(".tm-view-manager-toggle").Click();

        // Default view has star badge (★) in tm-view-item-badge element
        cut.FindAll(".tm-view-item-badge").Count.Should().Be(1);
    }

    [Fact]
    public async Task ViewManager_SaveView_CallsProviderSaveViewAsync()
    {
        var provider = BuildProvider();
        var cut = RenderComponent<TmViewManager>(p => p
            .Add(c => c.Provider, provider)
            .Add(c => c.ViewContext, TestViewContext)
            .Add(c => c.CurrentUserId, TestUserId)
            .Add(c => c.AvailableColumns, 
                new List<ViewColumnInfo> { new() { Key = "col1", Title = "Column 1", Visible = true } })
            .Add(c => c.GetCurrentView, () => new DataTableView { Name = "My View", VisibleColumns = ["col1"] }));

        await cut.InvokeAsync(() => { });

        cut.Find(".tm-view-manager-toggle").Click();
        cut.Find(".tm-btn-primary").Click(); // Create New View button
        
        // Fill in the form
        cut.Find("input[type=text]").Change("My View");
        
        // Save the view
        cut.FindAll(".tm-view-modal-footer button")[1].Click(); // Second button is Save

        await cut.InvokeAsync(() => { });

        await provider.Received(1).SaveViewAsync(
            Arg.Is<string>(ctx => ctx == TestViewContext),
            Arg.Is<DataTableView>(v => v.Name == "My View"),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ViewManager_DeleteView_CallsProviderDeleteViewAsync()
    {
        var provider = BuildProvider();
        var cut = RenderComponent<TmViewManager>(p => p
            .Add(c => c.Provider, provider)
            .Add(c => c.ViewContext, TestViewContext)
            .Add(c => c.CurrentUserId, TestUserId));

        await cut.InvokeAsync(() => { });
        cut.Find(".tm-view-manager-toggle").Click();

        // Click first delete button - should delete one of our test views
        cut.FindAll(".tm-view-delete-btn")[0].Click();
        await cut.InvokeAsync(() => { });

        // Verify DeleteViewAsync was called with correct context and one of our view IDs
        await provider.Received(1).DeleteViewAsync(
            Arg.Is<string>(ctx => ctx == TestViewContext),
            Arg.Is<string>(id => id == "v1" || id == "v2"),
            Arg.Any<CancellationToken>());
    }
}
