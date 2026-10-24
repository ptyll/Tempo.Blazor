using Tempo.Blazor.Components.Wireframe;
using Tempo.Blazor.Demo.Api.Services;

namespace Tempo.Blazor.Demo.Api.Endpoints;

/// <summary>
/// Demo endpoints that exercise the headless server-side <see cref="IWireframeSvgRenderer"/>
/// (no browser, no JS interop) so E2E tests can verify server-rendered previews over real HTTP.
/// </summary>
public static class WireframePreviewEndpoints
{
    public static void MapWireframePreviewEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/wireframe/preview");

        // Per-page render metadata + SVG for a named sample scenario.
        // Scenarios: multipage (default), empty, unknown, connectors.
        // ?theme= names the stencil theme (indigo, indigo-dark, dark, ...); an unknown name draws the default tokens (decision WIREFRAME-STENCIL-THEME).
        group.MapGet("/render.json", async (string? scenario, string? theme, IWireframeSvgRenderer renderer) =>
        {
            var document = SampleWireframes.ForScenario(scenario);
            var pages = await renderer.RenderDocumentAsync(document, options: new WireframePageSvgOptions { StencilTheme = theme });
            return Results.Ok(pages);
        });

        // A single page of a sample scenario as image/svg+xml — browser-navigable for screenshots.
        group.MapGet("/render.svg", async (string? scenario, int page, string? theme, IWireframeSvgRenderer renderer) =>
        {
            var document = SampleWireframes.ForScenario(scenario);
            if (page < 0 || page >= document.Pages.Count)
                return Results.NotFound();

            var svg = await renderer.RenderPageAsync(document.Pages[page],
                options: new WireframePageSvgOptions { StencilTheme = theme ?? document.TargetTheme });
            return Results.Content(svg, "image/svg+xml");
        });
    }
}
