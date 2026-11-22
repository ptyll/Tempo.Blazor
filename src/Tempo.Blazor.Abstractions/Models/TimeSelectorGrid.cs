namespace Tempo.Blazor.Models;

/// <summary>Whether <c>TmTimeSelector</c> shows the hour/minute grid under the readout.</summary>
public enum TimeSelectorGrid
{
    /// <summary>Show the grid when the layout has room; hide it in a tight square fit. The default.</summary>
    Auto = 0,

    /// <summary>Never show the grid — stepper and presets only.</summary>
    None = 1,

    /// <summary>
    /// Always show the grid, except the square + seconds combination which still collapses it
    /// (it would overflow the 260 px Flex-Window sheet).
    /// </summary>
    Always = 2,
}
