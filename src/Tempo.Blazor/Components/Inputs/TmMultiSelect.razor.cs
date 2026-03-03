using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Tempo.Blazor.Interfaces;
using Tempo.Blazor.Models;

namespace Tempo.Blazor.Components.Inputs;

public partial class TmMultiSelect<TItem, TValue>
{
    // ── Data Binding ──────────────────────────────────────────────

    /// <summary>Currently selected values.</summary>
    [Parameter] public IReadOnlyList<TValue> Values { get; set; } = [];

    /// <summary>Fires when the selection changes.</summary>
    [Parameter] public EventCallback<IReadOnlyList<TValue>> ValuesChanged { get; set; }

    // ── Data Source ───────────────────────────────────────────────

    /// <summary>Static list of items. Ignored when DataProvider is set.</summary>
    [Parameter] public IEnumerable<TItem> Items { get; set; } = [];

    /// <summary>Server-side data provider. When set, Items is ignored.</summary>
    [Parameter] public IDropdownDataProvider<TItem>? DataProvider { get; set; }

    // ── Field Selectors ──────────────────────────────────────────

    /// <summary>Extracts display text from an item.</summary>
    [Parameter, EditorRequired] public Func<TItem, string> DisplayField { get; set; } = default!;

    /// <summary>Extracts the value from an item.</summary>
    [Parameter, EditorRequired] public Func<TItem, TValue> ValueField { get; set; } = default!;

    /// <summary>Extracts a string key for @key and ExcludedIds. Defaults to ValueField.ToString().</summary>
    [Parameter] public Func<TItem, string>? IdField { get; set; }

    /// <summary>Extracts a group name for grouped display.</summary>
    [Parameter] public Func<TItem, string?>? GroupField { get; set; }

    // ── Display Mode ─────────────────────────────────────────────

    /// <summary>Visual mode for selected items. Default: Chip.</summary>
    [Parameter] public MultiSelectMode Mode { get; set; } = MultiSelectMode.Chip;

    /// <summary>Delimiter string for Delimiter mode. Default: ", ".</summary>
    [Parameter] public string Delimiter { get; set; } = ", ";

    // ── Standard Form Parameters ─────────────────────────────────

    /// <summary>Label shown above the component.</summary>
    [Parameter] public string? Label { get; set; }

    /// <summary>Placeholder text when no items are selected.</summary>
    [Parameter] public string? Placeholder { get; set; }

    /// <summary>Validation error message.</summary>
    [Parameter] public string? Error { get; set; }

    /// <summary>Help text shown below when no error is present.</summary>
    [Parameter] public string? HelpText { get; set; }

    /// <summary>HTML id attribute.</summary>
    [Parameter] public string Id { get; set; } = Guid.NewGuid().ToString("n")[..8];

    /// <summary>Additional CSS class(es).</summary>
    [Parameter] public string? Class { get; set; }

    /// <summary>Disables the component.</summary>
    [Parameter] public bool Disabled { get; set; }

    /// <summary>Marks the field as required.</summary>
    [Parameter] public bool Required { get; set; }

    // ── Filtering ────────────────────────────────────────────────

    /// <summary>Enables the search input in the dropdown. Default: true.</summary>
    [Parameter] public bool AllowFiltering { get; set; } = true;

    /// <summary>Placeholder for the filter input.</summary>
    [Parameter] public string? FilterPlaceholder { get; set; }

    /// <summary>Debounce delay in ms for server-side filtering. Default: 300.</summary>
    [Parameter] public int Debounce { get; set; } = 300;

    // ── Selection Behavior ───────────────────────────────────────

    /// <summary>Maximum selectable items. 0 = unlimited.</summary>
    [Parameter] public int MaxSelectionCount { get; set; }

    /// <summary>Shows Select All / Deselect All control.</summary>
    [Parameter] public bool ShowSelectAll { get; set; }

    /// <summary>Hides already-selected items from the dropdown.</summary>
    [Parameter] public bool HideSelectedItems { get; set; }

    /// <summary>Shows a clear-all button. Default: true.</summary>
    [Parameter] public bool ShowClearButton { get; set; } = true;

    /// <summary>Shows checkboxes next to items.</summary>
    [Parameter] public bool ShowCheckBox { get; set; }

    // ── Templates ────────────────────────────────────────────────

    /// <summary>Custom template for each item in the dropdown.</summary>
    [Parameter] public RenderFragment<TItem>? ItemTemplate { get; set; }

    /// <summary>Custom template for each selected value chip.</summary>
    [Parameter] public RenderFragment<TItem>? ValueTemplate { get; set; }

    /// <summary>Template shown when no items match.</summary>
    [Parameter] public RenderFragment? NoRecordsTemplate { get; set; }

    /// <summary>Popup header content.</summary>
    [Parameter] public RenderFragment? HeaderTemplate { get; set; }

    /// <summary>Popup footer content.</summary>
    [Parameter] public RenderFragment? FooterTemplate { get; set; }

    // ── Messages ─────────────────────────────────────────────────

    /// <summary>Loading message.</summary>
    [Parameter] public string? LoadingMessage { get; set; }

    /// <summary>Empty results message.</summary>
    [Parameter] public string? EmptyMessage { get; set; }

    /// <summary>Error message.</summary>
    [Parameter] public string? ErrorMessage { get; set; }

    // ── Callbacks ────────────────────────────────────────────────

    /// <summary>Fires when the dropdown opens.</summary>
    [Parameter] public EventCallback OnOpen { get; set; }

    /// <summary>Fires when the dropdown closes.</summary>
    [Parameter] public EventCallback OnClose { get; set; }

    /// <summary>Fires when the filter text changes.</summary>
    [Parameter] public EventCallback<string> OnFiltering { get; set; }

    // ── Internal State ───────────────────────────────────────────

    private bool _isOpen;
    private bool _isLoading;
    private bool _hasError;
    private string _filterText = string.Empty;
    private int _focusedIndex = -1;
    private List<TItem> _providerItems = [];
    private CancellationTokenSource? _debounceToken;
    private ElementReference _filterInputRef;

    // ── Computed ──────────────────────────────────────────────────

    private IReadOnlyList<TItem> SelectedItems
    {
        get
        {
            if (Values.Count == 0) return [];
            var source = DataProvider is not null ? _providerItems : Items;
            var comparer = EqualityComparer<TValue>.Default;
            var selectedSet = new HashSet<TValue>(Values, comparer);
            return source.Where(item => selectedSet.Contains(ValueField(item))).ToList();
        }
    }

    private bool IsSelected(TItem item)
    {
        var val = ValueField(item);
        return Values.Any(v => EqualityComparer<TValue>.Default.Equals(v, val));
    }

    private bool IsMaxReached => MaxSelectionCount > 0 && Values.Count >= MaxSelectionCount;

    private string GetItemId(TItem item)
    {
        if (IdField is not null) return IdField(item);
        return ValueField(item)?.ToString() ?? Guid.NewGuid().ToString();
    }

    private string GetTriggerCss()
    {
        var parts = new List<string>();
        if (Disabled) parts.Add("tm-multiselect--disabled");
        if (!string.IsNullOrEmpty(Error)) parts.Add("tm-multiselect--error");
        if (_isOpen) parts.Add("tm-multiselect--open");
        return string.Join(' ', parts);
    }

    // ── Visible Items ────────────────────────────────────────────

    private IEnumerable<TItem> GetVisibleItems()
    {
        var source = DataProvider is not null ? _providerItems : Items;
        IEnumerable<TItem> items = source;

        // Client-side text filter
        if (DataProvider is null && !string.IsNullOrWhiteSpace(_filterText))
        {
            var search = _filterText.ToLowerInvariant();
            items = items.Where(i => DisplayField(i).ToLowerInvariant().Contains(search));
        }

        // Hide already-selected
        if (HideSelectedItems && Values.Count > 0)
        {
            var selectedSet = new HashSet<TValue>(Values, EqualityComparer<TValue>.Default);
            items = items.Where(i => !selectedSet.Contains(ValueField(i)));
        }

        return items;
    }

    // ── Actions ──────────────────────────────────────────────────

    private async Task ToggleDropdownAsync()
    {
        if (Disabled) return;
        _isOpen = !_isOpen;
        if (_isOpen)
        {
            _filterText = string.Empty;
            _focusedIndex = -1;
            if (DataProvider is not null)
                await LoadFromProviderAsync();
            await OnOpen.InvokeAsync();
        }
        else
        {
            await OnClose.InvokeAsync();
        }
    }

    private async Task LoadFromProviderAsync()
    {
        _isLoading = true;
        _hasError = false;
        StateHasChanged();
        try
        {
            var req = new DropdownSearchRequest
            {
                SearchText = _filterText,
                Page = 1,
                PageSize = 50
            };
            var result = await DataProvider!.GetItemsAsync(req, CancellationToken.None);
            _providerItems = result.Items.ToList();
        }
        catch
        {
            _hasError = true;
            _providerItems.Clear();
        }
        finally
        {
            _isLoading = false;
            StateHasChanged();
        }
    }

    private async Task ToggleItemAsync(TItem item)
    {
        if (Disabled) return;
        var val = ValueField(item);
        var list = new List<TValue>(Values);

        if (IsSelected(item))
        {
            list.RemoveAll(v => EqualityComparer<TValue>.Default.Equals(v, val));
        }
        else
        {
            if (IsMaxReached) return;
            list.Add(val);
        }

        Values = list;
        await ValuesChanged.InvokeAsync(list);

        // In plain chip/delimiter modes (no checkboxes), close after selection
        if (!ShowCheckBox && Mode != MultiSelectMode.CheckBox)
            _isOpen = false;
    }

    private async Task RemoveItemAsync(TValue val)
    {
        if (Disabled) return;
        var list = Values.Where(v => !EqualityComparer<TValue>.Default.Equals(v, val)).ToList();
        Values = list;
        await ValuesChanged.InvokeAsync(list);
    }

    private async Task ClearAllAsync()
    {
        if (Disabled) return;
        Values = [];
        await ValuesChanged.InvokeAsync(Array.Empty<TValue>());
    }

    private async Task SelectAllAsync()
    {
        if (Disabled) return;
        var allValues = GetVisibleItems().Select(ValueField).ToList();
        if (MaxSelectionCount > 0)
            allValues = allValues.Take(MaxSelectionCount).ToList();
        Values = allValues;
        await ValuesChanged.InvokeAsync(allValues);
    }

    private Task DeselectAllAsync() => ClearAllAsync();

    // ── Filtering ────────────────────────────────────────────────

    private async Task HandleFilterInputAsync(ChangeEventArgs e)
    {
        _filterText = e.Value?.ToString() ?? string.Empty;
        _focusedIndex = -1;
        await OnFiltering.InvokeAsync(_filterText);

        if (DataProvider is not null)
        {
            _debounceToken?.Cancel();
            _debounceToken = new CancellationTokenSource();
            try
            {
                await Task.Delay(Debounce, _debounceToken.Token);
                await LoadFromProviderAsync();
            }
            catch (TaskCanceledException) { /* debounce cancelled */ }
        }
    }

    // ── Keyboard ─────────────────────────────────────────────────

    private async Task HandleTriggerKeyDownAsync(KeyboardEventArgs e)
    {
        switch (e.Key)
        {
            case "Enter" or " ":
                await ToggleDropdownAsync();
                break;
            case "Escape" when _isOpen:
                _isOpen = false;
                await OnClose.InvokeAsync();
                break;
            case "Backspace" when Values.Count > 0 && !_isOpen:
                await RemoveItemAsync(Values[^1]);
                break;
            case "ArrowDown" when !_isOpen:
                await ToggleDropdownAsync();
                break;
        }
    }

    private async Task HandleKeyDownAsync(KeyboardEventArgs e)
    {
        var items = GetVisibleItems().ToList();
        switch (e.Key)
        {
            case "Escape":
                _isOpen = false;
                await OnClose.InvokeAsync();
                break;
            case "ArrowDown":
                _focusedIndex = Math.Min(_focusedIndex + 1, items.Count - 1);
                break;
            case "ArrowUp":
                _focusedIndex = Math.Max(_focusedIndex - 1, 0);
                break;
            case "Enter" when _focusedIndex >= 0 && _focusedIndex < items.Count:
                await ToggleItemAsync(items[_focusedIndex]);
                break;
            case "Backspace" when string.IsNullOrEmpty(_filterText) && Values.Count > 0:
                await RemoveItemAsync(Values[^1]);
                break;
        }
    }
}
