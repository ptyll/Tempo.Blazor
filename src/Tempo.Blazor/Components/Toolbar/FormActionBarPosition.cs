namespace Tempo.Blazor.Components.Toolbar;

/// <summary>Placement mode for <see cref="TmFormActionBar"/>.</summary>
public enum FormActionBarPosition
{
    /// <summary>Render in normal document flow.</summary>
    Static,

    /// <summary>Stick to the top of the scroll container.</summary>
    StickyTop,

    /// <summary>Fix to the bottom of the viewport.</summary>
    FloatingBottom,

    /// <summary>
    /// In normal document flow below the <c>md</c> boundary (768px — the same edge the
    /// column wrap and <c>--tm-form-action-bar-reserve-block-size</c> already use); fixed to
    /// the bottom of the viewport from <c>md</c> up. The media query lives in the component's
    /// scoped stylesheet, so a host cannot have to out-specify the <c>[b-*]</c> scope attribute
    /// to express "static below, floating above", and the matching rule in
    /// <c>tokens.css</c> collapses the reserve to zero below the same boundary — position and
    /// reserve share one library-owned number, so the two cannot drift apart.
    /// </summary>
    FloatingBottomFromMd
}
