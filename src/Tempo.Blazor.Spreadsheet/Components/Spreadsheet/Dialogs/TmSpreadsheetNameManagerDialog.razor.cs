using Microsoft.JSInterop;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Tempo.Blazor.Components.Spreadsheet.Models;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.Components.Feedback;

namespace Tempo.Blazor.Components.Spreadsheet.Dialogs;

/// <summary>
/// Name Manager dialog that lists all named ranges in the workbook, allows filtering,
/// and raises events for New / Edit / Delete. The table is a WAI-ARIA grid with a roving tabindex:
/// the selected row is the single tab stop and owns the DOM focus, ↑/↓/Home/End move it, Enter edits
/// the selected row and Delete deletes it (selecting the neighbour afterwards).
/// </summary>
public partial class TmSpreadsheetNameManagerDialog
{
    // Per-instance id suffix: ids are implementation detail (the dialog contract is role + name),
    // never a selector for a host or a test.
    private readonly string _idSuffix = Guid.NewGuid().ToString("N")[..8];

    private string _filter = string.Empty;
    private SpreadsheetNamedRange? _selectedRange;

    // Per-item element references: the roving tabindex needs the DOM focus on the item it just moved
    // to, so the active row's reference is focused after the render that moved the tabindex.
    private readonly Dictionary<SpreadsheetNamedRange, ElementReference> _rowElements = [];
    private SpreadsheetNamedRange? _pendingFocus;

    private string NewButtonDomId => $"spreadsheet-name-manager-new-{_idSuffix}";

    private string RowDomId(int index) => $"spreadsheet-name-manager-row-{_idSuffix}-{index}";

    /// <summary>The workbook whose named ranges are being managed.</summary>
    [Parameter, EditorRequired] public SpreadsheetWorkbook Workbook { get; set; } = null!;

    /// <summary>Raised when the user wants to create a new named range.</summary>
    [Parameter] public EventCallback OnNew { get; set; }

    /// <summary>Raised when the user wants to edit the selected named range.</summary>
    [Parameter] public EventCallback<SpreadsheetNamedRange> OnEdit { get; set; }

    /// <summary>Raised when the user wants to delete the selected named range.</summary>
    [Parameter] public EventCallback<SpreadsheetNamedRange> OnDelete { get; set; }

    /// <summary>Raised when the dialog is dismissed.</summary>
    [Parameter] public EventCallback OnClose { get; set; }

    /// <summary>
    /// The id of the element focus returns to on close — the host spreadsheet passes its grid root so
    /// focus lands on the grid. Null falls back to the element focused before the dialog opened.
    /// </summary>
    [Parameter] public string? RestoreFocusTargetId { get; set; }

    /// <summary>
    /// The layout the dialog renders. <see cref="TmLayoutMode.Auto"/> follows the viewport scope (a
    /// fullscreen panel on the narrow one); a forced value pins the mode without measuring, so a test
    /// can render that presentation without a DOM.
    /// </summary>
    [Parameter] public TmLayoutMode LayoutMode { get; set; } = TmLayoutMode.Auto;

    /// <summary>The viewport's layout, when the app provides a viewport-scope observer.</summary>
    [CascadingParameter(Name = TmLayoutScopes.Viewport)] private TmLayoutContext? Viewport { get; set; }

    /// <summary>
    /// Whether the list presents as cards (the phone layout). A forced <see cref="LayoutMode"/> wins;
    /// <see cref="TmLayoutMode.Auto"/> reads the app-level viewport scope and keeps the table when the
    /// host provides no scope.
    /// </summary>
    private bool Mobile => LayoutMode switch
    {
        TmLayoutMode.Mobile => true,
        TmLayoutMode.Auto => Viewport?.IsMobile == true,
        _ => false
    };

    protected override void OnParametersSet() => EnsureSelection();

    private void OnFilterChanged(string value)
    {
        _filter = value ?? string.Empty;
        EnsureSelection();
    }

    private IReadOnlyList<SpreadsheetNamedRange> FilteredRanges =>
        string.IsNullOrWhiteSpace(_filter)
            ? Workbook.NamedRanges
            : Workbook.NamedRanges
                .Where(r => r.Name.Contains(_filter, StringComparison.OrdinalIgnoreCase))
                .ToList();

    /// <summary>Index of the selected row inside <see cref="FilteredRanges"/>, or -1 when nothing is selected.</summary>
    private int SelectedIndex
    {
        get
        {
            if (_selectedRange is null)
            {
                return -1;
            }

            var ranges = FilteredRanges;
            for (var i = 0; i < ranges.Count; i++)
            {
                if (ReferenceEquals(ranges[i], _selectedRange))
                {
                    return i;
                }
            }

            return -1;
        }
    }

    /// <summary>
    /// The element the focus scope focuses on open: the selected row when the list has rows, the New
    /// button when it is empty. Resolved by id inside the scope, so the module reads it at activation
    /// time and it does not depend on a reference captured after this render.
    /// </summary>
    private string InitialFocusDomId => FilteredRanges.Count == 0 ? NewButtonDomId : RowDomId(Math.Max(SelectedIndex, 0));

    private bool IsSelected(SpreadsheetNamedRange range) => ReferenceEquals(_selectedRange, range);

    private string GetRowClass(SpreadsheetNamedRange range)
        => IsSelected(range) ? "tm-spreadsheet-name-manager__row--selected" : string.Empty;

    private string GetCardClass(SpreadsheetNamedRange range)
    {
        var classes = new List<string> { "tm-spreadsheet-name-manager__card" };
        if (IsSelected(range))
        {
            classes.Add("tm-spreadsheet-name-manager__card--selected");
        }

        return string.Join(" ", classes);
    }

    /// <summary>
    /// The grid always has exactly one roving tab stop and the toolbar acts only on a visible row:
    /// when nothing is selected — the first open, or a filter that just hid the selected row — the
    /// first visible row takes the selection, and an empty list clears it (the New button is the
    /// focus target then). Called from the parameter set and from the filter's <c>@bind:after</c>.
    /// </summary>
    private void EnsureSelection()
    {
        if (SelectedIndex >= 0)
        {
            return;
        }

        var ranges = FilteredRanges;
        _selectedRange = ranges.Count > 0 ? ranges[0] : null;
    }

    /// <summary>The toolbar edits and deletes only a row that is actually visible.</summary>
    private bool HasVisibleSelection => SelectedIndex >= 0;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        await base.OnAfterRenderAsync(firstRender);

        PruneRowElements();

        if (_pendingFocus is null)
        {
            return;
        }

        var target = _pendingFocus;
        _pendingFocus = null;
        if (_rowElements.TryGetValue(target, out var element))
        {
            try
            {
                await element.FocusAsync();
            }
            catch (Exception e) when (e is JSException or JSDisconnectedException or TaskCanceledException)
            {
                // The row can be filtered out between the key press and the render, or the circuit
                // can be gone — neither is worth failing the render for.
            }
        }
    }

    /// <summary>
    /// Drops the references of rows that are no longer rendered (deleted, or filtered out), so the
    /// dictionary cannot grow across a session and a stale reference is never focused.
    /// </summary>
    private void PruneRowElements()
    {
        if (_rowElements.Count == 0)
        {
            return;
        }

        var visible = FilteredRanges;
        foreach (var stale in _rowElements.Keys.Where(key => !visible.Contains(key)).ToList())
        {
            _rowElements.Remove(stale);
        }
    }

    private string? GetPreviewValue(SpreadsheetNamedRange range)
    {
        try
        {
            if (range.Scope == NamedRangeScope.Sheet && range.SheetIndex.HasValue)
            {
                var sheet = Workbook.Sheets[range.SheetIndex.Value];
                var cellRef = range.RefersTo.Replace("$", "").ToUpperInvariant();
                if (sheet.Cells.TryGetValue(cellRef, out var cell))
                    return cell.Value?.ToString();
            }
            else
            {
                var active = Workbook.ActiveSheet;
                if (active is not null)
                {
                    var cellRef = range.RefersTo.Replace("$", "").ToUpperInvariant();
                    if (active.Cells.TryGetValue(cellRef, out var cell))
                        return cell.Value?.ToString();
                }
            }
        }
        catch { /* ignore preview errors */ }

        return null;
    }

    private void OnNewClick() => OnNew.InvokeAsync();

    private void OnEditClick()
    {
        if (_selectedRange is not null)
            OnEdit.InvokeAsync(_selectedRange);
    }

    private Task OnDeleteClick() => DeleteSelectedAsync();

    private Task OnCloseClick() => OnClose.InvokeAsync();

    /// <summary>A click selects a row and hands it the DOM focus, so the keyboard works right after it.</summary>
    private void OnRowClick(SpreadsheetNamedRange range)
    {
        _selectedRange = range;
        _pendingFocus = range;
    }

    /// <summary>Double click on a row selects it and opens the editor — the mouse equivalent of Enter.</summary>
    private void OnRowDoubleClick(SpreadsheetNamedRange range)
    {
        _selectedRange = range;
        OnEdit.InvokeAsync(range);
    }

    /// <summary>
    /// Deletes the selected range and selects its neighbour in the shortened list (the row that takes
    /// its place, or the new last row), or clears the selection when the list becomes empty.
    /// </summary>
    private async Task DeleteSelectedAsync()
    {
        var ranges = FilteredRanges;
        var index = SelectedIndex;
        if (index < 0)
        {
            return;
        }

        var removed = ranges[index];
        _rowElements.Remove(removed);
        await OnDelete.InvokeAsync(removed);

        var remaining = FilteredRanges;
        _selectedRange = remaining.Count == 0 ? null : remaining[Math.Min(index, remaining.Count - 1)];
        _pendingFocus = _selectedRange;
    }

    /// <summary>
    /// The grid's keyboard contract: one activation per press. Rows are <c>&lt;tr&gt;</c> elements
    /// with no native activation, so Enter/Delete are emulated here — the browser delivers nothing of
    /// its own for them, which is exactly what keeps the count at one
    /// (docs/keyboard-activation-convention.md).
    /// </summary>
    private async Task OnTableKeyDown(KeyboardEventArgs e)
    {
        var ranges = FilteredRanges;
        if (ranges.Count == 0)
        {
            return;
        }

        var index = SelectedIndex;
        switch (e.Key)
        {
            case "ArrowDown":
                _selectedRange = ranges[index < 0 ? 0 : Math.Min(index + 1, ranges.Count - 1)];
                _pendingFocus = _selectedRange;
                break;
            case "ArrowUp":
                _selectedRange = ranges[index < 0 ? ranges.Count - 1 : Math.Max(index - 1, 0)];
                _pendingFocus = _selectedRange;
                break;
            case "Home":
                _selectedRange = ranges[0];
                _pendingFocus = _selectedRange;
                break;
            case "End":
                _selectedRange = ranges[^1];
                _pendingFocus = _selectedRange;
                break;
            case "Enter":
                if (index >= 0)
                {
                    await OnEdit.InvokeAsync(ranges[index]);
                }

                break;
            case "Delete":
                await DeleteSelectedAsync();
                break;
        }
    }
}
