using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Abstractions.Models;
using Tempo.Blazor.Helpers;
using Tempo.Blazor.Interfaces;

namespace Tempo.Blazor.Components.Inputs;

/// <summary>
/// A combo box that displays a multi-column grid in its dropdown for rich item selection.
/// </summary>
public partial class TmMultiColumnComboBox<TItem, TValue>
{
    private bool _isOpen;
    private ElementReference _rootRef;
    private ElementReference _triggerRef;
    private string _filterText = string.Empty;
    private readonly List<MultiColumnComboBoxColumn<TItem>> _columns = [];
    private IReadOnlyList<TItem> _filteredItems = [];
    private List<TItem> _recentItems = [];
    private readonly List<TItem> _createdItems = [];

    // ── Parameters ───────────────────────────────────────────────

    /// <summary>Selected value.</summary>
    [Parameter] public TValue? Value { get; set; }

    /// <summary>Fires when the selection changes.</summary>
    [Parameter] public EventCallback<TValue?> ValueChanged { get; set; }

    /// <summary>Data source for the dropdown grid.</summary>
    [Parameter] public IReadOnlyList<TItem> Data { get; set; } = [];

    /// <summary>Expression that returns the value for a given item. Required for selection matching.</summary>
    [Parameter] public Func<TItem, TValue> ValueField { get; set; } = default!;

    /// <summary>Expression that returns the display text for the trigger when an item is selected.</summary>
    [Parameter] public Func<TItem, string> TextField { get; set; } = default!;

    /// <summary>Placeholder shown when no value is selected.</summary>
    [Parameter] public string? Placeholder { get; set; }

    /// <summary>Whether the user can filter the dropdown list. Default is <c>true</c>.</summary>
    [Parameter] public bool Filterable { get; set; } = true;

    /// <summary>Placeholder for the filter input.</summary>
    [Parameter] public string? FilterPlaceholder { get; set; }

    /// <summary>
    /// When true (default), filtering ignores diacritics via FormD normalization —
    /// e.g. "usti" matches "Ústí" and "práha" matches "Praha". Set to false for
    /// accent-sensitive (but still case-insensitive) filtering.
    /// </summary>
    [Parameter] public bool AccentInsensitiveFilter { get; set; } = true;

    /// <summary>Whether to show a clear button. Default is <c>true</c>.</summary>
    [Parameter] public bool ShowClearButton { get; set; } = true;

    /// <summary>Disables the component.</summary>
    [Parameter] public bool Disabled { get; set; }

    /// <summary>Additional CSS class.</summary>
    [Parameter] public string? Class { get; set; }

    /// <summary>Additional attributes spread onto the root element.</summary>
    [Parameter(CaptureUnmatchedValues = true)] public Dictionary<string, object>? AdditionalAttributes { get; set; }

    /// <summary>Column definitions for the dropdown grid.</summary>
    [Parameter] public IReadOnlyList<MultiColumnComboBoxColumn<TItem>> Columns { get; set; } = [];

    /// <summary>Enables multi-select mode: selections are tracked via <see cref="SelectedValues"/> and rendered as chips in the trigger. Default <c>false</c>.</summary>
    [Parameter] public bool MultiSelect { get; set; }

    /// <summary>The selected values in multi-select mode (source of truth when <see cref="MultiSelect"/> is <c>true</c>).</summary>
    [Parameter] public IReadOnlyList<TValue> SelectedValues { get; set; } = [];

    /// <summary>Fires when the multi-select selection changes.</summary>
    [Parameter] public EventCallback<IReadOnlyList<TValue>> SelectedValuesChanged { get; set; }

    /// <summary>Shows an inline "create new" row that calls <see cref="DataProvider"/>.CreateAsync. Requires <see cref="DataProvider"/>. Default <c>false</c>.</summary>
    [Parameter] public bool AllowCreateNew { get; set; }

    /// <summary>Shows a "recent" group loaded from <see cref="DataProvider"/>.GetRecentAsync when the dropdown opens. Requires <see cref="DataProvider"/>. Default <c>false</c>.</summary>
    [Parameter] public bool ShowRecent { get; set; }

    /// <summary>Optional data provider that powers the create-new and recent-items features (opt-in). The grid still binds to <see cref="Data"/>.</summary>
    [Parameter] public IDropdownDataProvider<TItem>? DataProvider { get; set; }

    // ── Lifecycle ────────────────────────────────────────────────

    /// <inheritdoc />
    protected override void OnParametersSet()
    {
        _columns.Clear();
        if (Columns is not null)
            _columns.AddRange(Columns);
        _filteredItems = ApplyFilter(Data, _filterText);
    }

    // ── Interaction ──────────────────────────────────────────────

    private async Task ToggleAsync()
    {
        if (Disabled) return;
        _isOpen = !_isOpen;
        if (_isOpen)
        {
            _filteredItems = ApplyFilter(Data, _filterText);
            if (ShowRecent && DataProvider is not null && _recentItems.Count == 0)
            {
                await LoadRecentAsync();
            }
        }
    }

    /// <summary>JS-driven dismissal (Escape or outside pointerdown from overlay.js).</summary>
    private void SetOpen(bool open) => _isOpen = open;

    private async Task SelectItemAsync(TItem item)
    {
        if (Disabled || ValueField is null) return;

        var value = ValueField(item);

        if (MultiSelect)
        {
            var list = SelectedValues.ToList();
            var existing = list.FindIndex(v => EqualityComparer<TValue>.Default.Equals(v, value));
            if (existing >= 0)
            {
                list.RemoveAt(existing);
            }
            else
            {
                list.Add(value);
            }
            await SelectedValuesChanged.InvokeAsync(list);
            // Keep the dropdown open so the user can toggle multiple items.
        }
        else
        {
            await ValueChanged.InvokeAsync(value);
            _isOpen = false;
        }
    }

    private async Task RemoveValueAsync(TValue value)
    {
        if (Disabled) return;
        var list = SelectedValues.Where(v => !EqualityComparer<TValue>.Default.Equals(v, value)).ToList();
        await SelectedValuesChanged.InvokeAsync(list);
    }

    private async Task CreateNewAsync()
    {
        if (Disabled || DataProvider is null) return;

        var created = await DataProvider.CreateAsync(_filterText);
        if (created is null) return;

        _createdItems.Add(created);
        await SelectItemAsync(created);
        if (MultiSelect)
        {
            // Reset the query so the create row disappears and the new chip is visible.
            _filterText = string.Empty;
            _filteredItems = ApplyFilter(Data, _filterText);
        }
    }

    private async Task LoadRecentAsync()
    {
        try
        {
            var recent = await DataProvider!.GetRecentAsync();
            _recentItems = recent.ToList();
        }
        catch
        {
            _recentItems.Clear();
        }
        await InvokeAsync(StateHasChanged);
    }

    private async Task ClearValueAsync()
    {
        if (Disabled) return;
        if (MultiSelect)
        {
            await SelectedValuesChanged.InvokeAsync(Array.Empty<TValue>());
        }
        else
        {
            await ValueChanged.InvokeAsync(default);
        }
    }

    private async Task OnFilterChangedAsync(string text)
    {
        _filterText = text;
        _filteredItems = ApplyFilter(Data, _filterText);
        await InvokeAsync(StateHasChanged);
    }

    // ── Helpers ──────────────────────────────────────────────────

    private IReadOnlyList<TItem> ApplyFilter(IReadOnlyList<TItem> data, string filter)
    {
        if (string.IsNullOrWhiteSpace(filter) || _columns.Count == 0)
            return data;

        var term = filter.Trim();
        return data.Where(item => _columns.Any(col =>
        {
            var val = col.Field(item)?.ToString();
            if (val is null) return false;
            return AccentInsensitiveFilter
                ? AccentInsensitiveText.Contains(val, term)
                : val.Contains(term, StringComparison.OrdinalIgnoreCase);
        })).ToList();
    }

    private string GetDisplayText(TValue? value)
    {
        if (value is null || TextField is null || Data is null) return string.Empty;

        var item = Data.FirstOrDefault(i =>
        {
            if (ValueField is null) return false;
            var itemValue = ValueField(i);
            return EqualityComparer<TValue>.Default.Equals(itemValue, value);
        });

        return item is not null ? TextField(item) : value.ToString() ?? string.Empty;
    }

    private bool IsEmptyValue(TValue? value) =>
        EqualityComparer<TValue?>.Default.Equals(value, default);

    private bool IsItemSelected(TItem item)
    {
        if (ValueField is null) return false;
        var itemValue = ValueField(item);

        if (MultiSelect)
        {
            return SelectedValues.Any(v => EqualityComparer<TValue>.Default.Equals(v, itemValue));
        }

        if (Value is null) return false;
        return EqualityComparer<TValue>.Default.Equals(itemValue, Value);
    }

    /// <summary>Whether the multi-select trigger currently has a non-empty selection.</summary>
    private bool HasSelection => MultiSelect ? SelectedValues.Count > 0 : Value is not null && !IsEmptyValue(Value);

    /// <summary>Whether the recent-items group should render in the dropdown.</summary>
    private bool RecentVisible => ShowRecent && _recentItems.Count > 0 && string.IsNullOrWhiteSpace(_filterText);

    /// <summary>Whether the inline create-new row should render.</summary>
    private bool ShowCreateOption => AllowCreateNew && DataProvider is not null && !string.IsNullOrWhiteSpace(_filterText);

    /// <summary>Resolves the items backing the current multi-select chips (from <see cref="Data"/> or created items).</summary>
    private IEnumerable<(TValue Value, string Text)> GetSelectedChips()
    {
        if (ValueField is null)
        {
            yield break;
        }

        foreach (var value in SelectedValues)
        {
            var item = FindItem(value);
            var text = item is not null && TextField is not null
                ? TextField(item)
                : value?.ToString() ?? string.Empty;
            yield return (value, text);
        }
    }

    private TItem? FindItem(TValue value)
    {
        if (ValueField is null) return default;

        foreach (var item in Data)
        {
            if (EqualityComparer<TValue>.Default.Equals(ValueField(item), value))
            {
                return item;
            }
        }
        foreach (var item in _createdItems)
        {
            if (EqualityComparer<TValue>.Default.Equals(ValueField(item), value))
            {
                return item;
            }
        }
        return default;
    }

    private string GetItemKey(TItem item)
    {
        if (ValueField is not null)
        {
            var val = ValueField(item);
            return val?.ToString() ?? item?.GetHashCode().ToString() ?? Guid.NewGuid().ToString();
        }
        return item?.GetHashCode().ToString() ?? Guid.NewGuid().ToString();
    }
}
