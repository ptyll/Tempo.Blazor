using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Tempo.Blazor.Components.Wireframe.Models;

namespace Tempo.Blazor.Components.Wireframe;

/// <summary>
/// Default <c>IWireframeSvgRenderer</c> built on the static <c>HtmlRenderer</c>.
/// Each render hosts <c>WireframePageSvg.BuildFragment</c> in a tiny throwaway component and
/// serializes the result to a string. No browser, no JS interop — safe to call on the server.
/// </summary>
public sealed class WireframeSvgRenderer : IWireframeSvgRenderer
{
    private readonly WireframeComponentRegistry _registry;
    private readonly IServiceProvider _services;
    private readonly ILoggerFactory _loggerFactory;

    /// <summary>Creates a renderer using the supplied component registry and service provider.</summary>
    public WireframeSvgRenderer(WireframeComponentRegistry registry, IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(services);
        _registry = registry;
        _services = services;
        _loggerFactory = services.GetService<ILoggerFactory>() ?? NullLoggerFactory.Instance;
    }

    /// <inheritdoc/>
    public async Task<string> RenderPageAsync(
        WireframePage page,
        WireframeComponentScope? scope = null,
        WireframePageSvgOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(page);

        await using var htmlRenderer = new HtmlRenderer(_services, _loggerFactory);
        return await RenderFragmentAsync(htmlRenderer,
            WireframePageSvg.BuildFragment(page, _registry, scope, options, documentTheme: null, _loggerFactory.CreateLogger<WireframeSvgRenderer>()));
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<WireframePageRender>> RenderDocumentAsync(
        WireframeDocument document,
        WireframeComponentScope? scope = null,
        WireframePageSvgOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(document);

        await using var htmlRenderer = new HtmlRenderer(_services, _loggerFactory);

        var results = new List<WireframePageRender>(document.Pages.Count);
        foreach (var page in document.Pages)
        {
            var svg = await RenderFragmentAsync(htmlRenderer,
                WireframePageSvg.BuildFragment(page, _registry, scope, options, document.TargetTheme, _loggerFactory.CreateLogger<WireframeSvgRenderer>()));
            results.Add(new WireframePageRender(page.Id, page.Name, page.Width, page.Height, svg));
        }

        return results;
    }

    private static Task<string> RenderFragmentAsync(HtmlRenderer htmlRenderer, RenderFragment fragment)
        => htmlRenderer.Dispatcher.InvokeAsync(async () =>
        {
            var parameters = ParameterView.FromDictionary(
                new Dictionary<string, object?> { ["Content"] = fragment });
            var output = await htmlRenderer.RenderComponentAsync<FragmentHost>(parameters);
            return output.ToHtmlString();
        });

    /// <summary>Minimal host component whose only job is to render the supplied fragment.</summary>
    private sealed class FragmentHost : ComponentBase
    {
        [Parameter] public RenderFragment? Content { get; set; }

        protected override void BuildRenderTree(RenderTreeBuilder builder)
            => Content?.Invoke(builder);
    }
}
