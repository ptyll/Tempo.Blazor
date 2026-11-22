namespace Tempo.Blazor.Models;

/// <summary>
/// Fine layout of <c>TmTimeSelector</c>. <see cref="Auto"/> is measured from the container
/// (inline-size and height); <see cref="Square"/> and <see cref="Regular"/> pin it for tests and
/// hosts such as a Flex-Window app.
/// </summary>
public enum TimeSelectorFit
{
    /// <summary>Measure width and height; square when both are below the local threshold. The default.</summary>
    Auto = 0,

    /// <summary>Force the compact square layout (Z Flip 5 cover screen, 399 × 306).</summary>
    Square = 1,

    /// <summary>Force the regular (phone / tablet / desktop) layout.</summary>
    Regular = 2,
}
