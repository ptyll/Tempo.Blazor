using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Tempo.Blazor.Components.Wireframe.Models;

namespace Tempo.Blazor.Components.Wireframe;

/// <summary>Options controlling a static wireframe page → SVG render.</summary>
public sealed class WireframePageSvgOptions
{
    /// <summary>Shared default options (no grid, white background).</summary>
    public static WireframePageSvgOptions Default { get; } = new();

    /// <summary>When true, draws the canvas grid. Defaults to false (previews rarely want a grid).</summary>
    public bool ShowGrid { get; init; }

    /// <summary>When true, draws the page background rectangle. Defaults to true.</summary>
    public bool ShowBackground { get; init; } = true;

    /// <summary>Background fill color used when <c>ShowBackground</c> is true.</summary>
    public string BackgroundFill { get; init; } = "white";

    /// <summary>
    /// Stencil theme the page is drawn with (<c>indigo</c>, <c>indigo-dark</c>, ...; decision WIREFRAME-STENCIL-THEME). <c>null</c> falls back to the
    /// page's <c>TargetTheme</c> (and, in <c>IWireframeSvgRenderer.RenderDocumentAsync</c>, the document's), then to the default tokens. An unknown
    /// name draws the default tokens and logs one warning.
    /// </summary>
    public string? StencilTheme { get; init; }
}

/// <summary>
/// Produces a clean, interaction-free SVG <c>RenderFragment</c> for a single
/// <c>WireframePage</c>. This is the shared presentation layer reused by the live canvas
/// and by headless server-side rendering (<c>IWireframeSvgRenderer</c>), so the visual stays in
/// one place and previews cannot drift from the editor.
///
/// It renders ONLY presentation:
///   - the page <c>&lt;svg&gt;</c> root + connector arrow markers (and an optional grid) in <c>&lt;defs&gt;</c>,
///   - the page background,
///   - visible connector paths and their labels,
///   - each element as a translated inner-<c>&lt;svg&gt;</c> viewport hosting the component's
///     <c>WireframeComponentDef.RenderSvg</c> (with a dashed fallback for unknown types).
///
/// It deliberately omits every piece of interaction chrome: selection/resize handles, connector
/// hit-test paths, waypoint handles, <c>pointer-events</c> surfaces and cursor styling.
/// </summary>
public static class WireframePageSvg
{
    private const string GridPatternId = "tm-wf-grid";

    /// <summary>
    /// Builds an interaction-free SVG render fragment for <paramref name="page"/>.
    /// Component definitions are resolved from <paramref name="registry"/> in
    /// <paramref name="scope"/> (pass the app scope to resolve app-scoped custom components).
    /// </summary>
    public static RenderFragment BuildFragment(
        WireframePage page,
        WireframeComponentRegistry registry,
        WireframeComponentScope? scope = null,
        WireframePageSvgOptions? options = null)
        => BuildFragment(page, registry, scope, options, documentTheme: null, logger: null);

    /// <summary>The fragment with the document's <c>TargetTheme</c> as the last theme fallback and a logger for the unknown-theme warning.</summary>
    internal static RenderFragment BuildFragment(
        WireframePage page,
        WireframeComponentRegistry registry,
        WireframeComponentScope? scope,
        WireframePageSvgOptions? options,
        string? documentTheme,
        Microsoft.Extensions.Logging.ILogger? logger)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(registry);
        options ??= WireframePageSvgOptions.Default;
        var theme = Stencil.StencilThemeScope.Resolve(options.StencilTheme, page.TargetTheme, documentTheme, logger);

        return builder =>
        {
            var width  = page.Width  > 0 ? page.Width  : 1280;
            var height = page.Height > 0 ? page.Height : 800;

            builder.OpenElement(0, "svg");
            builder.AddAttribute(1, "xmlns", "http://www.w3.org/2000/svg");
            builder.AddAttribute(2, "viewBox", $"0 0 {F(width)} {F(height)}");
            builder.AddAttribute(3, "width", F(width));
            builder.AddAttribute(4, "height", F(height));
            builder.AddAttribute(5, "data-page-id", page.Id);
            builder.AddAttribute(6, "data-page-name", page.Name);

            // Resolve which connectors are actually drawable (both endpoints exist + a path)
            // up front, so we never emit orphan arrow-marker defs for connectors we skip.
            var drawableConnectors = ResolveDrawableConnectors(page);

            // ── defs: optional grid pattern + connector arrow markers ──
            builder.OpenElement(6, "defs");
            if (options.ShowGrid)
                builder.AddMarkupContent(7, BuildGridPattern());
            foreach (var dc in drawableConnectors)
            {
                var markers = WireframeConnectorRenderer.BuildArrowMarkers(dc.Connector);
                if (!string.IsNullOrEmpty(markers))
                    builder.AddMarkupContent(8, markers);
            }
            builder.CloseElement();

            // ── background (+ optional grid overlay) ──
            if (options.ShowBackground)
            {
                builder.OpenElement(9, "rect");
                builder.AddAttribute(10, "x", "0");
                builder.AddAttribute(11, "y", "0");
                builder.AddAttribute(12, "width", F(width));
                builder.AddAttribute(13, "height", F(height));
                builder.AddAttribute(14, "fill", options.BackgroundFill);
                builder.CloseElement();
            }
            if (options.ShowGrid)
            {
                builder.OpenElement(15, "rect");
                builder.AddAttribute(16, "width", F(width));
                builder.AddAttribute(17, "height", F(height));
                builder.AddAttribute(18, "fill", $"url(#{GridPatternId})");
                builder.CloseElement();
            }

            // ── empty page: show the page name as a centered placeholder (never a blank box) ──
            if (page.Elements.Count == 0)
            {
                builder.AddMarkupContent(39,
                    $"""<text x="{F(width / 2)}" y="{F(height / 2)}" text-anchor="middle" dominant-baseline="middle" font-size="14" font-family="ui-sans-serif,system-ui" fill="#9ca3af">{WireframeSvg.Escape(page.Name)}</text>""");
            }

            // ── connectors: visible path + label only (no hit-test, no waypoints) ──
            foreach (var dc in drawableConnectors)
            {
                var connector = dc.Connector;
                var startMarker = WireframeConnectorRenderer.GetArrowMarkerRef(connector.StartArrow, isStart: true, connector.Id);
                var endMarker   = WireframeConnectorRenderer.GetArrowMarkerRef(connector.EndArrow, isStart: false, connector.Id);

                builder.OpenElement(19, "path");
                builder.AddAttribute(20, "d", dc.PathD);
                builder.AddAttribute(21, "fill", "none");
                builder.AddAttribute(22, "stroke", connector.Stroke);
                builder.AddAttribute(23, "stroke-width", F(connector.StrokeWidth));
                if (!string.IsNullOrEmpty(connector.StrokeDasharray))
                    builder.AddAttribute(24, "stroke-dasharray", connector.StrokeDasharray);
                if (startMarker is not null)
                    builder.AddAttribute(25, "marker-start", startMarker);
                if (endMarker is not null)
                    builder.AddAttribute(26, "marker-end", endMarker);
                builder.CloseElement();

                if (!string.IsNullOrEmpty(connector.Label))
                {
                    var labelPos = WireframeConnectorRenderer.GetLabelPosition(connector, dc.Source, dc.Target);
                    builder.AddMarkupContent(27,
                        $"""<text x="{F(labelPos.X)}" y="{F(labelPos.Y)}" text-anchor="middle" dominant-baseline="middle" font-size="12" font-family="ui-sans-serif,system-ui" fill="#374151">{WireframeSvg.Escape(connector.Label)}</text>""");
                }
            }

            // ── elements: <g transform> + inner <svg> + def.RenderSvg / fallback ──
            foreach (var element in page.Elements.OrderBy(e => e.ZIndex))
            {
                var def = registry.GetDef(element.Type, scope);

                builder.OpenElement(28, "g");
                builder.AddAttribute(29, "data-el-id", element.Id);
                builder.AddAttribute(30, "data-type", element.Type);
                builder.AddAttribute(31, "transform", BuildElementTransform(element));

                builder.OpenElement(32, "svg");
                builder.AddAttribute(33, "width", F(element.W));
                builder.AddAttribute(34, "height", F(element.H));
                builder.AddAttribute(35, "viewBox", $"0 0 {F(element.W)} {F(element.H)}");
                builder.AddAttribute(36, "overflow", "visible");

                if (def is not null)
                    builder.AddContent(37, (RenderFragment)(b =>
                    {
                        using var _ = Stencil.StencilThemeScope.Use(theme);
                        def.RenderSvg(element, b);
                    }));
                else
                    builder.AddMarkupContent(38, BuildUnknownComponentFallback(element));

                builder.CloseElement(); // inner <svg>
                builder.CloseElement(); // <g>
            }

            builder.CloseElement(); // root <svg>
        };
    }

    private static List<DrawableConnector> ResolveDrawableConnectors(WireframePage page)
    {
        var result = new List<DrawableConnector>();
        foreach (var connector in page.Connectors.OrderBy(c => c.ZIndex))
        {
            var source = page.Elements.FirstOrDefault(e => e.Id == connector.FromId);
            var target = page.Elements.FirstOrDefault(e => e.Id == connector.ToId);
            if (source is null || target is null)
                continue;

            var pathD = WireframeConnectorRenderer.BuildPath(connector, source, target);
            if (string.IsNullOrEmpty(pathD))
                continue;

            result.Add(new DrawableConnector(connector, source, target, pathD));
        }

        return result;
    }

    private readonly record struct DrawableConnector(
        WireframeConnector Connector, WireframeElement Source, WireframeElement Target, string PathD);

    private static string BuildElementTransform(WireframeElement element)
    {
        var transform = $"translate({F(element.X)}, {F(element.Y)})";
        if (element.Rotation != 0)
            transform += $" rotate({F(element.Rotation)}, {F(element.W / 2)}, {F(element.H / 2)})";
        return transform;
    }

    private static string BuildUnknownComponentFallback(WireframeElement element)
        => $"<rect width='{F(element.W)}' height='{F(element.H)}' rx='4' fill='#f9fafb' stroke='#9ca3af' stroke-width='1' stroke-dasharray='6 3'></rect>"
         + $"<text x='4' y='14' font-size='10' fill='#6b7280' font-family='ui-sans-serif,system-ui'>{WireframeSvg.Escape(element.Type)}</text>";

    private static string BuildGridPattern()
        => $"""<pattern id="{GridPatternId}" width="20" height="20" patternUnits="userSpaceOnUse"><path d="M 20 0 L 0 0 0 20" fill="none" stroke="#e5e7eb" stroke-width="0.5"/></pattern>""";

    private static string F(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);
}
