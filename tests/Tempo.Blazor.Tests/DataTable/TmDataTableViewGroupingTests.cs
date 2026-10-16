using Bunit;
using FluentAssertions;
using NSubstitute;
using Tempo.Blazor.Components.DataTable;
using Tempo.Blazor.Interfaces;
using Tempo.Blazor.Models;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.DataTable;

public class TmDataTableViewGroupingTests : LocalizationTestBase
{
    private record GroupRow(string Name, string City);

    [Fact]
    public void ViewDialog_ShowsGroupingSection_WhenGroupableColumnExists()
    {
        var provider = Substitute.For<IDataTableViewProvider>();
        provider.GetViewsAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IEnumerable<DataTableView>>([]));

        var cut = Render<TmDataTable<GroupRow>>(p => p
            .Add(c => c.Items, [new GroupRow("Ada", "Prague")])
            .Add(c => c.ViewProvider, provider)
            .Add(c => c.ViewContext, "people")
            .AddChildContent(b =>
            {
                b.OpenComponent<TmDataTableColumn<GroupRow>>(0);
                b.AddAttribute(1, "Title", "City");
                b.AddAttribute(2, "PropertyName", "City");
                b.AddAttribute(3, "Groupable", true);
                b.AddAttribute(4, "Field", (Func<GroupRow, object?>)(x => x.City));
                b.CloseComponent();
            }));

        cut.Find(".tm-view-manager-toggle").Click();
        cut.Find(".tm-btn-primary").Click();

        cut.FindAll(".tm-view-group-columns").Should().ContainSingle();
        cut.Markup.Should().Contain("City");
    }
}
