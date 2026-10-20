namespace Tempo.Blazor.Components.Diagram.Services;

/// <summary>Machine-readable reason a CSV/SQL import failed; UIs map it to a localized message.
/// Explicit numeric values: the enum ships in the same (unreleased) version that introduced it,
/// so the values may be pinned; <see cref="Unknown"/> reserves 0 for a reason outside the list.</summary>
public enum DiagramImportError
{
    /// <summary>A reason outside the known set (e.g. an older library build).</summary>
    Unknown = 0,
    /// <summary>The CSV text is empty.</summary>
    CsvEmpty = 1,
    /// <summary>The CSV has a header but no data rows.</summary>
    CsvNoData = 2,
    /// <summary>The CSV header has fewer than two columns.</summary>
    CsvMinColumns = 3,
    /// <summary>A required semantic field has no column mapped.</summary>
    MappingMissing = 4,
    /// <summary>A mapped column is not present in the CSV header.</summary>
    ColumnNotFound = 5,
    /// <summary>The input is too complex to analyse within the time or size budget.</summary>
    InputTooComplex = 6
}

/// <summary>
/// Thrown by <see cref="CsvParser"/>, the CSV generators and <see cref="SqlParser"/>. Derives from
/// <see cref="InvalidOperationException"/> so existing catch blocks keep working;
/// <see cref="Exception.Message"/> is an English developer message and must never be shown to end
/// users — map <see cref="Error"/> to a localized text instead.
/// </summary>
public sealed class DiagramImportException : InvalidOperationException
{
    /// <summary>Creates the exception.</summary>
    public DiagramImportException(DiagramImportError error, string message, string? column = null, string? field = null, Exception? inner = null)
        : base(message, inner)
    {
        Error = error;
        Column = column;
        Field = field;
    }

    /// <summary>The failure reason.</summary>
    public DiagramImportError Error { get; }

    /// <summary>The CSV column involved (user data), when any.</summary>
    public string? Column { get; }

    /// <summary>The semantic field involved ("Name", "Manager", …), when any.</summary>
    public string? Field { get; }
}
