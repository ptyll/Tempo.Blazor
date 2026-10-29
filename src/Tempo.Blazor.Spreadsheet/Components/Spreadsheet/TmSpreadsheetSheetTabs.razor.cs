using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using Tempo.Blazor.Components.Spreadsheet.Models;

namespace Tempo.Blazor.Components.Spreadsheet;

/// <summary>Tab bar for switching between workbook sheets with add, delete, and rename support.</summary>
public partial class TmSpreadsheetSheetTabs : IAsyncDisposable
{
    [Inject] private IJSRuntime JS { get; set; } = default!;

    private readonly string _overflowObserverId = $"tm-spreadsheet-tabs-{Guid.NewGuid():N}";
    private DotNetObjectReference<TmSpreadsheetSheetTabs>? _overflowDotNetRef;
    private bool _overflowObserved;
    private bool _disposed;

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
        _contextMenuSheetIndex = index;

        if (!RenderContextMenu)
        {
            // The host owns the menu: it must render it OUTSIDE the layout container (see
            // SpreadsheetContextMenuRequest — containment would reposition a fixed menu).
            OnContextMenuRequested.InvokeAsync(new SpreadsheetContextMenuRequest
            {
                SheetIndex = index,
                ClientX = e.ClientX,
                ClientY = e.ClientY,
            });
            return;
        }

        _contextMenuVisible = true;
    }

    /// <summary>
    /// Whether this component renders its own tab context menu. <see cref="TmSpreadsheet"/> passes
    /// <c>false</c> and renders it as a sibling of the layout container.
    /// </summary>
    [Parameter] public bool RenderContextMenu { get; set; } = true;

    /// <summary>Raised with the gesture when <see cref="RenderContextMenu"/> is <c>false</c>.</summary>
    [Parameter] public EventCallback<SpreadsheetContextMenuRequest> OnContextMenuRequested { get; set; }

    /// <summary>Starts the inline rename of the sheet at <paramref name="index"/> (the host's context menu action).</summary>
    public void RequestRename(int index)
    {
        if (index < 0 || index >= Sheets.Count)
        {
            return;
        }

        _editingIndex = index;
        _editName = Sheets[index].Name;
        _shouldFocusRenameInput = true;
        CloseContextMenu();
        StateHasChanged();
    }

    /// <summary>Raises the delete request for the sheet at <paramref name="index"/> (the host's context menu action).</summary>
    public void RequestDelete(int index)
    {
        if (index < 0)
        {
            return;
        }

        CloseContextMenu();
        OnDeleteSheetRequested.InvokeAsync(index);
    }

    /// <summary>Closes the built-in tab context menu.</summary>
    public void CloseContextMenu()
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

        // Phase 6: the strip lives in a CONTAINER-sized layout, so its own width can change without any
        // re-render of this component (the card grows, the container query flips, the host resizes a
        // column). A ResizeObserver on the scroller is the one signal that covers all of them.
        if (!_overflowObserved && !_disposed)
        {
            _overflowObserved = true;
            _overflowDotNetRef ??= DotNetObjectReference.Create(this);
            try
            {
                await JS.InvokeVoidAsync("tmSpreadsheetSheetTabs.observeOverflow", _scrollRef, _overflowDotNetRef, _overflowObserverId);
            }
            catch
            {
                // No script (prerender, bUnit): the strip keeps the per-render measurement.
            }
        }
    }

    /// <summary>Called by the shared ResizeObserver when the strip's own width changed.</summary>
    [JSInvokable]
    public async Task OnSheetTabsResized()
    {
        var hasOverflow = false;
        try
        {
            hasOverflow = await JS.InvokeAsync<bool>("tmSpreadsheetSheetTabs.hasOverflow", _scrollRef);
        }
        catch
        {
            // A detached scroller: nothing to measure, and nothing to re-render for.
            return;
        }

        await InvokeAsync(() =>
        {
            if (hasOverflow == _hasOverflow)
            {
                return;
            }

            _hasOverflow = hasOverflow;
            StateHasChanged();
        });
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_overflowObserved)
        {
            try
            {
                await JS.InvokeVoidAsync("tmSpreadsheetSheetTabs.disconnectOverflow", _overflowObserverId);
            }
            catch
            {
                // The circuit may already be gone with the module.
            }
        }

        _overflowDotNetRef?.Dispose();
        _overflowDotNetRef = null;
    }
}
