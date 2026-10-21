using System.Net;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Tempo.Blazor.Demo.Services;
using Tempo.Blazor.Demo.SharedUI.Pages;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.DataTable;

/// <summary>
/// Phase 13: the edge-case playgrounds the E2E and screenshot specs drive. Their data is fixed so a
/// screenshot is a measurement: an empty table, a loading table, a very long cell and view name, and
/// fifteen columns that have to scroll.
/// </summary>
public class DataTableEdgeDemoPageTests : DemoPageLocalizationTestBase
{
    public DataTableEdgeDemoPageTests()
    {
        Services.AddHttpClient("DemoApi", client => client.BaseAddress = new Uri("https://demo.test"))
            .ConfigurePrimaryHttpMessageHandler(() => new EmptyApiHandler());
        Services.AddSingleton<PersonHttpDataProvider>();
        Services.AddSingleton<ViewHttpProvider>();
    }

    [Fact]
    public void EdgeSection_RendersEveryScenarioWithFixedData()
    {
        var cut = Render<DataTablePage>();

        var section = cut.Find("[data-testid='dt-edge-section']");
        section.QuerySelector("[data-testid='dt-edge-empty'] .tm-empty-state").Should().NotBeNull(
            "an empty table shows its empty state, not a bare header");
        section.QuerySelector("[data-testid='dt-edge-loading'] .tm-data-table-loading-row").Should().NotBeNull(
            "IsLoading is pinned on so the loading state can be captured");
        section.QuerySelector("[data-testid='dt-edge-long'] tbody tr").Should().NotBeNull();
        section.QuerySelectorAll("[data-testid='dt-edge-wide'] thead th").Length.Should().BeGreaterThanOrEqualTo(15,
            "fifteen data columns force a horizontal scroll");
    }

    [Fact]
    public void EdgeSection_LongCellAndLongViewName_AreRenderedInFull()
    {
        var cut = Render<DataTablePage>();

        var longCell = cut.Find("[data-testid='dt-edge-long']");
        longCell.TextContent.Length.Should().BeGreaterThan(200, "the cell text is long enough to need truncation");
        longCell.QuerySelector(".tm-view-manager-toggle").Should().NotBeNull();

        // The toggle names the view once it is applied, so open the panel and pick the long-named view.
        cut.Find("[data-testid='dt-edge-long'] .tm-view-manager-toggle").Click();
        cut.WaitForAssertion(() => cut.Find("[data-testid='dt-edge-long'] .tm-view-item").Should().NotBeNull());
        cut.Find("[data-testid='dt-edge-long'] .tm-view-item").Click();
        cut.WaitForAssertion(() =>
            cut.Find("[data-testid='dt-edge-long'] .tm-view-manager-toggle").TextContent.Length.Should().BeGreaterThan(60,
                "the active view carries a very long name"));
    }

    [Fact]
    public void PagerAndGridEditSections_RenderForTheKeyboardAndMobileSpecs()
    {
        var cut = Render<DataTablePage>();

        cut.Find("[data-testid='dt-edge-pager-wide'] .tm-data-table-pager--responsive").Should().NotBeNull();
        cut.Find("[data-testid='dt-edge-pager-narrow'] .tm-data-table-pager--responsive").Should().NotBeNull();
        var edit = cut.Find("[data-testid='dt-grid-edit']");
        edit.QuerySelector("td[data-grid-cell]").Should().NotBeNull("the grid-edit table uses the Grid keyboard model");
        cut.Find("[data-testid='dt-grid-edit-saved']").TextContent.Trim().Should().BeEmpty("nothing is saved until the spec edits a row");
    }

    private sealed class EmptyApiHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var content = request.RequestUri?.AbsolutePath.StartsWith("/api/persons", StringComparison.Ordinal) == true
                ? "{\"items\":[],\"totalCount\":0}"
                : "[]";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(content, Encoding.UTF8, "application/json")
            });
        }
    }
}
