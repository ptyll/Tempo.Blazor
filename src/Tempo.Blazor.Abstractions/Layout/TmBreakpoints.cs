namespace Tempo.Blazor.Abstractions.Layout;

/// <summary>
/// The shared responsive widths, in CSS pixels. A custom property cannot appear in an
/// <c>@media</c> or <c>@container</c> condition, so these constants are the contract and the
/// stylesheets repeat them as literals. <see cref="TmLayoutMode"/> uses <see cref="Sm"/> and
/// <see cref="Lg"/> as its boundaries; <see cref="Md"/> and <see cref="Xl"/> exist for rules that
/// need a finer step.
/// </summary>
public static class TmBreakpoints
{
    /// <summary>640px. Below this a container is <see cref="TmLayoutMode.Mobile"/>.</summary>
    public const int Sm = 640;

    /// <summary>768px. A named step between mobile and tablet; not a layout-mode boundary.</summary>
    public const int Md = 768;

    /// <summary>1024px. At this width a container becomes <see cref="TmLayoutMode.Desktop"/>.</summary>
    public const int Lg = 1024;

    /// <summary>1280px. The wide desktop step.</summary>
    public const int Xl = 1280;

    /// <summary>Every width a new <c>@media</c> or <c>@container</c> condition may use, in ascending order.</summary>
    public static IReadOnlyList<int> All { get; } = new[] { Sm, Md, Lg, Xl };

    /// <summary>
    /// The layout a measured container width resolves to. A non-finite or negative width is rejected:
    /// guessing a mode from a broken measurement would silently render the wrong branch.
    /// </summary>
    /// <param name="widthPx">The container's inline size in CSS pixels.</param>
    public static TmLayoutMode Classify(double widthPx)
    {
        if (!double.IsFinite(widthPx) || widthPx < 0)
            throw new ArgumentOutOfRangeException(nameof(widthPx), widthPx, "A container width must be a finite, non-negative number of CSS pixels.");

        if (widthPx < Sm) return TmLayoutMode.Mobile;
        if (widthPx < Lg) return TmLayoutMode.Tablet;
        return TmLayoutMode.Desktop;
    }
}
