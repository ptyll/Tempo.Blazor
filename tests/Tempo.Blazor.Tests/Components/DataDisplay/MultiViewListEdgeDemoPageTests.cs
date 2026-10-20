using System.Net;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Tempo.Blazor.Demo.Services;
using Tempo.Blazor.Demo.SharedUI.Pages;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.DataDisplay;

/// <summary>Phase 13: the empty and very-long-title card playgrounds the edge-case E2E drives.</summary>
public class MultiViewListEdgeDemoPageTests : DemoPageLocalizationTestBase
{
    public MultiViewListEdgeDemoPageTests()
    {
        Services.AddHttpClient("DemoApi", client => client.BaseAddress = new Uri("https://demo.test"))
            .ConfigurePrimaryHttpMessageHandler(() => new EmptyApiHandler());
        Services.AddSingleton<PersonHttpDataProvider>();
        Services.AddSingleton<ViewHttpProvider>();
    }

    [Fact]
    public void EdgeSection_ShowsAnEmptyCardList_AndALongTitleCard()
    {
        var cut = Render<MultiViewListPage>();

        var section = cut.Find("[data-testid='mvl-edge-section']");
        section.QuerySelector("[data-testid='mvl-edge-empty'] .tm-empty-state").Should().NotBeNull(
            "no items in card view shows the empty state");
        section.QuerySelector("[data-testid='mvl-edge-empty'] .tm-mvl-card").Should().BeNull();

        var longCards = section.QuerySelectorAll("[data-testid='mvl-edge-long'] .tm-mvl-card");
        longCards.Length.Should().Be(2);
        section.QuerySelector("[data-testid='mvl-edge-long']")!.TextContent.Length.Should().BeGreaterThan(150);
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
