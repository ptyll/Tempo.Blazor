using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Tempo.Blazor.Demo.Shared;

namespace Tempo.Blazor.Demo.Api.Tests;

public class ImageEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public ImageEndpointTests(WebApplicationFactory<Program> factory)
        => _client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

    [Fact]
    public async Task GetImages_ReturnsList()
    {
        var images = await _client.GetFromJsonAsync<List<GalleryImageDto>>(
            "/api/images/gallery");

        Assert.NotNull(images);
        Assert.True(images.Count > 0);
    }

    [Fact]
    public async Task PostImageTicket_ReturnsTicketUrl()
    {
        var response = await _client.PostAsync("/api/images/1/ticket", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<TicketResponse>();
        Assert.NotNull(result);
        Assert.Contains("/api/images/stream/", result.TicketUrl);
    }

    [Fact]
    public async Task GetImageStream_ReturnsRedirectAndConsumesTicket()
    {
        var ticketResponse = await _client.PostAsync("/api/images/1/ticket", null);
        var ticket = await ticketResponse.Content.ReadFromJsonAsync<TicketResponse>();

        var streamResponse = await _client.GetAsync(ticket!.TicketUrl);
        Assert.Equal(HttpStatusCode.Redirect, streamResponse.StatusCode);

        var secondResponse = await _client.GetAsync(ticket.TicketUrl);
        Assert.Equal(HttpStatusCode.NotFound, secondResponse.StatusCode);
    }

    [Fact]
    public async Task GetImageStream_InvalidTicket_Returns404()
    {
        var response = await _client.GetAsync("/api/images/stream/invalid-ticket");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DeleteImage_RemovesImage()
    {
        var images = await _client.GetFromJsonAsync<List<GalleryImageDto>>("/api/images/gallery");
        var lastImage = images!.Last();

        var response = await _client.DeleteAsync($"/api/images/{lastImage.Id}");
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var imagesAfter = await _client.GetFromJsonAsync<List<GalleryImageDto>>("/api/images/gallery");
        Assert.DoesNotContain(imagesAfter!, i => i.Id == lastImage.Id);
    }

    private record TicketResponse(string TicketUrl);
}
