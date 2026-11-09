using System.Collections;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Tempo.Blazor.NotionEditor.Models;

namespace Tempo.Blazor.Components.NotionEditor.Blocks.Database.Cells;

public partial class TmNotionDbCellMultiSelect : TmNotionDbCellBase
{
    private readonly HashSet<string> _current  = [];
    private string         _search   = string.Empty;
    private ElementReference  _searchRef;

    private IReadOnlyList<SelectFieldOption> AllOptions =>
        (Field.Config as ISelectFieldConfig)?.Options ?? [];

    private IEnumerable<SelectFieldOption> FilteredOptions =>
        _search.Length == 0
            ? AllOptions
            : AllOptions.Where(o => o.Name.Contains(_search, StringComparison.OrdinalIgnoreCase));

    private SelectFieldOption? GetOption(string name) =>
        AllOptions.FirstOrDefault(o => string.Equals(o.Name, name, StringComparison.OrdinalIgnoreCase));

    protected override void OnStartEdit()
    {
        _search = string.Empty;
        _current.Clear();
        foreach (var v in ParseTags(Value)) _current.Add(v);
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (IsEditing) try { await _searchRef.FocusAsync(); } catch { }
    }

    private async Task HandleClickAsync()
    {
        if (!ReadOnly) await RequestEditAsync();
    }

    private void Toggle(string name)
    {
        if (!_current.Remove(name)) _current.Add(name);
        StateHasChanged();
    }

    private async Task ApplyAsync() => await CommitAsync(_current.ToArray());

    private async Task HandlePopupKeyAsync(KeyboardEventArgs e)
    {
        if (e.Key == "Escape") await CancelAsync();
        else if (e.Key == "Enter") await ApplyAsync();
    }

    private IReadOnlyList<string> ReadTags() => ParseTags(Value).ToList();

    private static IEnumerable<string> ParseTags(object? value) => value switch
    {
        string[] arr             => arr,
        IEnumerable<string> list => list,
        string s when s.Length > 0 => s.Split(',', StringSplitOptions.RemoveEmptyEntries),
        // The API round-trips a MultiSelect as a JSON array; the demo client normalizes it to an
        // object[] (F-04/F-05), and a bare JsonElement arrives from hosts that skip normalization.
        System.Text.Json.JsonElement { ValueKind: System.Text.Json.JsonValueKind.Array } json
            => json.EnumerateArray().Select(e => e.ValueKind == System.Text.Json.JsonValueKind.String
                ? e.GetString() ?? string.Empty
                : e.ToString()),
        System.Text.Json.JsonElement { ValueKind: System.Text.Json.JsonValueKind.String } json
            when (json.GetString() ?? string.Empty).Length > 0
            => (json.GetString() ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries),
        IEnumerable many         => many.Cast<object?>()
                                          .Select(v => v?.ToString() ?? string.Empty)
                                          .Where(s => s.Length > 0),
        _                        => []
    };
}
