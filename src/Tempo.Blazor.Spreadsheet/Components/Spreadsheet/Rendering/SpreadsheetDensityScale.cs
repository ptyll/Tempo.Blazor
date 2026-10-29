using Tempo.Blazor.Components.Spreadsheet.Enums;

namespace Tempo.Blazor.Components.Spreadsheet.Rendering;

/// <summary>
/// The SP1 density contract: the requested <see cref="SpreadsheetDensity"/> resolved to the render
/// scales the grid draws with. <see cref="SpreadsheetDensity.Auto"/> means comfortable on a coarse
/// pointer (touch) and compact everywhere else.
/// </summary>
/// <remarks>
/// These are RENDER scales. Row heights are scaled in <see cref="SpreadsheetGridGeometry"/> and cell
/// fonts in the canvas (which mirrors the same numbers in <c>spreadsheet-canvas.js</c>; both lanes pin
/// them so the two cannot drift apart silently). The workbook model — <c>DefaultRowHeight</c>, a
/// per-row custom height and every XLSX height — is never touched.
/// </remarks>
internal readonly record struct SpreadsheetDensityScale(
    SpreadsheetDensity Resolved,
    double RowScale,
    double FontScale,
    double HandleScale,
    double GutterScale)
{
    /// <summary>Row-height multiplier of the comfortable density (20px rows draw at 30px).</summary>
    public const double ComfortableRowScale = 1.5;

    /// <summary>Cell-font multiplier of the comfortable density (10pt text draws at 12.5pt).</summary>
    public const double ComfortableFontScale = 1.25;

    /// <summary>Fill-handle / filter-button multiplier of the comfortable density.</summary>
    public const double ComfortableHandleScale = 1.4;

    /// <summary>
    /// Header-gutter multiplier of the comfortable density (the row-header width and the
    /// column-header height). The tablet reference draws visibly taller/wider gutters: 40×1.4 = 56px
    /// and 20×1.4 = 28px, which is also what gives the auto-filter button its finger-sized height.
    /// </summary>
    public const double ComfortableGutterScale = 1.4;

    /// <summary>Resolves the requested density against the browser's pointer kind.</summary>
    public static SpreadsheetDensityScale Resolve(SpreadsheetDensity density, bool coarsePointer)
    {
        var resolved = density switch
        {
            SpreadsheetDensity.Comfortable => SpreadsheetDensity.Comfortable,
            SpreadsheetDensity.Compact => SpreadsheetDensity.Compact,
            _ => coarsePointer ? SpreadsheetDensity.Comfortable : SpreadsheetDensity.Compact,
        };

        return resolved == SpreadsheetDensity.Comfortable
            ? new SpreadsheetDensityScale(
                resolved, ComfortableRowScale, ComfortableFontScale, ComfortableHandleScale, ComfortableGutterScale)
            : new SpreadsheetDensityScale(resolved, 1, 1, 1, 1);
    }

    /// <summary>The resolved density as the canvas reads it (lowercase, one of compact/comfortable).</summary>
    public string Name => Resolved == SpreadsheetDensity.Comfortable ? "comfortable" : "compact";
}
