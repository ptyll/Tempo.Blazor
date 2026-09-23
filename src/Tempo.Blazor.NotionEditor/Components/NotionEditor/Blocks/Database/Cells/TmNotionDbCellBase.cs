using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Tempo.Blazor.NotionEditor.Models;

namespace Tempo.Blazor.Components.NotionEditor.Blocks.Database.Cells;

/// <summary>
/// Base class for Notion database cell renderers. Each concrete cell (<c>TmNotionDbCellText</c>,
/// <c>TmNotionDbCellNumber</c>, …) inherits the shared display/edit contract declared here, so the
/// parameters below are documented once instead of repeated on every cell's page.
/// </summary>
public abstract class TmNotionDbCellBase : ComponentBase
{
    /// <summary>The database field (column) this cell renders — required; supplies name, type and metadata.</summary>
    [Parameter, EditorRequired]
    public IDatabaseField Field { get; set; } = default!;

    /// <summary>The raw cell value, typed per the field's data kind (string, number, option id, …).</summary>
    [Parameter]
    public object? Value { get; set; }

    /// <summary>When true the cell renders display-only and never enters edit mode.</summary>
    [Parameter]
    public bool ReadOnly { get; set; }

    /// <summary>When true the cell shows its inline editor instead of the read view.</summary>
    [Parameter]
    public bool IsEditing { get; set; }

    /// <summary>Raised when the user confirms an edit; carries the new cell value.</summary>
    [Parameter]
    public EventCallback<object?> OnCommit { get; set; }

    /// <summary>Raised when the user cancels the edit (Escape, focus loss without commit).</summary>
    [Parameter]
    public EventCallback OnCancel { get; set; }

    /// <summary>Raised when the user asks to edit the cell (click/double-click in read mode).</summary>
    [Parameter]
    public EventCallback OnEditRequested { get; set; }

    private bool _wasEditing;

    protected override void OnParametersSet()
    {
        if (IsEditing && !_wasEditing)
            OnStartEdit();
        _wasEditing = IsEditing;
    }

    protected virtual void OnStartEdit() { }

    protected string StringValue => Value?.ToString() ?? string.Empty;

    protected async Task CommitAsync(object? value) => await OnCommit.InvokeAsync(value);
    protected async Task CancelAsync()              => await OnCancel.InvokeAsync();
    protected async Task RequestEditAsync()         => await OnEditRequested.InvokeAsync();

    protected async Task HandleKeyAsync(KeyboardEventArgs e, Func<Task> onCommit)
    {
        switch (e.Key)
        {
            case "Enter": await onCommit();    break;
            case "Escape": await CancelAsync(); break;
        }
    }

    protected static string AvatarColor(string seed)
    {
        var colors = new[]
        {
            "#3b82f6","#10b981","#f59e0b","#ef4444","#8b5cf6",
            "#06b6d4","#f97316","#84cc16","#ec4899","#14b8a6"
        };
        var idx = Math.Abs(seed.GetHashCode()) % colors.Length;
        return colors[idx];
    }

    protected static string Initials(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "?";
        var parts = name.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2
            ? $"{char.ToUpper(parts[0][0])}{char.ToUpper(parts[^1][0])}"
            : name.Length >= 2 ? name[..2].ToUpper() : name.ToUpper();
    }
}
