namespace Tempo.Blazor.Models;

/// <summary>
/// Tabular snapshot of a data table for export: visible column headers and fully materialized
/// rows (all pages) for the current filter/sort, as display strings with optional raw values.
/// </summary>
public sealed class DataTableExportData
{
    /// <summary>Visible column headers, left to right.</summary>
    public IReadOnlyList<string> Headers { get; init; } = [];

    /// <summary>Rows, each a list of cell display strings aligned to <see cref="Headers"/>.</summary>
    public IReadOnlyList<IReadOnlyList<string?>> Rows { get; init; } = [];

    /// <summary>
    /// Optional raw cell values aligned to <see cref="Headers"/>. Typed exporters use these values
    /// to preserve native numbers, booleans, and dates; exporters that only need display text can
    /// continue using <see cref="Rows"/>.
    /// </summary>
    public IReadOnlyList<IReadOnlyList<object?>> Values { get; init; } = [];

    /// <summary>Optional sheet/table name used by exporters that support it (e.g. XLSX).</summary>
    public string? Name { get; init; }
}

/// <summary>
/// Serializes a <see cref="DataTableExportData"/> snapshot into a downloadable document
/// (for example CSV or XLSX). Implementations are format-specific and stateless.
/// </summary>
public interface IDataTableExporter
{
    /// <summary>Short format key, e.g. <c>csv</c> or <c>xlsx</c>.</summary>
    string Format { get; }

    /// <summary>MIME content type of the produced document.</summary>
    string ContentType { get; }

    /// <summary>File extension (without the dot), e.g. <c>csv</c> or <c>xlsx</c>.</summary>
    string FileExtension { get; }

    /// <summary>Serializes the export data into document bytes.</summary>
    /// <param name="data">Headers and rows to export.</param>
    byte[] Export(DataTableExportData data);
}

/// <summary>Which rows a data-table export includes.</summary>
public enum DataTableExportScope
{
    /// <summary>Every row matching the current filter and sort, paging bypassed. This is the default.</summary>
    All,

    /// <summary>Only the rows the user has checked. Requires a selectable table.</summary>
    Selected
}

/// <summary>Built-in export formats supported by <c>TmDataTable</c>.</summary>
public enum DataTableExportFormat
{
    /// <summary>Comma-separated values encoded as UTF-8 with a byte-order mark.</summary>
    Csv,

    /// <summary>Office Open XML workbook supplied by the optional XLSX exporter service.</summary>
    Xlsx
}

/// <summary>Describes a successfully generated and downloaded data-table export.</summary>
/// <param name="Format">Generated file format.</param>
/// <param name="FileName">Browser download file name.</param>
/// <param name="RowCount">Number of exported data rows, excluding the header.</param>
/// <param name="Scope">Whether the file contains every matching row or only the checked rows.</param>
public sealed record DataTableExportResult(
    DataTableExportFormat Format,
    string FileName,
    int RowCount,
    DataTableExportScope Scope = DataTableExportScope.All);

/// <summary>
/// Optional service contract for producing XLSX bytes without adding an Open XML dependency to
/// the core component package.
/// </summary>
public interface IDataTableXlsxExporter
{
    /// <summary>Serializes the supplied table snapshot as an XLSX workbook.</summary>
    /// <param name="data">Visible headers and all filtered/sorted rows.</param>
    /// <returns>Complete XLSX file bytes.</returns>
    byte[] Export(DataTableExportData data);
}
