namespace Tempo.Blazor.Components.Spreadsheet.Enums;

/// <summary>
/// Grid density of a spreadsheet: how large the canvas draws rows and text.
/// </summary>
/// <remarks>
/// Density is a RENDER scale only. It never touches the workbook model — the default row height, a
/// per-row custom height and every XLSX import/export keep their exact values, so a comfortable grid
/// on a tablet still round-trips byte-identical dimensions.
/// </remarks>
public enum SpreadsheetDensity
{
    /// <summary>
    /// Compact on a fine pointer, comfortable on a coarse one (<c>pointer: coarse</c>). The default:
    /// a finger gets taller rows without any host code.
    /// </summary>
    Auto = 0,

    /// <summary>The classic Excel-like density (20 px rows, 10 pt cell text).</summary>
    Compact = 1,

    /// <summary>Taller rows and larger cell text for touch input (1.5x rows, 1.25x text).</summary>
    Comfortable = 2,
}
