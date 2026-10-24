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

    private sealed class Capturing(Recorder inner) : HttpMessageHandler
    {
        public List<string> Bodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Bodies.Add(request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken));
            return await new HttpClient(inner).SendAsync(new HttpRequestMessage(request.Method, request.RequestUri), cancellationToken);
        }
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

    [Theory]
    [InlineData("light", null)]
    [InlineData("default", null)]
    [InlineData("INDIGO-dark", "indigo-dark")]
    [InlineData("definitely-not-a-theme", null)]
    [InlineData("bad theme!", null)]
    [InlineData(null, null)]
    public async Task Export_SendsTheResolvedCanonicalTheme_OrNull(string? stencilTheme, string? expected)
    {
        var capture = new Capturing(new Recorder());
        var registry = new WireframeComponentRegistry();
        Services.AddSingleton(registry);
        Services.AddSingleton(new HttpClient(capture) { BaseAddress = new Uri("https://host.test/") });
        JSInterop.Setup<string>("tmWireframeDesigner.exportSvg", _ => true).SetResult("<svg xmlns='http://www.w3.org/2000/svg'/>");
        JSInterop.SetupVoid("tmWireframe.download", _ => true);
        _ = new Tempo.Blazor.Components.Wireframe.BuiltInStencilPackProvider().GetDefinitions().Count();   // compiling the pack registers its theme names
        var cut = Render<TmWireframeEditor>(p =>
        {
            p.Add(e => e.Document, new WireframeDocument { Title = "Export", Width = 800, Height = 600 });
            p.Add(e => e.LayoutMode, TmLayoutMode.Desktop);
            p.Add(e => e.StencilTheme, stencilTheme);
        });

        await ExportAsync(cut, "png");

        var body = System.Text.Json.JsonDocument.Parse(capture.Bodies.Should().ContainSingle().Subject).RootElement;
        var theme = body.GetProperty("theme");
        if (expected is null) theme.ValueKind.Should().Be(System.Text.Json.JsonValueKind.Null);
        else theme.GetString().Should().Be(expected);
    }

    [Fact]
    public async Task ExportHttpClientName_FallingBack_LogsOneWarning()
    {
        var own = new Recorder();
        var logs = new WarningCollector();
        Services.AddSingleton<Microsoft.Extensions.Logging.ILoggerFactory>(new CollectingFactory(logs));
        var cut = RenderEditor(own, api: null, p => p.Add(e => e.ExportHttpClientName, "DemoApi"));

        await ExportAsync(cut, "png");
        await ExportAsync(cut, "pdf");

        own.Calls.Should().HaveCount(2);
        logs.Warnings.Should().ContainSingle().Which.Should().Contain("DemoApi").And.Contain("BaseAddress");
    }

    private sealed class WarningCollector
    {
        public List<string> Warnings { get; } = [];
    }

    private sealed class CollectingFactory(WarningCollector sink) : Microsoft.Extensions.Logging.ILoggerFactory
    {
        public void AddProvider(Microsoft.Extensions.Logging.ILoggerProvider provider) { }
        public Microsoft.Extensions.Logging.ILogger CreateLogger(string categoryName) => new CollectingLogger(sink);
        public void Dispose() { }
    }

    private sealed class CollectingLogger(WarningCollector sink) : Microsoft.Extensions.Logging.ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;
        public void Log<TState>(Microsoft.Extensions.Logging.LogLevel logLevel, Microsoft.Extensions.Logging.EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel == Microsoft.Extensions.Logging.LogLevel.Warning) sink.Warnings.Add(formatter(state, exception));
        }
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
