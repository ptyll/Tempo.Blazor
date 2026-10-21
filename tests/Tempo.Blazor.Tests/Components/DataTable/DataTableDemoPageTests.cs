using System.Net;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Tempo.Blazor.Components.DataTable;
using Tempo.Blazor.Demo.Services;
using Tempo.Blazor.Demo.SharedUI.Pages;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.DataTable;

public class DataTableDemoPageTests : DemoPageLocalizationTestBase
{
    public DataTableDemoPageTests()
    {
        Services.AddHttpClient("DemoApi", client =>
            client.BaseAddress = new Uri("https://demo.test"))
            .ConfigurePrimaryHttpMessageHandler(() => new DemoApiHandler());
        Services.AddSingleton<PersonHttpDataProvider>();
        Services.AddSingleton<ViewHttpProvider>();
    }

    [Fact]
    public void RendersDedicatedTransactionEditingAndExportSections()
    {
        var cut = Render<DataTablePage>();

        var inlineSection = cut.Find("[data-testid='dt-inline-edit-section']");
        inlineSection.QuerySelector("[data-testid='transaction-table']").Should().NotBeNull();
        inlineSection.TextContent.Should().Contain("Amount").And.Contain("Category").And.Contain("Note");

        var exportSection = cut.Find("[data-testid='dt-export-section']");
        exportSection.QuerySelector("[data-testid='export-table']").Should().NotBeNull();
        exportSection.QuerySelectorAll("tbody input[type='checkbox']").Should().NotBeEmpty(
            "the export demo opts into AllowExportSelected, so the table is selectable");

        cut.Find("[data-testid='dt-grid-keyboard-section']").Should().NotBeNull();
    }

    [Fact]
    public void RedesignSection_OffersEveryOptIn_AndCountsRowActivations()
    {
        var cut = Render<DataTablePage>();

        var section = cut.Find("[data-testid='dt-redesign-section']");
        section.QuerySelector("[data-testid='dt-redesign-appearance']").Should().NotBeNull();
        section.QuerySelector("[data-testid='dt-redesign-mobile-layout']").Should().NotBeNull();
        section.QuerySelector("[data-testid='dt-redesign-keyboard']").Should().NotBeNull();
        section.QuerySelector("[data-testid='dt-redesign-column-menu']").Should().NotBeNull();
        section.QuerySelector("[data-testid='dt-redesign-edit-actions']").Should().NotBeNull();
        section.QuerySelector("[data-testid='dt-redesign-export-selected']").Should().NotBeNull();
        section.QuerySelector("[data-testid='dt-redesign-filter-summary']").Should().NotBeNull();
        section.QuerySelector("[data-testid='dt-redesign-active-view-name']").Should().NotBeNull();
        section.QuerySelector("[data-testid='dt-redesign-set-default']").Should().NotBeNull();
        section.QuerySelector("[data-testid='dt-redesign-result-preview']").Should().NotBeNull();

        var counter = section.QuerySelector("[data-testid='dt-row-activations']");
        counter.Should().NotBeNull();
        counter!.TextContent.Should().Contain("0");

        var table = section.QuerySelector("[data-testid='dt-redesign-table']");
        table.Should().NotBeNull();
        table!.QuerySelector("tbody input[type='checkbox']").Should().NotBeNull(
            "the redesign demo is selectable so export scope and grid Space can be exercised");
        table.QuerySelector(".tm-data-table-col-menu").Should().BeNull(
            "column menu starts at None and only appears after the control opts in");

        section.QuerySelector("[data-testid='dt-redesign-mobile-cards']")!
            .GetAttribute("style").Should().Contain("max-inline-size");

        var row = table.QuerySelector("tbody tr[role='row'], tbody tr");
        row.Should().NotBeNull();
        row!.Click();

        cut.WaitForAssertion(() =>
            cut.Find("[data-testid='dt-row-activations']").TextContent.Should().Contain("1"));
    }

    [Fact]
    public void ViewAndFilterSection_ShowsSetDefault_AndBothFilterPresentations()
    {
        var cut = Render<DataTablePage>();

        var views = cut.Find("[data-testid='dt-views-demo']");
        views.QuerySelector(".tm-view-manager-toggle").Should().NotBeNull();
        views.QuerySelector(".tm-view-manager-toggle")!.Click();
        cut.WaitForAssertion(() =>
        {
            var panel = cut.Find("[data-testid='dt-views-demo']");
            panel.QuerySelector(".tm-view-set-default-btn").Should().NotBeNull(
                "AllowSetDefault offers Set as default for the non-default view");
            panel.TextContent.Should().Contain("All employees").And.Contain("Engineering");
        });

        cut.Find("[data-testid='dt-views-demo'] .tm-view-item").Click();
        cut.WaitForAssertion(() =>
            cut.Find("[data-testid='dt-views-demo'] .tm-view-manager-toggle").TextContent.Should().Contain("All employees",
                "ShowActiveViewName puts the applied view's name on the toggle"));

        views.GetAttribute("style").Should().Contain("max-inline-size",
            "the narrow container is what makes the mobile sheet visible on a desktop");

        var filters = cut.Find("[data-testid='dt-filter-demo']");
        filters.QuerySelector("[data-testid='dt-filter-popover'] .tm-filter-builder").Should().NotBeNull();
        filters.QuerySelector("[data-testid='dt-filter-auto'] .tm-filter-builder").Should().NotBeNull();
        filters.QuerySelectorAll(".tm-filter-result-preview").Should().HaveCount(2,
            "both presentations opt into ShowResultPreview with a count provider");
    }

    [Fact]
    public void RedesignSection_TogglingAppearance_AddsTheCardSurface()
    {
        var cut = Render<DataTablePage>();
        var section = cut.Find("[data-testid='dt-redesign-section']");

        section.QuerySelector(".tm-data-table-wrapper--card-surface").Should().BeNull();

        section.QuerySelector("[data-testid='dt-redesign-appearance'] select")!
            .Change(DataTableAppearance.Card.ToString());

        cut.WaitForAssertion(() =>
            cut.Find("[data-testid='dt-redesign-table'] .tm-data-table-wrapper")
                .ClassList.Should().Contain("tm-data-table-wrapper--card-surface"));
    }

    private sealed class DemoApiHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
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
