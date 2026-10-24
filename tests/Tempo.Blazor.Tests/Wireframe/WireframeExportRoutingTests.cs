using System.Net;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.Abstractions.Wireframe.Export;
using Tempo.Blazor.Components.Wireframe;
using Tempo.Blazor.Components.Wireframe.Models;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Wireframe;

/// <summary>
/// The PNG / PDF export posts to <c>api/wireframe/export/png|pdf</c>. A WebAssembly host whose own origin does not serve those endpoints
/// (the demo: the API is a separate origin) names the <c>IHttpClientFactory</c> client of its API through
/// <see cref="TmWireframeEditor.ExportHttpClientName"/>; without it the injected <c>HttpClient</c> keeps working as before.
/// </summary>
public class WireframeExportRoutingTests : LocalizationTestBase
{
    private sealed class Recorder(HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        public List<(HttpMethod Method, string Url)> Calls { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls.Add((request.Method, request.RequestUri!.ToString()));
            return Task.FromResult(new HttpResponseMessage(status) { Content = new ByteArrayContent([1, 2, 3]) });
        }
    }

    private sealed class FakeFactory(Dictionary<string, HttpClient> clients) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => clients.TryGetValue(name, out var c) ? c : new HttpClient(new Recorder());
    }

    private IRenderedComponent<TmWireframeEditor> RenderEditor(Recorder own, Recorder? api, Action<ComponentParameterCollectionBuilder<TmWireframeEditor>>? configure = null)
    {
        var registry = new WireframeComponentRegistry();
        Services.AddSingleton(registry);
        Services.AddSingleton(new HttpClient(own) { BaseAddress = new Uri("https://host.test/") });
        if (api is not null)
            Services.AddSingleton<IHttpClientFactory>(new FakeFactory(new() { ["DemoApi"] = new HttpClient(api) { BaseAddress = new Uri("https://api.test/") } }));
        JSInterop.Setup<string>("tmWireframeDesigner.exportSvg", _ => true).SetResult("<svg xmlns='http://www.w3.org/2000/svg'/>");
        JSInterop.SetupVoid("tmWireframe.download", _ => true);
        return Render<TmWireframeEditor>(p =>
        {
            p.Add(e => e.Document, new WireframeDocument { Title = "Export", Width = 800, Height = 600 });
            p.Add(e => e.LayoutMode, TmLayoutMode.Desktop);
            configure?.Invoke(p);
        });
    }

    private static Task ExportAsync(IRenderedComponent<TmWireframeEditor> cut, string format)
    {
        var dialog = cut.FindComponent<TmWireframeExportDialog>().Instance;
        return cut.InvokeAsync(() => dialog.OnExport.InvokeAsync(new WireframeExportDialogResult { Format = format, FileName = "wf", Options = new WireframeExportOptions() }));
    }

    [Fact]
    public async Task ExportHttpClientName_DefaultsToEmpty_AndTheInjectedHttpClientIsUsed()
    {
        var own = new Recorder();
        var api = new Recorder();
        var cut = RenderEditor(own, api);
        cut.Instance.ExportHttpClientName.Should().BeEmpty();

        await ExportAsync(cut, "png");

        own.Calls.Should().ContainSingle().Which.Url.Should().Be("https://host.test/api/wireframe/export/png");
        api.Calls.Should().BeEmpty("nothing asked for the named client");
    }

    [Theory]
    [InlineData("png")]
    [InlineData("pdf")]
    public async Task ExportHttpClientName_RoutesPngAndPdfToTheNamedClient(string format)
    {
        var own = new Recorder(HttpStatusCode.MethodNotAllowed);
        var api = new Recorder();
        var cut = RenderEditor(own, api, p => p.Add(e => e.ExportHttpClientName, "DemoApi"));

        await ExportAsync(cut, format);

        api.Calls.Should().ContainSingle().Which.Url.Should().Be($"https://api.test/api/wireframe/export/{format}");
        own.Calls.Should().BeEmpty("the host origin answers 405 for the export endpoints, so it is never asked");
        cut.FindAll("[role='alert']").Should().BeEmpty("no export error");
    }

    [Fact]
    public async Task ExportHttpClientName_WithoutAFactory_FallsBackToTheInjectedHttpClient()
    {
        var own = new Recorder();
        var cut = RenderEditor(own, api: null, p => p.Add(e => e.ExportHttpClientName, "DemoApi"));

        await ExportAsync(cut, "png");

        own.Calls.Should().ContainSingle();
    }

    [Fact]
    public async Task AFailingExport_ShowsTheLocalizedError_NotAnException()
    {
        var own = new Recorder();
        var api = new Recorder(HttpStatusCode.MethodNotAllowed);
        var cut = RenderEditor(own, api, p => p.Add(e => e.ExportHttpClientName, "DemoApi"));

        await ExportAsync(cut, "png");

        cut.WaitForAssertion(() => cut.Find(".tm-wd-editor__error-banner").TextContent.Should().Contain("405"));
    }
}
