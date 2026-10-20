using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using NSubstitute;
using Tempo.Blazor.Interfaces;
using Tempo.Blazor.Components.DataTable;
using Tempo.Blazor.Components.Dropdowns;
using Tempo.Blazor.Models;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.DataTable;

public class TmDataTableToolbarOutlineTests : LocalizationTestBase
{
    private record Person(string Name);

    [Fact]
    public void ViewManagerToggle_AndExportTrigger_UseOutlineStyle()
    {
        var provider = Substitute.For<IDataTableViewProvider>();
        provider.GetViewsAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IEnumerable<DataTableView>>([]));

        var cut = Render<TmDataTable<Person>>(p => p
            .Add(c => c.Items, [new Person("Alice")])
            .Add(c => c.ViewProvider, provider)
            .Add(c => c.ShowExport, true)
            .Add(c => c.ShowViewManager, true)
            .Add(c => c.ViewContext, "outline-test")
            .AddChildContent(b =>
            {
                b.OpenComponent<TmDataTableColumn<Person>>(0);
                b.AddAttribute(1, "Title", "Name");
                b.AddAttribute(2, "Field", (Func<Person, object>)(x => x.Name));
                b.CloseComponent();
            }));

        cut.Find(".tm-view-manager-toggle").ClassList.Should().Contain("tm-btn-outline");
    }

    [Fact]
    public void DropdownOutlineTrigger_IsOptIn()
    {
        var defaultDropdown = Render<TmDropdown>(p => p.Add(c => c.Text, "Default"));
        defaultDropdown.Find(".tm-dropdown-trigger").ClassList.Should().NotContain("tm-btn").And.NotContain("tm-btn-outline");

        var outlinedDropdown = Render<TmDropdown>(p => p
            .Add(c => c.Text, "Export")
            .Add(c => c.OutlineTrigger, true));
        outlinedDropdown.Find(".tm-dropdown-trigger").ClassList.Should().Contain("tm-btn").And.Contain("tm-btn-outline");
    }
}
