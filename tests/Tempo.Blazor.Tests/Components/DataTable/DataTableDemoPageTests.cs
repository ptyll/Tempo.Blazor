using System.Net;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Tempo.Blazor.Demo.Services;
using Tempo.Blazor.Demo.SharedUI.Pages;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.DataTable;

public class DataTableDemoPageTests : LocalizationTestBase
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
        exportSection.TextContent.Should().Contain("CSV").And.Contain("XLSX");

        cut.Find("[data-testid='dt-grid-keyboard-section']").Should().NotBeNull();
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
