using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace Tempo.Blazor.Components.DataTable;

public partial class TmDataTable<TItem>
{
    private int _activeRow;
    private int _activeCol;

    private bool IsGrid => KeyboardNavigation == DataTableKeyboardNavigation.Grid;

    private string CellTabIndex(int row, int col) =>
        IsGrid && row == _activeRow && col == _activeCol ? "0" : IsGrid ? "-1" : "0";

    private string RowTabIndex() => IsGrid ? "-1" : "0";

    private int GridColumnCount =>
        _visibleColumns.Count + (Selectable ? 1 : 0) + (HasDetail ? 1 : 0);

    private Task HandleGridKeyDownAsync(KeyboardEventArgs e, int row, int col, TItem? item)
    {
        if (!IsGrid)
            return Task.CompletedTask;

        var moved = false;
        switch (e.Key)
        {
            case "ArrowRight":
                _activeCol = Math.Min(GridColumnCount - 1, col + 1);
                _activeRow = row;
                moved = true;
                break;
            case "ArrowLeft":
                _activeCol = Math.Max(0, col - 1);
                _activeRow = row;
                moved = true;
                break;
            case "ArrowDown":
                _activeRow = Math.Min(Math.Max(0, _displayedItems.Count - 1), row + 1);
                _activeCol = col;
                moved = true;
                break;
            case "ArrowUp":
                _activeRow = Math.Max(0, row - 1);
                _activeCol = col;
                moved = true;
                break;
            case "PageDown":
                _activeRow = Math.Min(Math.Max(0, _displayedItems.Count - 1), row + 10);
                _activeCol = col;
                moved = true;
                break;
            case "PageUp":
                _activeRow = Math.Max(0, row - 10);
                _activeCol = col;
                moved = true;
                break;
            case "Home":
                _activeCol = 0;
                _activeRow = e.CtrlKey ? 0 : row;
                moved = true;
                break;
            case "End":
                _activeCol = Math.Max(0, GridColumnCount - 1);
                _activeRow = e.CtrlKey ? Math.Max(0, _displayedItems.Count - 1) : row;
                moved = true;
                break;
            case "Enter" when !e.Repeat && item is not null:
                return HandleRowClickAsync(item);
            case " " when Selectable && item is not null:
                return ToggleRowSelectionAsync(item, new ChangeEventArgs { Value = !IsSelected(item) });
        }

        if (!moved)
            return Task.CompletedTask;
        StateHasChanged();
        return Task.CompletedTask;
    }
}
