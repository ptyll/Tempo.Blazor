using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using System.Globalization;
using Tempo.Blazor.Components.NotionEditor.Services;
using Tempo.Blazor.Interfaces;

namespace Tempo.Blazor.Components.NotionEditor.UI;

public partial class TmNotionTokenDropdown : ComponentBase, IDisposable
{
    // ── DI ───────────────────────────────────────────────────────────────────

    [Inject] private IJSRuntime JS { get; set; } = default!;

    // ── Cascaded context ─────────────────────────────────────────────────────

    [CascadingParameter]
    private NotionEditorContext Context { get; set; } = default!;

    // ── Parameters ───────────────────────────────────────────────────────────

    [Parameter] public bool   Visible { get; set; }
    [Parameter] public double Top     { get; set; }
    [Parameter] public double Left    { get; set; }

    /// <summary>Raised when the user selects a token. Args = (Key, DisplayName, ColorClass).</summary>
    [Parameter] public EventCallback<(string Key, string DisplayName, string? ColorClass)> OnItemSelected { get; set; }

    /// <summary>Raised when the user dismisses the dropdown (Escape / backdrop click).</summary>
    [Parameter] public EventCallback OnClosed { get; set; }

    /// <summary>Raised when the user clicks "Create token" (optional). Arg = current search query.</summary>
    [Parameter] public EventCallback<string> OnCreateRequested { get; set; }

    /// <summary>Pre-selects the item matching this key when the dropdown opens.</summary>
    [Parameter] public string? CurrentKey { get; set; }

    // ── State ────────────────────────────────────────────────────────────────

    private string           _query         = string.Empty;
    private int              _selectedIndex;
    private double           _top;
    private double           _left;
    private bool             _wasVisible;
    private bool             _needsFocus;
    private bool             _needsPositionAdjustment;
    private bool             _isLoading;
    private List<IToken>     _items         = [];
    private CancellationTokenSource? _cts;

    private ElementReference _menuRef;
    private ElementReference _inputRef;
    private ElementReference _listRef;

    private string MenuStyle => string.Create(
        CultureInfo.InvariantCulture,
        $"--tm-nmm-anchor-top:{_top}px;--tm-nmm-anchor-left:{_left}px;top:max(8px,min({_top}px,calc(100vh - 360px - 8px)));left:max(8px,min({_left}px,calc(100vw - 360px - 8px)))");

    // ── Lifecycle ────────────────────────────────────────────────────────────

    protected override async Task OnParametersSetAsync()
    {
        if (Visible && !_wasVisible)
        {
            _query         = string.Empty;
            _selectedIndex = 0;
            _top           = Top;
            _left          = Left;
            _needsFocus    = true;
            _needsPositionAdjustment = true;
            _items         = [];
            await SearchAsync(string.Empty);
        }
        else if (!Visible && _wasVisible)
        {
            _cts?.Cancel();
        }
        _wasVisible = Visible;
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (_needsFocus && Visible)
        {
            _needsFocus = false;
            try { await JS.InvokeVoidAsync("eval", "void 0"); } catch { }
            // Same focus race as the mention/slash menus — re-assert across frames.
            try { await JS.InvokeVoidAsync("tmNotionEditor.focusMenuInput", _inputRef); } catch { }
        }

        if (_needsPositionAdjustment && Visible)
        {
            _needsPositionAdjustment = false;
            try { await JS.InvokeVoidAsync("tmNotionEditor.adjustSlashMenuPosition", _menuRef); }
            catch { }
        }
    }

    // ── Search ────────────────────────────────────────────────────────────────

    private async Task SearchAsync(string query)
    {
        if (Context.TokenProvider is null) return;

        _cts?.Cancel();
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        _isLoading = true;
        StateHasChanged();

        try
        {
            var results = await Context.TokenProvider.SearchTokensAsync(query, token);
            if (!token.IsCancellationRequested)
            {
                _items = results.ToList();
                if (!string.IsNullOrEmpty(CurrentKey))
                {
                    var ki = _items.FindIndex(t => t.Key == CurrentKey);
                    _selectedIndex = ki >= 0 ? ki : 0;
                }
                else
                {
                    _selectedIndex = 0;
                }
                _isLoading = false;
                _needsPositionAdjustment = true;
                StateHasChanged();
            }
        }
        catch (OperationCanceledException) { }
        catch
        {
            if (!token.IsCancellationRequested)
            {
                _isLoading = false;
                _needsPositionAdjustment = true;
                StateHasChanged();
            }
        }
    }

    // ── Input ─────────────────────────────────────────────────────────────────

    private async Task HandleQueryInputAsync(ChangeEventArgs e)
    {
        _query = e.Value?.ToString() ?? string.Empty;
        await SearchAsync(_query);
    }

    // ── Keyboard navigation ───────────────────────────────────────────────────

    private async Task HandleKeyDownAsync(KeyboardEventArgs e)
    {
        switch (e.Key)
        {
            case "ArrowDown":
                _selectedIndex = Math.Min(_selectedIndex + 1, _items.Count - 1);
                await ScrollSelectedIntoViewAsync();
                break;
            case "ArrowUp":
                _selectedIndex = Math.Max(_selectedIndex - 1, 0);
                await ScrollSelectedIntoViewAsync();
                break;
            case "Enter":
                if (_selectedIndex >= 0 && _selectedIndex < _items.Count)
                    await SelectItemAsync(_items[_selectedIndex]);
                break;
            case "Escape":
                await OnClosed.InvokeAsync();
                break;
        }
    }

    private async Task ScrollSelectedIntoViewAsync()
    {
        try
        {
            await JS.InvokeVoidAsync("tmNotionEditor.scrollSlashItemIntoView",
                _listRef, _selectedIndex);
        }
        catch { }
        StateHasChanged();
    }

    // ── Selection ─────────────────────────────────────────────────────────────

    private async Task SelectItemAsync(IToken token)
    {
        await OnItemSelected.InvokeAsync((token.Key, token.DisplayName, token.ColorClass));
    }

    // ── Create ────────────────────────────────────────────────────────────────

    private async Task HandleCreateAsync() => await OnCreateRequested.InvokeAsync(_query);

    // ── Backdrop ──────────────────────────────────────────────────────────────

    private async Task HandleBackdropClickAsync() => await OnClosed.InvokeAsync();

    // ── Dispose ───────────────────────────────────────────────────────────────

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
    }
}
