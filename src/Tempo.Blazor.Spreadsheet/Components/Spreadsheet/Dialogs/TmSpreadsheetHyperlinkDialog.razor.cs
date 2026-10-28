using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Tempo.Blazor.Components.Spreadsheet.Models;
using Tempo.Blazor.Abstractions.Layout;

namespace Tempo.Blazor.Components.Spreadsheet.Dialogs;

/// <summary>
/// Dialog for inserting or editing a structured hyperlink on a cell.
/// Supports four link kinds: Web, Email, InternalRef, NamedRange.
/// </summary>
public partial class TmSpreadsheetHyperlinkDialog
{
    // The form-field ids predate the redesign and stay constant: the published E2E lane keys off
    // them (#hl-type, #hl-target, …) and one dialog is open at a time, so they cannot collide.
    private string TargetInputDomId => "hl-target";

    private string TypeSelectDomId => "hl-type";

    private string EmailSubjectDomId => "hl-subject";

    private string DisplayInputDomId => "hl-display";

    private string TooltipInputDomId => "hl-tooltip";

    /// <summary>A web address takes a URL keyboard on a phone, a mailbox an e-mail one.</summary>
    private string TargetInputMode => _kind switch
    {
        SpreadsheetHyperlinkKind.Email => "email",
        SpreadsheetHyperlinkKind.Web => "url",
        _ => "text"
    };

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
    /// The layout the dialog renders. <see cref="TmLayoutMode.Auto"/> follows the viewport scope; a
    /// forced value pins the mode without measuring, so a test can render the mobile sheet.
    /// </summary>
    [Parameter] public TmLayoutMode LayoutMode { get; set; } = TmLayoutMode.Auto;

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

    private void OnKeyDown(KeyboardEventArgs e)
    {
        if (e.Key == "Enter" && e.CtrlKey)
            OnSaveClick();
    }
}
