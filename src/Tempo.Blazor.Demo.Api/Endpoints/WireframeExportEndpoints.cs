using Tempo.Blazor.Abstractions.Wireframe.Export;
using Tempo.Blazor.Demo.Api.Services;

namespace Tempo.Blazor.Demo.Api.Endpoints;

public static class WireframeExportEndpoints
{
    public static void MapWireframeExportEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/wireframe/export");

        group.MapPost("/png", async (
            WireframeExportRequest request,
            WireframeExportService service,
            ILoggerFactory loggers,
            CancellationToken cancellationToken) =>
        {
            if (WireframeThemeRequest.Validate(request.Theme) is { } invalid) return invalid;
            LogTheme(loggers, request);
            var png = await service.ExportPngAsync(request, cancellationToken);
            var fileName = $"{request.FileName}.png";
            return Results.File(png, "image/png", fileName);
        });

        group.MapPost("/pdf", async (
            WireframeExportRequest request,
            WireframeExportService service,
            ILoggerFactory loggers,
            CancellationToken cancellationToken) =>
        {
            if (WireframeThemeRequest.Validate(request.Theme) is { } invalid) return invalid;
            LogTheme(loggers, request);
            var pdf = await service.ExportPdfAsync(request, cancellationToken);
            var fileName = $"{request.FileName}.pdf";
            return Results.File(pdf, "application/pdf", fileName);
        });
    }

    // The browser posts the SVG it has already drawn with the theme, so the server rasterises it as it is. The name is validated (400 when malformed) and logged so a themed export that looks wrong can be traced to its theme.
    private static void LogTheme(ILoggerFactory loggers, WireframeExportRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Theme)) return;
        // Only a validated name reaches the log (WireframeThemeRequest.Validate ran first), never raw input.
        loggers.CreateLogger("Tempo.Blazor.Demo.Api.WireframeExport").LogInformation("Wireframe export drawn with stencil theme '{Theme}'.", request.Theme.Trim());
    }
}