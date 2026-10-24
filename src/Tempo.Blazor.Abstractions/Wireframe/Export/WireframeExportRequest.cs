namespace Tempo.Blazor.Abstractions.Wireframe.Export;

/// <summary>
/// Request body sent from the wireframe editor to the export API.
/// </summary>
public sealed class WireframeExportRequest
{
    /// <summary>SVG markup string (already cleaned of UI overlays).</summary>
    public string Svg { get; set; } = "";

    /// <summary>Desired output file name without extension.</summary>
    public string FileName { get; set; } = "wireframe";

    /// <summary>Export options (scale, background, etc.).</summary>
    public WireframeExportOptions Options { get; set; } = new();

    /// <summary>
    /// The stencil theme the exported <see cref="Svg"/> was drawn with (<c>indigo</c>, <c>indigo-dark</c>, ...), or <c>null</c> for the default tokens.
    /// The browser has already drawn the SVG it posts with that theme, so the server rasterises it as it is and never re-themes it; the name
    /// travels with the request so a server can log it and keep the exported file in step with the canvas (decision WIREFRAME-STENCIL-THEME).
    /// A server that renders a document itself passes the same name to <c>WireframePageSvgOptions.StencilTheme</c>.
    /// </summary>
    public string? Theme { get; set; }
}
