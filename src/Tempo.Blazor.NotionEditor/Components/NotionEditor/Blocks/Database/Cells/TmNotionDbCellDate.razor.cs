using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Components.NotionEditor.Blocks.Database;

namespace Tempo.Blazor.Components.NotionEditor.Blocks.Database.Cells;

public partial class TmNotionDbCellDate : TmNotionDbCellBase
{
    private string        _buffer   = string.Empty;
    private ElementReference _inputRef;

    private bool IncludeTime =>
        (Field.Config as Tempo.Blazor.NotionEditor.Models.IDateFieldConfig)?.IncludeTime ?? false;

    private string InputType => IncludeTime ? "datetime-local" : "date";

    private string DisplayValue
    {
        get
        {
            if (Value is DateTime or DateTimeOffset or string)
            {
                var formatted = NotionDatabaseValueFormatter.Format(Value, IncludeTime ? "g" : "d");
                if (formatted.Length > 0) return formatted;
            }
            if (Value is string s && s.Length > 0) return s;
            return string.Empty;
        }
    }

    protected override void OnStartEdit()
    {
        _buffer = Value switch
        {
            DateTime dt => IncludeTime ? dt.ToString("yyyy-MM-ddTHH:mm") : dt.ToString("yyyy-MM-dd"),
            string s    => s,
            _           => string.Empty
        };
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (IsEditing) try { await _inputRef.FocusAsync(); } catch { }
    }

    private async Task CommitDateAsync()
    {
        object? val = DateTime.TryParse(_buffer, out var dt) ? dt : (object?)null;
        await CommitAsync(val);
    }
}
