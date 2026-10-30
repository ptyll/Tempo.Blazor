using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Components.Diagram.Models;
using Tempo.Blazor.Components.Diagram.Stencils;

namespace Tempo.Blazor.Components.Diagram;

/// <summary>
/// Public small stencil preview: renders the registered stencil's compact SVG (the same
/// text-trimming pipeline the toolbox tiles use) for hosts that present stencils outside the
/// toolbox — the Modeling "Open in editor" notation palette and the preview legend embed it
/// instead of duplicating the icon markup. Always <c>aria-hidden</c>: the visible text label of
/// the surrounding row/tile owns the accessible name. A stencil id the registry does not know
/// renders the generic fallback shape.
/// </summary>
public partial class TmDiagramStencilIcon : TmComponentBase
{
    [Inject] private DiagramStencilRegistry StencilRegistry { get; set; } = default!;

    /// <summary>Stencil id to preview. Null or unknown ids render the fallback shape.</summary>
    [Parameter] public string? StencilId { get; set; }

    /// <summary>CSS size of the icon box (applied to width and height). Default "32px" — the toolbox tile size.</summary>
    [Parameter] public string? Size { get; set; }

    /// <summary>Additional CSS class.</summary>
    [Parameter] public string? Class { get; set; }

    /// <summary>Review fix (phase 15): the registry lookup is cached per parameter set — the
    /// legend and palette rows re-render frequently and <see cref="Stencil"/> was re-querying
    /// the registry on every property read.</summary>
    private DiagramStencil? _cachedStencil;

    /// <inheritdoc />
    protected override void OnParametersSet()
    {
        _cachedStencil = string.IsNullOrWhiteSpace(StencilId) ? null : StencilRegistry.GetStencil(StencilId);
    }

    private string RootClass => string.Join(" ", new[]
    {
        "tm-diagram-stencil-icon",
        Class
    }.Where(value => !string.IsNullOrWhiteSpace(value)));

    private string SizeStyle => $"width:{ResolvedSize};height:{ResolvedSize};";

    private string ResolvedSize
    {
        get
        {
            var size = Size?.Trim();
            return string.IsNullOrWhiteSpace(size) ? "32px" : size;
        }
    }

    private DiagramStencil? Stencil => _cachedStencil;

    /// <summary>Whether the icon renders the registered stencil's own SVG (false = the fallback shape).</summary>
    private bool HasRegisteredIcon
        => Stencil is not null && !string.IsNullOrWhiteSpace(Stencil.IconSvg);

    private string? FallbackAttribute => HasRegisteredIcon ? null : "true";

    private string Markup => HasRegisteredIcon
        ? DiagramStencilIconMarkup.ForPreview(Stencil!.IconSvg)
        : FallbackShapeMarkup(Stencil?.Kind ?? DiagramStencilKind.Node);

    private static string FallbackShapeMarkup(DiagramStencilKind kind) => kind == DiagramStencilKind.Edge
        ? "<path d=\"M4 16 H28\" fill=\"none\" stroke=\"var(--tm-color-text)\" stroke-width=\"2\" stroke-linecap=\"round\" />"
            + "<path d=\"M23 11 L28 16 L23 21\" fill=\"none\" stroke=\"var(--tm-color-text)\" stroke-width=\"2\" stroke-linecap=\"round\" stroke-linejoin=\"round\" />"
        : "<rect x=\"2\" y=\"8\" width=\"28\" height=\"16\" rx=\"2\" fill=\"var(--tm-color-surface-elevated)\" stroke=\"var(--tm-color-text)\" stroke-width=\"2\" />";
}
