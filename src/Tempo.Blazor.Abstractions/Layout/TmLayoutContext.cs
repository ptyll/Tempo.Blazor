namespace Tempo.Blazor.Abstractions.Layout;

/// <summary>
/// The layout a component has resolved, cascaded to its children so they can branch in markup
/// without measuring anything themselves.
/// </summary>
/// <param name="Mode">The mode the host asked for. <see cref="TmLayoutMode.Auto"/> while the container is being measured.</param>
/// <param name="Resolved">The mode actually rendered. Never <see cref="TmLayoutMode.Auto"/>.</param>
public sealed record TmLayoutContext(TmLayoutMode Mode, TmLayoutMode Resolved)
{
    /// <summary>A resolved layout is desktop, tablet or mobile. Auto means "not resolved yet".</summary>
    /// <exception cref="ArgumentException">Thrown when <paramref name="Resolved"/> is <see cref="TmLayoutMode.Auto"/>.</exception>
    public TmLayoutMode Resolved { get; init; } = Resolved == TmLayoutMode.Auto
        ? throw new ArgumentException("A resolved layout is desktop, tablet or mobile.", nameof(Resolved))
        : Resolved;

    /// <summary>The BEM modifier for <see cref="Resolved"/>: <c>desktop</c>, <c>tablet</c> or <c>mobile</c>.</summary>
    public string CssModifier => Resolved switch
    {
        TmLayoutMode.Mobile => "mobile",
        TmLayoutMode.Tablet => "tablet",
        TmLayoutMode.Desktop => "desktop",
        _ => throw new ArgumentOutOfRangeException(nameof(Resolved), Resolved, "A resolved layout is desktop, tablet or mobile."),
    };

    /// <summary>True when the rendered layout is the narrow one.</summary>
    public bool IsMobile => Resolved == TmLayoutMode.Mobile;

    /// <summary>True when the rendered layout is the compact one.</summary>
    public bool IsTablet => Resolved == TmLayoutMode.Tablet;

    /// <summary>True when the rendered layout is the wide one.</summary>
    public bool IsDesktop => Resolved == TmLayoutMode.Desktop;
}
