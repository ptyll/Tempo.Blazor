using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Tempo.Blazor.Components.Spreadsheet.Models;

namespace Tempo.Blazor.Components.Spreadsheet.Dialogs;

/// <summary>
/// Dialog for inserting or editing a structured hyperlink on a cell.
/// Supports four link kinds: Web, Email, InternalRef, NamedRange.
/// </summary>
public partial class TmSpreadsheetHyperlinkDialog
{
    private SpreadsheetHyperlinkKind _kind = SpreadsheetHyperlinkKind.Web;
    private string _target = string.Empty;
    private string? _display;
    private string? _tooltip;
    private string? _emailSubject;

    /// <summary>The workbook (used for named range choices).</summary>
    [Parameter, EditorRequired] public SpreadsheetWorkbook Workbook { get; set; } = null!;

    /// <summary>The existing hyperlink to edit, or null for a new one.</summary>
    [Parameter] public SpreadsheetHyperlink? Hyperlink { get; set; }

    /// <summary>Raised when the user saves the dialog.</summary>
    [Parameter] public EventCallback<SpreadsheetHyperlink> OnSave { get; set; }

    /// <summary>Raised when the dialog is dismissed.</summary>
    [Parameter] public EventCallback OnCancel { get; set; }

    protected override void OnParametersSet()
    {
        if (Hyperlink is not null)
        {
            _kind = Hyperlink.Kind;
            _target = Hyperlink.Target;
            _display = Hyperlink.Display;
            _tooltip = Hyperlink.Tooltip;
            _emailSubject = Hyperlink.EmailSubject;
        }
        else
        {
            _kind = SpreadsheetHyperlinkKind.Web;
            _target = string.Empty;
            _display = null;
            _tooltip = null;
            _emailSubject = null;
        }
    }

    private void OnSaveClick()
    {
        var link = new SpreadsheetHyperlink
        {
            Kind = _kind,
            Target = _target.Trim(),
            Display = string.IsNullOrWhiteSpace(_display) ? null : _display.Trim(),
            Tooltip = string.IsNullOrWhiteSpace(_tooltip) ? null : _tooltip.Trim(),
            EmailSubject = _kind == SpreadsheetHyperlinkKind.Email && !string.IsNullOrWhiteSpace(_emailSubject)
                ? _emailSubject.Trim()
                : null
        };

        OnSave.InvokeAsync(link);
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
