using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.Components.Spreadsheet.Models;

namespace Tempo.Blazor.Components.Spreadsheet.Dialogs;

/// <summary>
/// The Remove Duplicates dialog. Lists every column in the range with a checkbox, offers a
/// "my data has headers" toggle (which switches the column labels between header text and the column
/// letter), select-all / deselect-all helpers and a case-sensitivity option. Applying yields a
/// <see cref="SpreadsheetRemoveDuplicatesOptions"/>. All text is localized.
/// </summary>
public partial class TmSpreadsheetRemoveDuplicatesDialog
{
    private readonly string _idSuffix = Guid.NewGuid().ToString("N")[..8];

    private readonly HashSet<int> _selected = [];
    private bool _hasHeader = true;
    private bool _caseSensitive;

    /// <summary>The range being deduplicated.</summary>
    [Parameter, EditorRequired] public SpreadsheetRange Range { get; set; } = null!;

    /// <summary>
    /// Header text per absolute column index, used to label columns when <c>my data has headers</c>
    /// is on. Columns without an entry fall back to their column letter.
    /// </summary>
    [Parameter] public IReadOnlyDictionary<int, string>? HeaderLabels { get; set; }

    /// <summary>Raised when the user applies the dialog.</summary>
    [Parameter] public EventCallback<SpreadsheetRemoveDuplicatesOptions> OnApply { get; set; }

    /// <summary>Raised when the dialog is dismissed.</summary>
    [Parameter] public EventCallback OnClose { get; set; }

    /// <summary>
    /// The id of the element focus returns to on close — the host spreadsheet passes its grid root so
    /// focus lands on the grid. Null falls back to the element focused before the dialog opened.
    /// </summary>
    [Parameter] public string? RestoreFocusTargetId { get; set; }

    /// <summary>
    /// The layout the dialog renders. <see cref="TmLayoutMode.Auto"/> follows the viewport scope; a
    /// forced value pins the mode without measuring, so a test can render the mobile sheet.
    /// </summary>
    [Parameter] public TmLayoutMode LayoutMode { get; set; } = TmLayoutMode.Auto;

    private IReadOnlyList<int> Columns { get; set; } = [];

    protected override void OnParametersSet()
    {
        Columns = Enumerable.Range(Range.StartCol, Range.ColumnCount).ToList();

        // Default to all columns selected the first time the dialog is shown.
        if (_selected.Count == 0)
            foreach (var c in Columns)
                _selected.Add(c);
    }

    /// <summary>The first column's checkbox, or null for an empty range (a null id is skipped by the trap).</summary>
    private string? FirstColumnDomId => Columns.Count == 0 ? null : ColumnDomId(Columns[0]);

    /// <summary>The per-instance checkbox id the dialog focuses on open (the first column).</summary>
    private string ColumnDomId(int col) => $"spreadsheet-dedup-column-{_idSuffix}-{col}";

    private string ColumnLabel(int col)
    {
        if (_hasHeader && HeaderLabels is not null && HeaderLabels.TryGetValue(col, out var label) && !string.IsNullOrWhiteSpace(label))
            return label;

        return string.Format(Loc["TmSpreadsheet_Dedup_ColumnLabel"], SpreadsheetRange.ColumnIndexToLetters(col));
    }

    private bool IsSelected(int col) => _selected.Contains(col);

    private void ToggleColumn(int col, bool selected)
    {
        if (selected)
            _selected.Add(col);
        else
            _selected.Remove(col);
    }

    private void SelectAll()
    {
        foreach (var c in Columns)
            _selected.Add(c);
    }

    private void DeselectAll() => _selected.Clear();

    private Task Apply()
    {
        var options = new SpreadsheetRemoveDuplicatesOptions
        {
            KeyColumns = _selected.OrderBy(c => c).ToList(),
            HasHeader = _hasHeader,
            CaseSensitive = _caseSensitive
        };

        return OnApply.InvokeAsync(options);
    }

    private Task Close() => OnClose.InvokeAsync();

}
