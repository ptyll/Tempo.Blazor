using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Tempo.Blazor.Components.Spreadsheet.Enums;
using Tempo.Blazor.Components.Spreadsheet.Models;

namespace Tempo.Blazor.Components.Spreadsheet.Data;

/// <summary>
/// The auto-filter dropdown shown for a single header column. Offers ascending/descending sort, a
/// search box over the distinct values, a checkbox list (with select-all), an entry point to the
/// custom (text/number/date) filter dialog, and OK/Cancel. All text is localized.
/// </summary>
public partial class TmSpreadsheetFilterDropdown
{
    private string _search = string.Empty;
    private List<SpreadsheetFilterValue> _allValues = [];
    private HashSet<string> _checked = new(StringComparer.Ordinal);

    /// <summary>The sheet the filter applies to.</summary>
    [Parameter, EditorRequired] public SpreadsheetSheet Sheet { get; set; } = null!;

    /// <summary>The active auto-filter.</summary>
    [Parameter, EditorRequired] public SpreadsheetAutoFilter Filter { get; set; } = null!;

    /// <summary>The zero-based column index this dropdown filters.</summary>
    [Parameter] public int ColumnIndex { get; set; }

    /// <summary>The culture used to format and compare values.</summary>
    [Parameter] public CultureInfo? Culture { get; set; }

    /// <summary>The left position (px) of the dropdown.</summary>
    [Parameter] public double X { get; set; }

    /// <summary>The top position (px) of the dropdown.</summary>
    [Parameter] public double Y { get; set; }

    /// <summary>Raised when the user requests an ascending sort by this column.</summary>
    [Parameter] public EventCallback<int> OnSortAscending { get; set; }

    /// <summary>Raised when the user requests a descending sort by this column.</summary>
    [Parameter] public EventCallback<int> OnSortDescending { get; set; }

    /// <summary>Raised when the user applies a value filter (null clears this column's filter).</summary>
    [Parameter] public EventCallback<SpreadsheetColumnFilter?> OnApply { get; set; }

    /// <summary>Raised when the user opens the custom text/number/date filter dialog.</summary>
    [Parameter] public EventCallback<int> OnOpenCustomFilter { get; set; }

    /// <summary>Raised when the dropdown is dismissed.</summary>
    [Parameter] public EventCallback OnClose { get; set; }

    private CultureInfo EffectiveCulture => Culture ?? CultureInfo.CurrentCulture;

    private bool HasActiveFilter => Filter.GetColumn(ColumnIndex)?.IsActive ?? false;

    private string CustomFilterLabelKey => DetectColumnKind() switch
    {
        SpreadsheetFilterKind.Number => "TmSpreadsheet_Filter_NumberFilters",
        SpreadsheetFilterKind.Date => "TmSpreadsheet_Filter_DateFilters",
        _ => "TmSpreadsheet_Filter_TextFilters"
    };

    private IReadOnlyList<SpreadsheetFilterValue> VisibleValues => string.IsNullOrEmpty(_search)
        ? _allValues
        : _allValues.Where(v => !v.IsBlank
            && v.Display.Contains(_search, StringComparison.OrdinalIgnoreCase)).ToList();

    private bool AllVisibleChecked => VisibleValues.Count > 0 && VisibleValues.All(v => _checked.Contains(v.Display));

    protected override void OnParametersSet()
    {
        _allValues = SpreadsheetFilterEngine.DistinctValues(Sheet, Filter, ColumnIndex, EffectiveCulture).ToList();

        var existing = Filter.GetColumn(ColumnIndex);
        if (existing is { Kind: SpreadsheetFilterKind.Values, AllowedValues: { } allowed })
            _checked = new HashSet<string>(allowed, StringComparer.Ordinal);
        else
            _checked = new HashSet<string>(_allValues.Select(v => v.Display), StringComparer.Ordinal);
    }

    private SpreadsheetFilterKind DetectColumnKind()
    {
        for (var row = Filter.FirstDataRow; row <= Filter.Range.EndRow; row++)
        {
            var cell = Sheet.GetCell(row, ColumnIndex);
            switch (cell?.DataType)
            {
                case SpreadsheetDataType.Number or SpreadsheetDataType.Currency or SpreadsheetDataType.Percentage:
                    return SpreadsheetFilterKind.Number;
                case SpreadsheetDataType.Date or SpreadsheetDataType.DateTime or SpreadsheetDataType.Time:
                    return SpreadsheetFilterKind.Date;
            }
        }

        return SpreadsheetFilterKind.Text;
    }

    private void OnSearchInput(ChangeEventArgs e) => _search = e.Value?.ToString() ?? string.Empty;

    private void ToggleValue(string display)
    {
        if (!_checked.Remove(display))
            _checked.Add(display);
    }

    private void ToggleSelectAll(ChangeEventArgs e)
    {
        var check = e.Value is bool b && b;
        foreach (var item in VisibleValues)
        {
            if (check)
                _checked.Add(item.Display);
            else
                _checked.Remove(item.Display);
        }
    }

    private Task SortAscending() => OnSortAscending.InvokeAsync(ColumnIndex);

    private Task SortDescending() => OnSortDescending.InvokeAsync(ColumnIndex);

    private Task OpenCustomFilter() => OnOpenCustomFilter.InvokeAsync(ColumnIndex);

    private Task ClearColumnFilter() => OnApply.InvokeAsync(null);

    private Task ApplyValues()
    {
        // All values checked → no restriction (clear the column filter).
        if (_allValues.Count > 0 && _allValues.All(v => _checked.Contains(v.Display)))
            return OnApply.InvokeAsync(null);

        var allowed = new HashSet<string>(
            _allValues.Where(v => _checked.Contains(v.Display)).Select(v => v.Display),
            StringComparer.Ordinal);

        return OnApply.InvokeAsync(new SpreadsheetColumnFilter
        {
            ColumnIndex = ColumnIndex,
            Kind = SpreadsheetFilterKind.Values,
            AllowedValues = allowed
        });
    }

    private Task Close() => OnClose.InvokeAsync();

    // Escape stays container-level so it works wherever focus sits in the
    // dropdown. Enter->ApplyValues lives on the search input only: every other
    // control here is a native <button> or checkbox — the browser turns Enter
    // on a focused <button> into a click on keydown, so a container-level
    // Enter case applied the filter on top of the control's own click.
    private async Task OnKeyDown(KeyboardEventArgs e)
    {
        if (e.Key == "Escape")
            await Close();
    }

    private async Task OnSearchKeyDown(KeyboardEventArgs e)
    {
        if (e.Key == "Enter")
            await ApplyValues();
    }
}
