using Microsoft.AspNetCore.Http.Features;
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
        // ?theme= names the stencil theme (indigo, indigo-dark, dark, ...); an unknown name draws the default tokens, a malformed one
        // (over 64 chars or outside [A-Za-z0-9._-]) is a 400 (decision WIREFRAME-STENCIL-THEME).
        group.MapGet("/render.json", async (string? scenario, string? theme, IWireframeSvgRenderer renderer) =>
        {
            if (WireframeThemeRequest.Validate(theme) is { } invalid) return invalid;
            var document = SampleWireframes.ForScenario(scenario);
            var pages = await renderer.RenderDocumentAsync(document, options: new WireframePageSvgOptions { StencilTheme = theme });
            return Results.Ok(pages);
        });

        // Renders an arbitrary WireframeDocument posted as JSON (the exact shape WireframeSerializer
        // produces) through the same headless IWireframeSvgRenderer, so E2E specs can compare the
        // server-side string render against the live browser DOM for ONE AND THE SAME document
        // (client/server export parity).
        group.MapPost("/render-document", async (HttpRequest request, IWireframeSvgRenderer renderer) =>
        {
            const long maxBodyBytes = 1_048_576; // 1 MB — documents are a few KB; this is a demo/test endpoint.
            if (request.ContentLength > maxBodyBytes)
                return Results.BadRequest(new { error = "The WireframeDocument JSON exceeds the 1 MB limit." });

            // A chunked body carries no Content-Length, so ask the server to enforce the same cap
            // while the upload is being read (no-op on servers where the feature is read-only).
            var sizeFeature = request.HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>();
            if (sizeFeature is { IsReadOnly: false })
                sizeFeature.MaxRequestBodySize = maxBodyBytes;

            string json;
            try
            {
                // TestServer does not enforce MaxRequestBodySize, so also read through a capped
                // buffer and reject the moment the body outgrows it.
                using var reader = new StreamReader(request.Body);
                var buffer = new char[maxBodyBytes + 1];
                var read = await reader.ReadBlockAsync(buffer, 0, buffer.Length);
                if (read > maxBodyBytes)
                    return Results.BadRequest(new { error = "The WireframeDocument JSON exceeds the 1 MB limit." });
                json = new string(buffer, 0, read);
            }
            catch (BadHttpRequestException ex) when (ex.StatusCode == StatusCodes.Status413PayloadTooLarge)
            {
                return Results.BadRequest(new { error = "The WireframeDocument JSON exceeds the 1 MB limit." });
            }

            if (string.IsNullOrWhiteSpace(json) || !WireframeSerializer.TryDeserialize(json, out var document) || document is null)
                return Results.BadRequest(new { error = "The body must be a WireframeDocument JSON produced by WireframeSerializer." });

            var pages = await renderer.RenderDocumentAsync(document, options: new WireframePageSvgOptions());
            return Results.Ok(pages);
        });

        // A single page of a sample scenario as image/svg+xml — browser-navigable for screenshots.
        group.MapGet("/render.svg", async (string? scenario, int page, string? theme, IWireframeSvgRenderer renderer) =>
        {
            if (WireframeThemeRequest.Validate(theme) is { } invalid) return invalid;
            var document = SampleWireframes.ForScenario(scenario);
            if (page < 0 || page >= document.Pages.Count)
                return Results.NotFound();

            // The theme resolves host -> page -> document exactly like RenderDocumentAsync: the ?theme= value goes in alone and the document
            // TargetTheme stays the last fallback (it must not outrank the page TargetTheme).
            var pages = await renderer.RenderDocumentAsync(document, options: new WireframePageSvgOptions { StencilTheme = theme });
            return Results.Content(pages[page].Svg, "image/svg+xml");
        });
    }
}
