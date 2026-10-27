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
    // Per-instance title id (never a selector); the form-field ids below predate this phase and stay
    // constant because the published E2E lane keys off them (#hl-type, #hl-target, …).
    private readonly string _idSuffix = Guid.NewGuid().ToString("N")[..8];

    private string TitleDomId => $"spreadsheet-hyperlink-title-{_idSuffix}";

    private string TargetInputDomId => "hl-target";

    private SpreadsheetHyperlinkKind _kind = SpreadsheetHyperlinkKind.Web;
    private string _target = string.Empty;
    private string? _display;
    private string? _tooltip;
    private string? _emailSubject;
    private bool _seeded;
    private SpreadsheetHyperlink? _seededFor;

    /// <summary>The workbook (used for named range choices).</summary>
    [Parameter, EditorRequired] public SpreadsheetWorkbook Workbook { get; set; } = null!;

    /// <summary>The existing hyperlink to edit, or null for a new one.</summary>
    [Parameter] public SpreadsheetHyperlink? Hyperlink { get; set; }

    /// <summary>Raised when the user saves the dialog.</summary>
    [Parameter] public EventCallback<SpreadsheetHyperlink> OnSave { get; set; }

    /// <summary>Raised when the dialog is dismissed.</summary>
    [Parameter] public EventCallback OnCancel { get; set; }

    /// <summary>
    /// The id of the element focus returns to on close — the host spreadsheet passes its grid root so
    /// focus lands on the grid. Null falls back to the element focused before the dialog opened.
    /// </summary>
    [Parameter] public string? RestoreFocusTargetId { get; set; }

    protected override void OnParametersSet()
    {
        // Seed the form from the parameters ONCE per edit target: a Spreadsheet re-renders on its own
        // and re-sets this dialog's parameters with it, and re-seeding would wipe the address the user
        // is typing.
        if (_seeded && ReferenceEquals(_seededFor, Hyperlink))
        {
            return;
        }

        _seeded = true;
        _seededFor = Hyperlink;

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

    /// <summary>Raised by the focus scope's document-level Escape listener.</summary>
    private Task OnEscapeHandler() => OnCancel.InvokeAsync();

    private void OnKeyDown(KeyboardEventArgs e)
    {
        if (e.Key == "Enter" && e.CtrlKey)
            OnSaveClick();
    }
}
