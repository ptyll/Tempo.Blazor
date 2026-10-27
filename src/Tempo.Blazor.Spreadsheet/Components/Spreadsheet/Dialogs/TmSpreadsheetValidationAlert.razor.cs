using Microsoft.AspNetCore.Components;

namespace Tempo.Blazor.Components.Spreadsheet.Dialogs;

/// <summary>
/// The Spreadsheet validation alert, used for both the Stop-style error (a hard block: only the
/// primary action or Escape dismisses it) and the Warning/Information confirm (which also cancels
/// on a backdrop click).
/// </summary>
/// <remarks>
/// The dialog contract — <c>role="alertdialog"</c>, <c>aria-modal</c>, <c>aria-labelledby</c> /
/// <c>aria-describedby</c>, initial focus on the primary action, document-level Escape and the
/// focus restore on close — is delegated to the core <c>TmFocusScope</c>; the package only decides
/// the words and whether a cancel path exists. All element ids are per-instance, so two alerts (or
/// two spreadsheets) on one page never collide. Phase 3 of the redesign plan swaps the card for the
/// core <c>TmModal</c> and keeps this public surface.
/// </remarks>
public partial class TmSpreadsheetValidationAlert
{
    // Per-instance id suffix: the ids below are implementation detail, never a selector contract.
    private readonly string _idSuffix = Guid.NewGuid().ToString("N")[..8];

    private string TitleDomId => $"spreadsheet-validation-alert-title-{_idSuffix}";

    private string MessageDomId => $"spreadsheet-validation-alert-message-{_idSuffix}";

    private string PrimaryActionDomId => $"spreadsheet-validation-alert-primary-{_idSuffix}";

    private string CancelActionDomId => $"spreadsheet-validation-alert-cancel-{_idSuffix}";

    /// <summary>The alert title (the rule's own title, or the localized default).</summary>
    [Parameter] public string? Title { get; set; }

    /// <summary>The alert body text (the rule's own message, or the localized default).</summary>
    [Parameter] public string? Message { get; set; }

    /// <summary>
    /// True for the Stop style: the value is blocked, so a backdrop click does nothing and there is
    /// no cancel path — the alert only closes through its primary action or Escape.
    /// </summary>
    [Parameter] public bool IsStop { get; set; }

    /// <summary>Label of the primary action (Confirm / Yes).</summary>
    [Parameter] public string? PrimaryText { get; set; }

    /// <summary>Label of the cancel action. Only rendered when <see cref="ShowCancel"/> is true.</summary>
    [Parameter] public string? CancelText { get; set; }

    /// <summary>Whether a cancel action exists. False for a Stop alert, true for the confirm.</summary>
    [Parameter] public bool ShowCancel { get; set; }

    /// <summary>Whether closing the alert restores the focus it took. Defaults to true.</summary>
    [Parameter] public bool RestoreFocus { get; set; } = true;

    /// <summary>
    /// The id of the element focus returns to when the alert closes — the host spreadsheet passes its
    /// grid root, so focus lands on the grid rather than the toolbar button that opened the alert.
    /// Null falls back to the element focused before the alert opened.
    /// </summary>
    [Parameter] public string? RestoreFocusTargetId { get; set; }

    /// <summary>Raised when the user accepts the alert (the only close path of a Stop alert).</summary>
    [Parameter] public EventCallback OnPrimary { get; set; }

    /// <summary>Raised when the user cancels the alert (a backdrop click or the cancel action).</summary>
    [Parameter] public EventCallback OnCancel { get; set; }

    /// <summary>
    /// A Stop alert ignores the backdrop click: the value stays blocked, and the click only steals
    /// focus for a moment (the focus trap belongs to the scope, so Escape keeps working).
    /// </summary>
    private Task OnBackdropClick()
        => IsStop ? Task.CompletedTask : OnCancel.InvokeAsync();

    /// <summary>Escape closes the alert — the same path as the cancel action.</summary>
    private Task OnEscapeHandler()
        => IsStop ? OnPrimary.InvokeAsync() : OnCancel.InvokeAsync();
}
