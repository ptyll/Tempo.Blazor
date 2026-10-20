using System.Net;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Tempo.Blazor.Demo.Services;
using Tempo.Blazor.Demo.SharedUI.Pages;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.DataDisplay;

public class MultiViewListDemoPageTests : DemoPageLocalizationTestBase
{
    public MultiViewListDemoPageTests()
    {
        Services.AddHttpClient("DemoApi", client =>
            client.BaseAddress = new Uri("https://demo.test"))
            .ConfigurePrimaryHttpMessageHandler(() => new DemoApiHandler());
        Services.AddSingleton<PersonHttpDataProvider>();
        Services.AddSingleton<ViewHttpProvider>();
    }

    [Fact]
    public void RedesignSection_OffersEveryOptIn()
    {
        var cut = Render<MultiViewListPage>();

        var section = cut.Find("[data-testid='mvl-redesign-section']");
        section.QuerySelector("[data-testid='mvl-redesign-switcher-labels']").Should().NotBeNull();
        section.QuerySelector("[data-testid='mvl-redesign-avatar-fallback']").Should().NotBeNull();
        section.QuerySelector("[data-testid='mvl-redesign-cell-layout']").Should().NotBeNull();
        section.QuerySelector("[data-testid='mvl-redesign-card-activation']").Should().NotBeNull();
        section.QuerySelector("[data-testid='mvl-redesign-item-count']").Should().NotBeNull();
        section.QuerySelector("[data-testid='mvl-redesign-responsive']").Should().NotBeNull();
        section.QuerySelector("[data-testid='mvl-redesign-max-columns']").Should().NotBeNull();
        section.QuerySelector("[data-testid='mvl-redesign-item-href']").Should().NotBeNull();
        section.QuerySelector("[data-testid='mvl-redesign-status-variant']").Should().NotBeNull();
        section.QuerySelector("[data-testid='mvl-redesign-group-header']").Should().NotBeNull();
        section.QuerySelector("[data-testid='mvl-redesign-result-preview']").Should().NotBeNull();
        section.QuerySelector("[data-testid='mvl-redesign-list'] .tm-filter-result-preview").Should().NotBeNull(
            "ShowFilterResultPreview is on, with a view provider and filter definitions so the count renders");
        var cards = cut.Find("[data-testid='mvl-redesign-cards']");
        cards.QuerySelector(".tm-mvl-card").Should().NotBeNull("the same people render as cards");
        cards.QuerySelector(".tm-mvl-group-btn")!.Click();
        cut.WaitForAssertion(() =>
            cut.Find("[data-testid='mvl-redesign-cards'] .tm-mvl-group-option input").Should().NotBeNull());
        cut.Find("[data-testid='mvl-redesign-cards'] .tm-mvl-group-option input").Change(true);
        cut.WaitForAssertion(() =>
        {
            var grouped = cut.Find("[data-testid='mvl-redesign-cards']");
            grouped.QuerySelector(".tm-mvl-group-header").Should().NotBeNull("grouping by role is available in the card view");
            grouped.TextContent.Should().Contain("Developer").And.Contain("Analyst");
        });

        var list = section.QuerySelector("[data-testid='mvl-redesign-list'] .tm-multi-view-list");
        list.Should().NotBeNull();
        list!.ClassList.Should().NotContain("tm-multi-view-list--responsive",
            "responsive stays off until the toggle opts in");
        section.QuerySelector("[data-testid='mvl-redesign-list'] .tm-mvl-item-count").Should().BeNull();
    }

    [Fact]
    public void RedesignSection_TogglingResponsive_AddsTheResponsiveClass()
    {
        var cut = Render<MultiViewListPage>();
        var section = cut.Find("[data-testid='mvl-redesign-section']");

        section.QuerySelector("[data-testid='mvl-redesign-responsive'] input")!.Change(true);

        cut.WaitForAssertion(() =>
            cut.Find("[data-testid='mvl-redesign-list'] .tm-multi-view-list")
                .ClassList.Should().Contain("tm-multi-view-list--responsive"));
    }

    private sealed class DemoApiHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri?.AbsolutePath ?? string.Empty;
            var content = path.StartsWith("/api/persons", StringComparison.Ordinal)
                ? "{\"items\":[],\"totalCount\":0}"
                : "[]";

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(content, Encoding.UTF8, "application/json")
            });
        }
    }
}
