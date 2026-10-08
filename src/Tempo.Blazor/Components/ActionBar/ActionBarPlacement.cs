namespace Tempo.Blazor.Components.ActionBar;

/// <summary>Where a <see cref="TmMobileActionBar"/> anchors.</summary>
public enum ActionBarPlacement
{
    /// <summary>
    /// The default: the bar is the last in-flow element of its container and sticks to the bottom
    /// of the viewport (<c>position: sticky</c>) while the container is scrolled through. Never
    /// <c>position: fixed</c> — the bar belongs to its component, not to the page.
    /// </summary>
    StickyContainer,

    /// <summary>
    /// A deliberate opt-in: the bar anchors to the viewport (<c>position: fixed</c>, both inline
    /// edges pinned) and respects the bottom safe area. A host transform/filter/contain on an
    /// ancestor re-anchors it per CSS — render it outside such ancestors.
    /// </summary>
    FixedViewport,

    /// <summary>The bar is static: it sits at the end of the content with no sticking or fixing.</summary>
    Inline,
}
