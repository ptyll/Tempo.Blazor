using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Tempo.Blazor.Components.Spreadsheet.Models;

namespace Tempo.Blazor.Components.Spreadsheet.Dialogs;

/// <summary>
/// Dialog for creating or editing a named range. Validates name syntax and required fields.
/// </summary>
public partial class TmSpreadsheetNamedRangeEditDialog
{
    private string _name = string.Empty;
    private string _refersTo = string.Empty;
    private NamedRangeScope _scope = NamedRangeScope.Workbook;
    private int _sheetIndex;
    private string? _comment;
    private readonly HashSet<string> _errors = [];
    private bool _isNew;

    /// <summary>The workbook containing the named range.</summary>
    [Parameter, EditorRequired] public SpreadsheetWorkbook Workbook { get; set; } = null!;

    /// <summary>The range to edit, or null when creating a new one.</summary>
    [Parameter] public SpreadsheetNamedRange? Range { get; set; }

    /// <summary>Raised when the user saves the dialog.</summary>
    [Parameter] public EventCallback<SpreadsheetNamedRange> OnSave { get; set; }

    /// <summary>Raised when the dialog is dismissed.</summary>
    [Parameter] public EventCallback OnCancel { get; set; }

    protected override void OnParametersSet()
    {
        _isNew = Range is null;
        _name = Range?.Name ?? string.Empty;
        _refersTo = Range?.RefersTo ?? string.Empty;
        _scope = Range?.Scope ?? NamedRangeScope.Workbook;
        _sheetIndex = Range?.SheetIndex ?? Workbook.ActiveSheetIndex;
        _comment = Range?.Comment;
        _errors.Clear();
    }

    private void OnSaveClick()
    {
        _errors.Clear();

        if (string.IsNullOrWhiteSpace(_name))
            _errors.Add("name");

        if (string.IsNullOrWhiteSpace(_refersTo))
            _errors.Add("refersTo");

        if (!SpreadsheetNamedRange.IsValidName(_name.Trim()))
        {
            _errors.Add("invalid");
        }

        if (_errors.Count > 0)
        {
            StateHasChanged();
            return;
        }

        var range = new SpreadsheetNamedRange
        {
            Name = _name.Trim(),
            RefersTo = _refersTo.Trim(),
            Scope = _scope,
            SheetIndex = _scope == NamedRangeScope.Sheet ? _sheetIndex : null,
            Comment = string.IsNullOrWhiteSpace(_comment) ? null : _comment.Trim()
        };

        OnSave.InvokeAsync(range);
    }

    private void OnCancelClick() => OnCancel.InvokeAsync();

    private async Task OnKeyDown(KeyboardEventArgs e)
    {
        if (e.Key == "Escape")
            await OnCancel.InvokeAsync();
        else if (e.Key == "Enter" && e.CtrlKey)
            OnSaveClick();
    }

    // The dialog's Ctrl+Enter -> save shortcut lives on the root; the chrome
    // buttons isolate their keydowns (stopPropagation) so Enter/Ctrl+Enter on
    // them runs only the button's native click. Escape is re-handled here so
    // it still cancels while focus sits on a button.
    private async Task OnChromeKeyDown(KeyboardEventArgs e)
    {
        if (e.Key == "Escape")
            await OnCancel.InvokeAsync();
    }
}
