using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Tempo.Blazor.Components.NotionEditor.Services;
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
        // Stable across a name, but a token — a stored hex would pin the avatar off the theme.
        var names = new[]
        {
            "blue", "green", "yellow", "red", "purple",
            "gray", "orange", "brown", "pink", "blue"
        };
        var idx = TmNotionDbPropertyValue.StableIndex(seed, names.Length);
        return NotionColorTokens.OptionSolid(names[idx]);
    }

    /// <summary>A chip background: the option token, or the muted surface when the option has none.</summary>
    protected static string ChipBackground(string? color) =>
        string.IsNullOrEmpty(color)
            ? "var(--tm-bg-muted)"
            : NotionColorTokens.OptionWash(color);

    protected static string ChipForeground(string? color) =>
        string.IsNullOrEmpty(color)
            ? "var(--tm-text-secondary)"
            : NotionColorTokens.OptionInk(color);

    protected static string Initials(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "?";
        var parts = name.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2
            ? $"{char.ToUpper(parts[0][0])}{char.ToUpper(parts[^1][0])}"
            : name.Length >= 2 ? name[..2].ToUpper() : name.ToUpper();
    }
}
