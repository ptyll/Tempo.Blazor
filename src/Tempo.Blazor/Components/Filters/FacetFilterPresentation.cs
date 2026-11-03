namespace Tempo.Blazor.Components.Filters;

/// <summary>
/// How <c>TmFacetFilter</c> presents its groups.
/// </summary>
public enum FacetFilterPresentation
{
    /// <summary>
    /// A trigger button that opens the groups in an overlay: an anchored dialog popover on a wide
    /// layout, a bottom sheet on a mobile one. The default — the groups cost no page space until the
    /// user asks for them.
    /// </summary>
    Popover = 0,

    /// <summary>
    /// The groups are rendered in the page flow, always visible, with no trigger and no overlay. Use
    /// it for a filter sidebar where the groups ARE the content — the mobile sheet does not apply,
    /// because there is nothing to open.
    /// </summary>
    Inline = 1
}
