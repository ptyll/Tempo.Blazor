using Tempo.Blazor.Models;
using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Components.Spreadsheet.Data;
using Tempo.Blazor.Components.Spreadsheet.Models;
using Tempo.Blazor.Abstractions.Layout;

namespace Tempo.Blazor.Components.Spreadsheet;

public partial class TmSpreadsheetDataValidationDialog
{
    [Parameter] public SpreadsheetDataValidation? Validation { get; set; }
    [Parameter] public EventCallback<SpreadsheetDataValidation> OnSave { get; set; }
    [Parameter] public EventCallback OnClose { get; set; }

    /// <summary>
    /// The id of the element focus returns to on close — the host spreadsheet passes its grid root so
    /// focus lands on the grid. Null falls back to the element focused before the dialog opened.
    /// </summary>
    [Parameter] public string? RestoreFocusTargetId { get; set; }

    /// <summary>
    /// The layout the dialog renders. <see cref="TmLayoutMode.Auto"/> follows the viewport scope; a
    /// forced value pins the mode without measuring, so a test can render the mobile sheet.
    /// </summary>
    [Parameter] public TmLayoutMode LayoutMode { get; set; } = TmLayoutMode.Auto;

    private readonly string _idSuffix = Guid.NewGuid().ToString("N")[..8];

    private string TypeSelectDomId => $"spreadsheet-dv-type-{_idSuffix}";
    private string OperatorSelectDomId => $"spreadsheet-dv-operator-{_idSuffix}";
    private string Formula1DomId => $"spreadsheet-dv-formula1-{_idSuffix}";
    private string Formula2DomId => $"spreadsheet-dv-formula2-{_idSuffix}";
    private string InputTitleDomId => $"spreadsheet-dv-input-title-{_idSuffix}";
    private string InputMessageDomId => $"spreadsheet-dv-input-message-{_idSuffix}";
    private string ErrorStyleDomId => $"spreadsheet-dv-error-style-{_idSuffix}";
    private string ErrorTitleDomId => $"spreadsheet-dv-error-title-{_idSuffix}";
    private string ErrorMessageDomId => $"spreadsheet-dv-error-message-{_idSuffix}";

    private IReadOnlyList<SelectOption<SpreadsheetValidationType>> TypeOptions =>
        _allTypes.Select(t => new SelectOption<SpreadsheetValidationType>(t, Loc[$"TmSpreadsheet_Validation_Type_{t}"])).ToList();

    private IReadOnlyList<SelectOption<SpreadsheetValidationOperator>> OperatorOptions =>
        _allOperators.Select(op => new SelectOption<SpreadsheetValidationOperator>(op, Loc[$"TmSpreadsheet_Validation_Op_{op}"])).ToList();

    private IReadOnlyList<SelectOption<SpreadsheetValidationErrorStyle>> ErrorStyleOptions =>
    [
        new(SpreadsheetValidationErrorStyle.Stop, Loc["TmSpreadsheet_Validation_Style_Stop"]),
        new(SpreadsheetValidationErrorStyle.Warning, Loc["TmSpreadsheet_Validation_Style_Warning"]),
        new(SpreadsheetValidationErrorStyle.Information, Loc["TmSpreadsheet_Validation_Style_Information"])
    ];

    private string _activeTab = "Settings";

    private SpreadsheetValidationType _validationType = SpreadsheetValidationType.Any;
    private SpreadsheetValidationOperator _operator = SpreadsheetValidationOperator.Between;
    private string _formula1 = string.Empty;
    private string _formula2 = string.Empty;
    private bool _allowBlank = true;
    private bool _showDropDown = true;

    private bool _showInputMessage;
    private string _inputTitle = string.Empty;
    private string _inputMessage = string.Empty;

    private bool _showErrorAlert = true;
    private SpreadsheetValidationErrorStyle _errorStyle = SpreadsheetValidationErrorStyle.Stop;
    private string _errorTitle = string.Empty;
    private string _errorMessage = string.Empty;

    private static readonly SpreadsheetValidationType[] _allTypes =
    [
        SpreadsheetValidationType.Any,
        SpreadsheetValidationType.Whole,
        SpreadsheetValidationType.Decimal,
        SpreadsheetValidationType.List,
        SpreadsheetValidationType.Date,
        SpreadsheetValidationType.Time,
        SpreadsheetValidationType.TextLength,
        SpreadsheetValidationType.Custom
    ];

    private static readonly SpreadsheetValidationOperator[] _allOperators =
    [
        SpreadsheetValidationOperator.Between,
        SpreadsheetValidationOperator.NotBetween,
        SpreadsheetValidationOperator.Equal,
        SpreadsheetValidationOperator.NotEqual,
        SpreadsheetValidationOperator.GreaterThan,
        SpreadsheetValidationOperator.LessThan,
        SpreadsheetValidationOperator.GreaterOrEqual,
        SpreadsheetValidationOperator.LessOrEqual
    ];

    protected override void OnParametersSet()
    {
        var v = Validation;
        if (v is null) return;
        _validationType = v.Type;
        _operator = v.Operator;
        _formula1 = v.Formula1 ?? string.Empty;
        _formula2 = v.Formula2 ?? string.Empty;
        _allowBlank = v.AllowBlank;
        _showDropDown = v.ShowDropDown;
        _showInputMessage = v.InputMessage is not null;
        _inputTitle = v.InputMessage?.Title ?? string.Empty;
        _inputMessage = v.InputMessage?.Message ?? string.Empty;
        // A NEW rule (nothing configured yet) starts with the error alert ON: Excel's default, and
        // otherwise a user who picks the Warning style would silently get a rule with no ErrorAlert,
        // which the engine treats as Stop. An existing rule that deliberately has no alert keeps it off.
        _showErrorAlert = v.ErrorAlert is not null || IsBlankRule(v);
        _errorStyle = v.ErrorAlert?.Style ?? SpreadsheetValidationErrorStyle.Stop;
        _errorTitle = v.ErrorAlert?.Title ?? string.Empty;
        _errorMessage = v.ErrorAlert?.Message ?? string.Empty;
    }

    /// <summary>A rule the dialog has never configured — the state the host passes for a new rule.</summary>
    private static bool IsBlankRule(SpreadsheetDataValidation rule)
        => rule.Type == SpreadsheetValidationType.Any
            && string.IsNullOrEmpty(rule.Formula1)
            && rule.ErrorAlert is null
            && rule.InputMessage is null;

    private void OnTypeChanged(SpreadsheetValidationType type) => _validationType = type;

    private void OnOperatorChanged(SpreadsheetValidationOperator op) => _operator = op;

    private void OnErrorStyleChanged(SpreadsheetValidationErrorStyle style) => _errorStyle = style;

    private async Task OnApply()
    {
        var result = new SpreadsheetDataValidation
        {
            Range = Validation?.Range ?? new SpreadsheetRange(0, 0, 0, 0),
            Type = _validationType,
            Operator = _operator,
            Formula1 = string.IsNullOrWhiteSpace(_formula1) ? null : _formula1.Trim(),
            Formula2 = string.IsNullOrWhiteSpace(_formula2) ? null : _formula2.Trim(),
            AllowBlank = _allowBlank,
            ShowDropDown = _showDropDown,
            InputMessage = _showInputMessage && (!string.IsNullOrEmpty(_inputTitle) || !string.IsNullOrEmpty(_inputMessage))
                ? new SpreadsheetInputMessage { Title = _inputTitle, Message = _inputMessage }
                : null,
            ErrorAlert = _showErrorAlert
                ? new SpreadsheetValidationErrorAlert { Style = _errorStyle, Title = _errorTitle, Message = _errorMessage }
                : null
        };

        await OnSave.InvokeAsync(result);
    }

    private async Task OnCancel() => await OnClose.InvokeAsync();
}
