using System.Collections.ObjectModel;
using Tempo.Blazor.Components.Diagram.Models;

namespace Tempo.Blazor.Components.Diagram.Services;

/// <summary>
/// Renders a <see cref="DiagramDocument"/> to a static SVG string on the server, without a
/// browser or JavaScript. Intended for headless export and documentation embedding (the same
/// output the demo export service rasterises to PNG/PDF).
/// </summary>
/// <remarks>
/// The render is deterministic: identical documents and options produce byte-for-byte identical
/// SVG (invariant-culture coordinate formatting, nodes ordered by z-index). Registered as a
/// singleton by <c>AddTempoBlazorDiagramEditor()</c>.
/// </remarks>
public interface IDiagramSvgRenderer
{
    /// <summary>Renders a single diagram page (selected via <see cref="DiagramSvgRenderOptions.PageIndex"/>,
    /// defaulting to the active page) to an SVG string.</summary>
    string RenderSvg(DiagramDocument document, DiagramSvgRenderOptions? options = null);
}

/// <summary>Colour theme for <see cref="IDiagramSvgRenderer"/> output.</summary>
public enum DiagramSvgTheme
{
    /// <summary>Light background with dark strokes/text (default).</summary>
    Light,

    /// <summary>Dark background with light strokes/text.</summary>
    Dark
}

/// <summary>Options controlling headless diagram SVG rendering. All fields are additive and
/// optional; the defaults reproduce the light-theme auto-fit export.</summary>
public sealed class DiagramSvgRenderOptions
{
    /// <summary>Colour theme. Only the <em>default</em> node/edge/text colours switch by theme;
    /// explicit per-node/edge colours (stencil layout or <see cref="DiagramStyle"/>) always win.</summary>
    public DiagramSvgTheme Theme { get; init; } = DiagramSvgTheme.Light;

    /// <summary>Zero-based page index to render. When null, the document's active page is used.</summary>
    public int? PageIndex { get; init; }

    /// <summary>Explicit output width. When both width and height are null the renderer auto-fits
    /// to the node bounds with <see cref="Padding"/>.</summary>
    public double? Width { get; init; }

    /// <summary>Explicit output height. See <see cref="Width"/>.</summary>
    public double? Height { get; init; }

    /// <summary>Padding around auto-fitted content in pixels.</summary>
    public double Padding { get; init; } = 20;

    /// <summary>When true, a dotted grid pattern is drawn behind the diagram.</summary>
    public bool IncludeGrid { get; init; }

    /// <summary>Overrides the theme background colour (any CSS colour). Null uses the theme default.</summary>
    public string? BackgroundColor { get; init; }

    /// <summary>Maps the render options onto the shared <see cref="DiagramExportOptions"/> the builder consumes.</summary>
    public DiagramExportOptions ToExportOptions() => new()
    {
        Width = Width,
        Height = Height,
        Padding = Padding,
        IncludeGrid = IncludeGrid,
        BackgroundColor = BackgroundColor,
        PageIndex = PageIndex
    };
}

/// <summary>Resolved colour palette applied to a diagram SVG render. Only fills in the
/// <em>defaults</em>; explicit stencil/style colours override these per element.</summary>
public sealed class DiagramSvgPalette
{
    /// <summary>Page background colour (used when no explicit background override is supplied).</summary>
    public string Background { get; init; } = "#ffffff";

    /// <summary>Grid dot colour.</summary>
    public string Grid { get; init; } = "#e5e7eb";

    /// <summary>Fill for solid arrow markers (association, composition).</summary>
    public string MarkerSolid { get; init; } = "#111827";

    /// <summary>Fill for hollow arrow markers (inheritance, aggregation).</summary>
    public string MarkerHollowFill { get; init; } = "white";

    /// <summary>Stroke for hollow arrow markers.</summary>
    public string MarkerHollowStroke { get; init; } = "#111827";

    /// <summary>Default node fill when neither stencil layout nor node style specifies one.</summary>
    public string DefaultNodeFill { get; init; } = "#ffffff";

    /// <summary>Default node stroke.</summary>
    public string DefaultNodeStroke { get; init; } = "#111827";

    /// <summary>Section divider line colour.</summary>
    public string Divider { get; init; } = "#e5e7eb";

    /// <summary>Default text colour.</summary>
    public string DefaultText { get; init; } = "#111827";

    /// <summary>
    /// Token name (including the leading <c>--</c>) to the concrete colour this theme bakes into a
    /// standalone SVG. Used by <c>DiagramSvgBuilder</c> to resolve <c>var(--token)</c> paints the
    /// shipped ArchiMate / BuiltIn / Extended stencils emit. A missing key falls back to
    /// <see cref="Light"/>.Tokens (the 2.12.0 hex), then the paint-slot default. Additive: existing
    /// palette fields keep their 2.12.0 values. Defaults to an empty read-only dictionary so a host
    /// constructing <c>new DiagramSvgPalette()</c> cannot mutate the fallback.
    /// </summary>
    /// <remarks>
    /// Light maps <c>--tm-diagram-node-ink-on-fill</c> to <see cref="DefaultText"/> (<c>#111827</c>),
    /// not the canvas CSS value <c>#4b5563</c> — the Light export snapshot stays pixel-identical
    /// (DIAGRAM-STENCIL-THEMED-FILLS R3). Light maps <c>--tm-diagram-stencil-stroke-on-fill</c> to
    /// <c>#1f2937</c>, not <see cref="DefaultNodeStroke"/> (R2). Host <c>node.Style.Fill</c> /
    /// <c>node.Style.Color</c> / heat overlay still win over the stencil.
    /// </remarks>
    public IReadOnlyDictionary<string, string> Tokens { get; init; }
        = new Dictionary<string, string>(StringComparer.Ordinal).AsReadOnly();

    /// <summary>
    /// Catalogued stencil token names in declaration order. Light and Dark
    /// <see cref="Tokens"/> dictionaries use exactly this set so a missing dark
    /// (or island) re-declaration cannot silently drop a fill.
    /// </summary>
    public static readonly string[] StencilTokenNames =
    [
        "--tm-diagram-node-ink-on-fill",
        "--tm-diagram-stencil-stroke-on-fill",
        "--tm-archimate-business-fill",
        "--tm-archimate-application-fill",
        "--tm-archimate-technology-fill",
        "--tm-archimate-motivation-fill",
        "--tm-archimate-strategy-fill",
        "--tm-archimate-physical-fill",
        "--tm-archimate-implementation-fill",
        "--tm-archimate-cross-fill",
        "--tm-diagram-stencil-yellow-fill",
        "--tm-diagram-stencil-yellow-stroke",
        "--tm-diagram-stencil-red-fill",
        "--tm-diagram-stencil-red-stroke",
        "--tm-diagram-stencil-red-ink",
        "--tm-diagram-stencil-blue-fill",
        "--tm-diagram-stencil-blue-stroke",
        "--tm-diagram-stencil-blue-ink",
        "--tm-diagram-stencil-orange-fill",
        "--tm-diagram-stencil-orange-stroke",
        "--tm-diagram-stencil-orange-ink",
        "--tm-diagram-stencil-green-fill",
        "--tm-diagram-stencil-green-stroke",
        "--tm-diagram-stencil-green-ink",
        "--tm-diagram-stencil-amber-fill",
        "--tm-diagram-stencil-amber-stroke",
        "--tm-diagram-stencil-amber-ink",
        "--tm-diagram-stencil-slate-fill",
        "--tm-diagram-stencil-slate-muted-fill",
        "--tm-diagram-stencil-cyan-fill",
        "--tm-diagram-stencil-amber-100-fill",
        "--tm-diagram-stencil-amber-200-fill",
        "--tm-diagram-stencil-lime-fill",
        "--tm-diagram-stencil-lime-strong-fill",
        "--tm-diagram-stencil-sky-fill",
        "--tm-diagram-stencil-blue-100-fill",
        "--tm-diagram-stencil-green-100-fill",
        "--tm-diagram-stencil-yellow-100-fill",
        "--tm-diagram-stencil-teal-fill",
        "--tm-diagram-stencil-indigo-fill",
        "--tm-diagram-stencil-indigo-50-fill",
        "--tm-diagram-stencil-pink-fill",
        "--tm-diagram-stencil-table-header-fill"
    ];

    /// <summary>Light theme palette (default). Values reproduce the original demo export output.</summary>
    public static DiagramSvgPalette Light { get; } = new()
    {
        Tokens = StencilTokens(
        onFillInk: "#111827",
        strokeOnFill: "#1f2937",
        business: "#fff2b8",
        application: "#cfe8ff",
        technology: "#d9f2d0",
        motivation: "#eadcff",
        strategy: "#ffe1c2",
        physical: "#d9f2d0",
        implementation: "#ffd6e7",
        cross: "#eef2f7",
        yellowFill: "#fef08a",
        yellowStroke: "#ca8a04",
        redFill: "#fef2f2",
        redStroke: "#dc2626",
        redInk: "#991b1b",
        blueFill: "#eff6ff",
        blueStroke: "#2563eb",
        blueInk: "#1e40af",
        orangeFill: "#fff7ed",
        orangeStroke: "#ea580c",
        orangeInk: "#9a3412",
        greenFill: "#f0fdf4",
        greenStroke: "#16a34a",
        greenInk: "#166534",
        amberFill: "#fefce8",
        amberStroke: "#ca8a04",
        amberInk: "#854d0e",
        slateFill: "#f8fafc",
        slateMutedFill: "#f1f5f9",
        cyanFill: "#ecfeff",
        amber100Fill: "#fef3c7",
        amber200Fill: "#fde68a",
        limeFill: "#ecfccb",
        limeStrongFill: "#d9f99d",
        skyFill: "#e0f2fe",
        blue100Fill: "#dbeafe",
        green100Fill: "#dcfce7",
        yellow100Fill: "#fef9c3",
        tealFill: "#ccfbf1",
        indigoFill: "#e0e7ff",
        indigo50Fill: "#eef2ff",
        pinkFill: "#fce7f3",
        tableHeaderFill: "#f3f4f6")
    };

    /// <summary>Dark theme palette.</summary>
    public static DiagramSvgPalette Dark { get; } = new()
    {
        Background = "#1e1e2e",
        Grid = "#3f3f46",
        MarkerSolid = "#e5e7eb",
        MarkerHollowFill = "#1e1e2e",
        MarkerHollowStroke = "#e5e7eb",
        DefaultNodeFill = "#27272a",
        DefaultNodeStroke = "#e5e7eb",
        Divider = "#3f3f46",
        DefaultText = "#e5e7eb",
        Tokens = StencilTokens(
        onFillInk: "#e5e7eb",
        strokeOnFill: "#e5e7eb",
        business: "#78350f",
        application: "#1e3a8a",
        technology: "#14532d",
        motivation: "#4c1d95",
        strategy: "#9a3412",
        physical: "#14532d",
        implementation: "#9d174d",
        cross: "#3f3f46",
        yellowFill: "#713f12",
        yellowStroke: "#facc15",
        redFill: "#7f1d1d",
        redStroke: "#fca5a5",
        redInk: "#fecaca",
        blueFill: "#1e3a8a",
        blueStroke: "#93c5fd",
        blueInk: "#bfdbfe",
        orangeFill: "#9a3412",
        orangeStroke: "#fdba74",
        orangeInk: "#fed7aa",
        greenFill: "#14532d",
        greenStroke: "#86efac",
        greenInk: "#bbf7d0",
        amberFill: "#78350f",
        amberStroke: "#facc15",
        amberInk: "#fde68a",
        slateFill: "#334155",
        slateMutedFill: "#334155",
        cyanFill: "#164e63",
        amber100Fill: "#78350f",
        amber200Fill: "#713f12",
        limeFill: "#3f6212",
        limeStrongFill: "#3f6212",
        skyFill: "#0c4a6e",
        blue100Fill: "#1e40af",
        green100Fill: "#14532d",
        yellow100Fill: "#713f12",
        tealFill: "#115e59",
        indigoFill: "#312e81",
        indigo50Fill: "#312e81",
        pinkFill: "#9d174d",
        tableHeaderFill: "#334155")
    };

    private static IReadOnlyDictionary<string, string> StencilTokens(
        string onFillInk,
        string strokeOnFill,
        string business,
        string application,
        string technology,
        string motivation,
        string strategy,
        string physical,
        string implementation,
        string cross,
        string yellowFill,
        string yellowStroke,
        string redFill,
        string redStroke,
        string redInk,
        string blueFill,
        string blueStroke,
        string blueInk,
        string orangeFill,
        string orangeStroke,
        string orangeInk,
        string greenFill,
        string greenStroke,
        string greenInk,
        string amberFill,
        string amberStroke,
        string amberInk,
        string slateFill,
        string slateMutedFill,
        string cyanFill,
        string amber100Fill,
        string amber200Fill,
        string limeFill,
        string limeStrongFill,
        string skyFill,
        string blue100Fill,
        string green100Fill,
        string yellow100Fill,
        string tealFill,
        string indigoFill,
        string indigo50Fill,
        string pinkFill,
        string tableHeaderFill)
    {
        string[] values =
        [
            onFillInk, strokeOnFill,
            business, application, technology, motivation, strategy, physical, implementation, cross,
            yellowFill, yellowStroke,
            redFill, redStroke, redInk,
            blueFill, blueStroke, blueInk,
            orangeFill, orangeStroke, orangeInk,
            greenFill, greenStroke, greenInk,
            amberFill, amberStroke, amberInk,
            slateFill, slateMutedFill, cyanFill,
            amber100Fill, amber200Fill, limeFill, limeStrongFill, skyFill,
            blue100Fill, green100Fill, yellow100Fill, tealFill,
            indigoFill, indigo50Fill, pinkFill,
            tableHeaderFill
        ];

        var dict = new Dictionary<string, string>(StencilTokenNames.Length, StringComparer.Ordinal);
        for (var i = 0; i < StencilTokenNames.Length; i++)
        {
            dict[StencilTokenNames[i]] = values[i];
        }

        return dict.AsReadOnly();
    }

    /// <summary>Returns the palette for a theme.</summary>
    public static DiagramSvgPalette ForTheme(DiagramSvgTheme theme)
        => theme == DiagramSvgTheme.Dark ? Dark : Light;
}
