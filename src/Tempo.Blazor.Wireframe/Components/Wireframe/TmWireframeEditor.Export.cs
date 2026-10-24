using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Tempo.Blazor.Abstractions.Wireframe.Export;

namespace Tempo.Blazor.Components.Wireframe;

// Export of the editor (JSON / SVG directly, PNG / PDF through the export dialog and the server endpoint) and the browser download.
// Kept beside the editor to hold the main file under the source-file length ceiling.
public partial class TmWireframeEditor
{
    // ── Toolbar: Export ───────────────────────────────────────────────────────

    private async Task HandleExportSelect(string format)
    {
        switch (format)
        {
            case "json": await ExportJson(); break;
            case "svg": await ExportSvg(); break;
            case "png": ExportPng(); break;
            case "pdf": ExportPdf(); break;
        }
    }

    private async Task ExportJson()
    {
        if (_document is null) return;
        var json     = WireframeSerializer.Serialize(_document);
        var fileName = SanitizeFileName(_document.Title) + ".wireframe.json";
        await DownloadFile(fileName, "application/json", System.Text.Encoding.UTF8.GetBytes(json));
    }

    private async Task ExportSvg()
    {
        if (_canvas is null) return;
        var svg      = await _canvas.ExportSvg();
        var fileName = SanitizeFileName(_document?.Title ?? Loc["TmWireframe_ExportFilename"]) + ".svg";
        await DownloadFile(fileName, "image/svg+xml", System.Text.Encoding.UTF8.GetBytes(svg));
    }

    /// <summary>Returns the current canvas state as an SVG string for use by embedded callers.</summary>
    public async Task<string> ExportSvgAsync()
    {
        if (_canvas is null) return string.Empty;
        return await _canvas.ExportSvg();
    }

    private void ExportPng()
    {
        _exportDialogFormat = "png";
        _exportDialogOpen = true;
    }

    private void ExportPdf()
    {
        _exportDialogFormat = "pdf";
        _exportDialogOpen = true;
    }

    private void CloseExportDialog()
    {
        _exportDialogOpen = false;
        _exportDialogFormat = null;
    }

    private async Task OnExportDialogExport(WireframeExportDialogResult result)
    {
        _exportDialogOpen = false;
        if (_canvas is null) return;

        var svg = await _canvas.ExportSvg();
        if (string.IsNullOrWhiteSpace(svg)) return;

        var request = new WireframeExportRequest
        {
            Svg = svg,
            FileName = result.FileName,
            Options = result.Options,
            Theme = ResolveExportTheme(),
        };

        var endpoint = result.Format == "pdf"
            ? "api/wireframe/export/pdf"
            : "api/wireframe/export/png";

        try
        {
            var response = await ResolveExportClient().PostAsJsonAsync(endpoint, request);
            response.EnsureSuccessStatusCode();

            var bytes = await response.Content.ReadAsByteArrayAsync();
            var contentType = result.Format == "pdf" ? "application/pdf" : "image/png";
            var ext = result.Format == "pdf" ? ".pdf" : ".png";
            var fileName = SanitizeFileName(result.FileName) + ext;

            await DownloadFile(fileName, contentType, bytes);
        }
        catch (Exception ex)
        {
            _importError = string.Format(Loc["TmWireframe_ExportError"], ex.Message);
        }
    }

    private bool _exportClientFallbackLogged;

    /// <summary>
    /// The canonical stencil theme name the exported SVG was drawn with (the same host → page → document order the canvas uses), or <c>null</c>
    /// for the default tokens (also for <c>light</c> / <c>default</c>, a blank, a malformed or an unknown name). Informational for the server.
    /// </summary>
    private string? ResolveExportTheme()
        => Stencil.StencilThemeScope.Resolve(StencilTheme, _activePage?.TargetTheme, _document?.TargetTheme, Services.GetService<ILoggerFactory>()?.CreateLogger<TmWireframeEditor>());

    /// <summary>
    /// The client the server export posts to: the named factory client when the host asked for one, else the injected HttpClient. Unlike
    /// <c>TmDiagramEditor</c> (which throws for a missing factory or an unusable client) this falls back to the injected client, and says so with
    /// ONE warning per editor instance so a misconfigured <see cref="ExportHttpClientName"/> is visible instead of silently posting to the host origin.
    /// </summary>
    private HttpClient ResolveExportClient()
    {
        if (string.IsNullOrWhiteSpace(ExportHttpClientName)) return Http;
        var factory = Services.GetService(typeof(IHttpClientFactory)) as IHttpClientFactory;
        var named = factory?.CreateClient(ExportHttpClientName);
        // A factory hands out a default client for a name nobody configured; one without a base address cannot resolve the relative
        // export route, so the injected client (the host origin) is the better answer than an InvalidOperationException.
        if (named is { BaseAddress: not null }) return named;

        if (!_exportClientFallbackLogged)
        {
            _exportClientFallbackLogged = true;
            Services.GetService<ILoggerFactory>()?.CreateLogger<TmWireframeEditor>().LogWarning(
                "Wireframe export: ExportHttpClientName '{ClientName}' is set but {Reason}; the injected HttpClient is used for the export.",
                ExportHttpClientName, factory is null ? "no IHttpClientFactory is registered" : "that client has no BaseAddress");
        }
        return Http;
    }
}