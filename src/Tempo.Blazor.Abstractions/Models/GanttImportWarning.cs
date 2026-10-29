namespace Tempo.Blazor.Abstractions.Models;

/// <summary>
/// A non-fatal problem found while importing. A warning carries code + data only — never a
/// ready-made message: the language belongs to the UI that renders it.
/// </summary>
public sealed class GanttImportWarning
{
    /// <summary>
    /// The 1-based data-row ordinal the warning came from (1 = first data row). Predecessor row
    /// references count these ordinals; UI text uses <see cref="SheetRow"/> instead.
    /// </summary>
    public int Row { get; init; }

    /// <summary>
    /// The 1-based sheet row number the warning came from, ready for display (the header row is 1;
    /// taken from the row's <c>RowIndex</c>, falling back to the ordinal plus the header offset).
    /// </summary>
    public int SheetRow { get; init; }

    /// <summary>The source column header the warning came from, when the warning is tied to a column.</summary>
    public string? Column { get; init; }

    /// <summary>What went wrong; the UI maps this to a localized message.</summary>
    public GanttImportWarningCode Code { get; init; }

    /// <summary>The offending raw value, when there is one (the unknown predecessor token, the duplicated WBS code…).</summary>
    public string? Value { get; init; }
}
