using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.Components.Spreadsheet.Data;
using Tempo.Blazor.Components.Icons;

namespace Tempo.Blazor.Components.Spreadsheet.Dialogs;

/// <summary>
/// The Spreadsheet validation alert, shared by the Stop-style error (a hard block: only the primary
/// action or Escape dismisses it) and the Warning / Information confirm (which also cancels on a
/// backdrop click or the cancel action).
/// </summary>
/// <remarks>
/// The dialog contract — <c>role="alertdialog"</c>, <c>aria-modal</c>, the accessible name (the
/// title) and description (the message), the initial focus on the primary action, the document-level
/// Escape and the focus restore on close — comes from the core <c>TmModal</c> + <c>TmFocusScope</c>;
/// the package only decides the severity, the words and whether a cancel path exists. Every element
/// id is per-instance, so two alerts (or two spreadsheets) on one page never collide.
/// </remarks>
public partial class TmSpreadsheetValidationAlert
{
    // Per-instance id suffix: the ids below are implementation detail, never a selector contract.
    private readonly string _idSuffix = Guid.NewGuid().ToString("N")[..8];

    private string MessageDomId => $"spreadsheet-validation-alert-message-{_idSuffix}";

    private string PrimaryActionDomId => $"spreadsheet-validation-alert-primary-{_idSuffix}";

    private string CancelActionDomId => $"spreadsheet-validation-alert-cancel-{_idSuffix}";

    /// <summary>
    /// How severe the message is. <see cref="SpreadsheetValidationErrorStyle.Stop"/> blocks the value
    /// (a single acknowledge action, a backdrop click does nothing); Warning and Information ask
    /// whether the value should be kept.
    /// </summary>
    [Parameter] public SpreadsheetValidationErrorStyle Severity { get; set; } = SpreadsheetValidationErrorStyle.Stop;

    /// <summary>
    /// The alert title (the rule's own title). Null falls back to the severity's localized default
    /// ("Invalid value" / "Value outside the recommended range" / "Note").
    /// </summary>
    [Parameter] public string? Title { get; set; }

    /// <summary>
    /// The alert body text (the rule's own message). Null falls back to the severity's localized
    /// default.
    /// </summary>
    [Parameter] public string? Message { get; set; }

    /// <summary>Label of the primary action. Null falls back to the severity's localized default.</summary>
    [Parameter] public string? PrimaryText { get; set; }

    /// <summary>Label of the cancel action. Null falls back to the localized "Cancel".</summary>
    [Parameter] public string? CancelText { get; set; }

    /// <summary>
    /// Whether a cancel action exists. Null falls back to the severity: a Stop alert has none (it is
    /// a hard block), a Warning or Information confirm always offers one.
    /// </summary>
    [Parameter] public bool? ShowCancel { get; set; }

    /// <summary>Whether closing the alert restores the focus it took. Defaults to true.</summary>
    [Parameter] public bool RestoreFocus { get; set; } = true;

    /// <summary>
    /// The id of the element focus returns to when the alert closes — the host spreadsheet passes its
    /// grid root, so focus lands on the grid rather than the toolbar button that opened the alert.
    /// Null falls back to the element focused before the alert opened.
    /// </summary>
    [Parameter] public string? RestoreFocusTargetId { get; set; }

    /// <summary>
    /// The layout the alert renders. <see cref="TmLayoutMode.Auto"/> follows the viewport scope; a
    /// forced value pins the mode without measuring, so a test can render the mobile sheet.
    /// </summary>
    [Parameter] public TmLayoutMode LayoutMode { get; set; } = TmLayoutMode.Auto;

    /// <summary>Raised when the user accepts the alert.</summary>
    [Parameter] public EventCallback OnPrimary { get; set; }

    /// <summary>Raised when the user cancels the alert (the cancel action or a backdrop click).</summary>
    [Parameter] public EventCallback OnCancel { get; set; }

    private bool IsStop => Severity == SpreadsheetValidationErrorStyle.Stop;

    private string RootClass => Severity switch
    {
        SpreadsheetValidationErrorStyle.Warning => "tm-spreadsheet-alert tm-spreadsheet-alert--warning",
        SpreadsheetValidationErrorStyle.Information => "tm-spreadsheet-alert tm-spreadsheet-alert--information",
        _ => "tm-spreadsheet-alert tm-spreadsheet-alert--stop"
    };

    /// <summary>Each severity paints its own icon; only the Stop style blocks.</summary>
    private string IconName => Severity switch
    {
        SpreadsheetValidationErrorStyle.Warning => IconNames.AlertTriangle,
        SpreadsheetValidationErrorStyle.Information => IconNames.Info,
        _ => IconNames.AlertCircle
    };

    // A rule that carries an EMPTY title/message (the Data validation dialog seeds both as "") must
    // still explain itself: an empty string would render no title at all, which also breaks the
    // frame's aria-labelledby (it would point at a missing element).
    private string ResolvedTitle => string.IsNullOrWhiteSpace(Title) ? Severity switch
    {
        SpreadsheetValidationErrorStyle.Warning => Loc["TmSpreadsheet_Validation_DefaultWarningTitle"],
        SpreadsheetValidationErrorStyle.Information => Loc["TmSpreadsheet_Validation_DefaultInfoTitle"],
        _ => Loc["TmSpreadsheet_Validation_DefaultErrorTitle"]
    } : Title;

    private string ResolvedMessage => string.IsNullOrWhiteSpace(Message) ? Severity switch
    {
        SpreadsheetValidationErrorStyle.Warning => Loc["TmSpreadsheet_Validation_DefaultWarningMessage"],
        SpreadsheetValidationErrorStyle.Information => Loc["TmSpreadsheet_Validation_DefaultInfoMessage"],
        _ => Loc["TmSpreadsheet_Validation_DefaultErrorMessage"]
    } : Message;

    private bool ResolvedShowCancel => ShowCancel ?? !IsStop;

    /// <summary>
    /// A Stop alert offers the single acknowledge action; the confirm styles offer "Cancel" plus the
    /// "use the value anyway" action.
    /// </summary>
    private string ResolvedPrimaryText => PrimaryText ?? (IsStop
        ? Loc["TmSpreadsheet_Validation_Acknowledge"]
        : Loc["TmSpreadsheet_Validation_KeepValue"]);

    private string ResolvedCancelText => CancelText ?? Loc["Tm_Cancel"];

    /// <summary>
    /// Escape dismisses the alert. A Stop alert is a hard block, so Escape takes the SAME path as the
    /// acknowledge action (the value stays rejected); a Warning/Information confirm cancels instead.
    /// </summary>
    private Task OnDismiss() => IsStop ? OnPrimary.InvokeAsync() : OnCancel.InvokeAsync();

    private Task OnPrimaryAction() => OnPrimary.InvokeAsync();

    private Task OnCancelAction() => OnCancel.InvokeAsync();
}
