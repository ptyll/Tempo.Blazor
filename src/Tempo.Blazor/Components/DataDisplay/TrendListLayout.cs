namespace Tempo.Blazor.Components.DataDisplay;

/// <summary>
/// How <see cref="TmTrendList"/> presents its rows: the default compact semantic list, or a table
/// with real column headers (which is what a wide host layout wants — the rows then line up with
/// the metrics' numbers).
/// </summary>
public enum TrendListLayout
{
    /// <summary>One semantic <c>&lt;ul&gt;</c> row per metric. The default.</summary>
    List = 0,

    /// <summary>A <c>&lt;table&gt;</c> with a localized header row and a row header per metric.</summary>
    Table = 1
}
