using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using Tempo.Blazor.Components.Spreadsheet.Models;

namespace Tempo.Blazor.Components.Spreadsheet;

/// <summary>Tab bar for switching between workbook sheets with add, delete, and rename support.</summary>
public partial class TmSpreadsheetSheetTabs
{
    [Inject] private IJSRuntime JS { get; set; } = default!;

    private ElementReference _renameInput;
    private ElementReference _scrollRef;
    private bool _hasOverflow;
    private int _editingIndex = -1;
    private string? _editName;
    private bool _shouldFocusRenameInput;

    private bool _contextMenuVisible;
    private double _contextMenuX;
    private double _contextMenuY;
    private int _contextMenuSheetIndex = -1;

    /// <summary>All sheets in the workbook.</summary>
    [Parameter] public List<SpreadsheetSheet> Sheets { get; set; } = new();

    /// <summary>Zero-based index of the active sheet.</summary>
    [Parameter] public int ActiveIndex { get; set; }

    /// <summary>Called when the active sheet should change.</summary>
    [Parameter] public EventCallback<int> OnActiveSheetChanged { get; set; }

    /// <summary>Called when a new sheet should be added.</summary>
    [Parameter] public EventCallback OnAddSheetRequested { get; set; }

    /// <summary>Called when a sheet should be deleted.</summary>
    [Parameter] public EventCallback<int> OnDeleteSheetRequested { get; set; }

    /// <summary>Called when a sheet should be renamed.</summary>
    [Parameter] public EventCallback<(int Index, string NewName)> OnRenameSheetRequested { get; set; }

    /// <summary>When true, hides add/delete/rename controls (embedded/readonly view).</summary>
    [Parameter] public bool IsReadonly { get; set; }

    /// <summary>
    /// Whether every tab paints its own × close affordance. Defaults to <c>true</c>. With
    /// <c>false</c> the clean reference tab look is kept and deleting stays available from the tab's
    /// context menu (the reader keeps both paths).
    /// </summary>
    [Parameter] public bool ShowTabCloseButtons { get; set; } = true;

    /// <summary>Whether formula-point mode is active (tab click selects sheet for formula rather than switching view).</summary>
    [Parameter] public bool IsFormulaPointMode { get; set; }

    /// <summary>Called when a tab is clicked in formula-point mode to switch the displayed sheet for ref selection.</summary>
    [Parameter] public EventCallback<int> OnSheetSelectedForFormula { get; set; }

    private void OnTabClick(int index)
    {
        CloseContextMenu();
        if (_editingIndex == index) return;
        _editingIndex = -1;
        if (index != ActiveIndex)
        {
            if (IsFormulaPointMode)
                OnSheetSelectedForFormula.InvokeAsync(index);
            else
                OnActiveSheetChanged.InvokeAsync(index);
        }
    }

    private void OnCloseClick(MouseEventArgs e, int index)
    {
        CloseContextMenu();
        OnDeleteSheetRequested.InvokeAsync(index);
    }

    private void OnAddSheet()
    {
        CloseContextMenu();
        OnAddSheetRequested.InvokeAsync();
    }

    private void OnTabContextMenu(MouseEventArgs e, int index)
    {
        _contextMenuX = e.ClientX;
        _contextMenuY = e.ClientY;
        _contextMenuVisible = true;
        _contextMenuSheetIndex = index;
    }

    private void CloseContextMenu()
    {
        _contextMenuVisible = false;
        _contextMenuSheetIndex = -1;
    }

    private void ContextMenuRename()
    {
        if (_contextMenuSheetIndex < 0 || _contextMenuSheetIndex >= Sheets.Count) return;
        _editingIndex = _contextMenuSheetIndex;
        _editName = Sheets[_editingIndex].Name;
        _shouldFocusRenameInput = true;
        CloseContextMenu();
        StateHasChanged();
    }

    private void ContextMenuDelete()
    {
        if (_contextMenuSheetIndex < 0) return;
        var index = _contextMenuSheetIndex;
        CloseContextMenu();
        OnDeleteSheetRequested.InvokeAsync(index);
    }

    private void CommitRename(int index)
    {
        if (_editingIndex != index) return;
        if (!string.IsNullOrWhiteSpace(_editName))
        {
            OnRenameSheetRequested.InvokeAsync((index, _editName.Trim()));
        }
        _editingIndex = -1;
        _editName = null;
    }

    private void OnRenameKeyDown(KeyboardEventArgs e, int index)
    {
        if (e.Key == "Enter")
        {
            CommitRename(index);
        }
        else if (e.Key == "Escape")
        {
            _editingIndex = -1;
            _editName = null;
        }
    }

    private async Task ScrollTabsAsync(string direction)
    {
        try
        {
            await JS.InvokeVoidAsync("tmSpreadsheetSheetTabs.scrollTabs", _scrollRef, direction);
        }
        catch
        {
            // The package script may not be loaded (prerender, bUnit, a host without the script tag).
        }
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (_shouldFocusRenameInput)
        {
            _shouldFocusRenameInput = false;
            try { await _renameInput.FocusAsync(); } catch { /* ElementReference may not be bound yet */ }
        }

        // Overflow arrows are a measured affordance: ask the package script after every render and only
        // re-render when the answer flips (the arrows live outside the scroller, so this settles).
        var hasOverflow = false;
        try
        {
            hasOverflow = await JS.InvokeAsync<bool>("tmSpreadsheetSheetTabs.hasOverflow", _scrollRef);
        }
        catch
        {
            // No script / prerender: keep the strip scrollable without arrows.
        }

        if (hasOverflow != _hasOverflow)
        {
            _hasOverflow = hasOverflow;
            StateHasChanged();
        }
    }
}
