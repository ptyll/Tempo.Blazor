using Microsoft.JSInterop;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Tempo.Blazor.Components.Spreadsheet.Models;

namespace Tempo.Blazor.Components.Spreadsheet.Dialogs;

/// <summary>
/// Dialog for creating or editing a named range. Validates name syntax and required fields.
/// </summary>
public partial class TmSpreadsheetNamedRangeEditDialog
{
    // Per-instance id suffix: ids this phase introduced are implementation detail and never a
    // selector. The form-field ids below predate this phase and stay constant because the published
    // E2E lane keys off them (#nr-name, #nr-refers, …) — one dialog is open at a time, so they cannot
    // collide.
    private readonly string _idSuffix = Guid.NewGuid().ToString("N")[..8];
    private readonly HashSet<string> _errors = [];

    private string _name = string.Empty;
    private string _refersTo = string.Empty;
    private NamedRangeScope _scope = NamedRangeScope.Workbook;
    private int _sheetIndex;
    private string? _comment;
    private bool _isNew;
    private bool _seeded;
    private SpreadsheetNamedRange? _seededFor;

    private ElementReference _nameInputRef;
    private ElementReference _refersToInputRef;
    private ElementReference? _pendingErrorFocus;

    private string TitleDomId => $"spreadsheet-named-range-edit-title-{_idSuffix}";

    private string NameErrorDomId => $"spreadsheet-named-range-edit-name-error-{_idSuffix}";

    private string RefersToErrorDomId => $"spreadsheet-named-range-edit-refers-error-{_idSuffix}";

    private string NameInputDomId => "nr-name";

    private string RefersToInputDomId => "nr-refers";

    private string ScopeSelectDomId => "nr-scope";

    private string SheetSelectDomId => "nr-sheet";

    private string CommentInputDomId => "nr-comment";

    /// <summary>Whether the name field is currently marked invalid (required or not a valid name).</summary>
    private bool HasNameError => _errors.Contains("name") || _errors.Contains("invalid");

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
        // Seed the form from the parameters ONCE per edit target. A Spreadsheet re-renders on its own
        // (canvas sync callbacks, status aggregation) and re-sets the dialog's parameters with it;
        // re-seeding on every parameter set would wipe what the user is typing mid-edit — which reads
        // as a Save that silently refuses to close because the fields came back empty.
        if (_seeded && ReferenceEquals(_seededFor, Range))
        {
            return;
        }

        _seeded = true;
        _seededFor = Range;
        _isNew = Range is null;
        _name = Range?.Name ?? string.Empty;
        _refersTo = Range?.RefersTo ?? string.Empty;
        _scope = Range?.Scope ?? NamedRangeScope.Workbook;
        _sheetIndex = Range?.SheetIndex ?? Workbook.ActiveSheetIndex;
        _comment = Range?.Comment;
        _errors.Clear();
    }

    /// <summary>After a failed save the first invalid field takes the focus, so the error is at hand.</summary>
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        await base.OnAfterRenderAsync(firstRender);

        var target = _pendingErrorFocus;
        _pendingErrorFocus = null;
        if (target is null)
        {
            return;
        }

        try
        {
            await target.Value.FocusAsync();
        }
        catch (Exception e) when (e is JSException or JSDisconnectedException or TaskCanceledException)
        {
            // The dialog may already be gone, or the circuit may be down.
        }
    }

    private void OnSaveClick()
    {
        _errors.Clear();

        // "Required" wins over "not valid": an empty name is missing, not malformed, so the two
        // messages never stack on the same field.
        if (string.IsNullOrWhiteSpace(_name))
        {
            _errors.Add("name");
        }
        else if (!SpreadsheetNamedRange.IsValidName(_name.Trim()))
        {
            _errors.Add("invalid");
        }

        if (string.IsNullOrWhiteSpace(_refersTo))
        {
            _errors.Add("refersTo");
        }

        if (_errors.Count > 0)
        {
            _pendingErrorFocus = HasNameError ? _nameInputRef : _refersToInputRef;
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

    /// <summary>Raised by the focus scope's document-level Escape listener.</summary>
    private Task OnEscapeHandler() => OnCancel.InvokeAsync();

    private void OnKeyDown(KeyboardEventArgs e)
    {
        if (e.Key == "Enter" && e.CtrlKey)
            OnSaveClick();
    }
}
