using System.Net;
using System.Text;
using FluentAssertions;
using Tempo.Blazor.Demo.Services;
using Tempo.Blazor.DocumentLibrary;

namespace Tempo.Blazor.Tests.Services;

/// <summary>
/// Tests that <see cref="ApiTempoDocumentLibraryProvider"/> builds correct request URLs and maps
/// HTTP outcomes, using a stub message handler (no live server).
/// </summary>
public class ApiTempoDocumentLibraryProviderTests
{
    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(responder(request));
        }
    }

    private sealed class StubFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
            => new(handler) { BaseAddress = new Uri("http://localhost") };
    }

    private static (ApiTempoDocumentLibraryProvider Provider, StubHandler Handler) Build(
        Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        var handler = new StubHandler(responder);
        return (new ApiTempoDocumentLibraryProvider(new StubFactory(handler)), handler);
    }

    private static HttpResponseMessage Json(string json)
        => new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    [Fact]
    public async Task BrowseAsync_BuildsQueryString_AndDeserialises()
    {
        var (provider, handler) = Build(_ => Json(
            "{\"items\":[{\"id\":\"" + Guid.NewGuid() + "\",\"name\":\"Doc\",\"kind\":\"wireframe\"}],\"totalCount\":1}"));

        var page = await provider.BrowseAsync(new DocumentLibraryQuery
        {
            Kind = TempoDocumentKind.Wireframe,
            FolderPath = "/Designs",
            Search = "home",
            Descending = true,
            Take = 10
        });

        page.TotalCount.Should().Be(1);
        page.Items.Should().ContainSingle().Which.Name.Should().Be("Doc");

        var url = handler.LastRequest!.RequestUri!.ToString();
        url.Should().Contain("/api/document-library/wireframe/browse");
        url.Should().Contain("folderPath=%2FDesigns");
        url.Should().Contain("search=home");
        url.Should().Contain("descending=true");
        url.Should().Contain("take=10");
    }

    [Theory]
    [InlineData(DocumentLibraryScope.Folder, false)]
    [InlineData(DocumentLibraryScope.Subtree, true)]
    [InlineData(DocumentLibraryScope.Library, true)]
    public async Task BrowseAsync_SendsTheScope_OnlyWhenItIsWiderThanFolder(DocumentLibraryScope scope, bool expected)
    {
        var (provider, handler) = Build(_ => Json("{\"items\":[],\"totalCount\":0}"));

        await provider.BrowseAsync(new DocumentLibraryQuery { Kind = TempoDocumentKind.Wireframe, Scope = scope });

        var url = handler.LastRequest!.RequestUri!.ToString();
        if (expected)
        {
            url.Should().Contain($"scope={scope}");
        }
        else
        {
            url.Should().NotContain("scope=");
        }
    }

    [Fact]
    public async Task GetFolderTreeAsync_HitsTreeEndpoint()
    {
        var (provider, handler) = Build(_ => Json("{\"path\":\"/\",\"name\":\"/\",\"children\":[]}"));

        var tree = await provider.GetFolderTreeAsync(TempoDocumentKind.Diagram);

        tree.Path.Should().Be("/");
        handler.LastRequest!.RequestUri!.ToString().Should().EndWith("/api/document-library/diagram/tree");
    }

    [Fact]
    public async Task CreateFolderAsync_Conflict_ThrowsInvalidOperation()
    {
        var (provider, _) = Build(_ => new HttpResponseMessage(HttpStatusCode.Conflict));

        var act = async () => await provider.CreateFolderAsync(TempoDocumentKind.Wireframe, "/", "Designs");

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task DeleteFolderAsync_UsesDeleteVerb_WithEscapedPath()
    {
        var (provider, handler) = Build(_ => new HttpResponseMessage(HttpStatusCode.NoContent));

        await provider.DeleteFolderAsync(TempoDocumentKind.Spreadsheet, "/Reports/2026");

        handler.LastRequest!.Method.Should().Be(HttpMethod.Delete);
        handler.LastRequest.RequestUri!.ToString()
            .Should().Contain("/api/document-library/spreadsheet/folders?folderPath=%2FReports%2F2026");
    }
}
