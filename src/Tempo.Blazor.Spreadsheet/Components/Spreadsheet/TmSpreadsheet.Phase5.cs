using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Tempo.Blazor.Components.Spreadsheet.Commands;
using Tempo.Blazor.Components.Spreadsheet.Data;
using Tempo.Blazor.Components.Spreadsheet.Models;
using Tempo.Blazor.Components.Spreadsheet.Rendering;

namespace Tempo.Blazor.Components.Spreadsheet;

/// <summary>
/// Phase 5 — Data Validation wiring: dialog, in-cell dropdown, "circle invalid data",
/// commit-path enforcement, and toolbar Data-tab buttons.
/// </summary>
public partial class TmSpreadsheet
{
    // ── Data Validation dialog ────────────────────────────────────────────────

    private bool _showValidationDialog;
    private SpreadsheetDataValidation? _pendingValidation;

    // ── Validation error / warning state ─────────────────────────────────────

    private bool _showValidationError;
    private string? _validationErrorTitle;
    private string? _validationErrorMessage;
    private SpreadsheetValidationErrorStyle _validationErrorStyle = SpreadsheetValidationErrorStyle.Stop;

    // Pending commit that needs user confirmation (Warning/Information style)
    private Func<Task>? _pendingConfirmedCommit;
    private bool _showValidationConfirm;
    private string? _validationConfirmTitle;
    private string? _validationConfirmMessage;
    private SpreadsheetValidationErrorStyle _validationConfirmStyle = SpreadsheetValidationErrorStyle.Warning;

    // ── In-cell validation dropdown ───────────────────────────────────────────

    private bool _showValidationDropdown;
    private double _validationDropdownX;
    private double _validationDropdownY;
    private int _validationDropdownRow;
    private int _validationDropdownCol;
    private IReadOnlyList<string> _validationDropdownItems = Array.Empty<string>();

    // ── Circle invalid data ───────────────────────────────────────────────────

    private bool _circlesVisible;

    // ── Open / close dialog ───────────────────────────────────────────────────

    private void OpenDataValidationDialog()
    {
        if (_workbook.ActiveSheet is null)
            return;

        var bounds = GetSelectionBounds();
        var existingRule = _workbook.ActiveSheet.DataValidations
            .FirstOrDefault(dv =>
                dv.Range.StartRow == bounds.StartRow &&
                dv.Range.StartCol == bounds.StartCol &&
                dv.Range.EndRow == bounds.EndRow &&
                dv.Range.EndCol == bounds.EndCol);

        _pendingValidation = existingRule?.DeepClone() ?? new SpreadsheetDataValidation
        {
            Range = new SpreadsheetRange(bounds.StartRow, bounds.StartCol, bounds.EndRow, bounds.EndCol)
        };

        _showValidationDialog = true;
        StateHasChanged();
    }

    private void ApplyDataValidation(SpreadsheetDataValidation validation)
    {
        _showValidationDialog = false;
        if (_workbook.ActiveSheet is null || _commandManager is null)
            return;

        var command = new SetDataValidationCommand(_workbook.ActiveSheet, validation);
        _commandManager.Execute(command);

        // Reflect dropdown indicators in canvas
        ClearRenderedCache();
        RequestCanvasJsEngineFullRender();
        StateHasChanged();
    }

    private void CloseValidationDialog()
    {
        _showValidationDialog = false;
        StateHasChanged();
    }

    // ── In-cell validation dropdown ───────────────────────────────────────────

    private void OnValidationDropButtonClicked((int Row, int Col, double ClientX, double ClientY) args)
    {
        var sheet = _workbook.ActiveSheet;
        if (sheet is null) return;

        var cellRef = SpreadsheetSelectionState.ToCellRef(args.Row, args.Col);
        var rule = FindValidationRule(sheet, cellRef);
        if (rule?.Type != SpreadsheetValidationType.List) return;

        _validationDropdownItems = SpreadsheetValidationEngine.GetListItems(rule, sheet);
        _validationDropdownRow = args.Row;
        _validationDropdownCol = args.Col;
        _validationDropdownX = args.ClientX;
        _validationDropdownY = args.ClientY;
        _showValidationDropdown = true;
        StateHasChanged();
    }

    private async Task CommitValidationDropdownSelection(string value)
    {
        _showValidationDropdown = false;
        var sheet = _workbook.ActiveSheet;
        if (sheet is null || _commandManager is null) return;

        var cellRef = SpreadsheetSelectionState.ToCellRef(_validationDropdownRow, _validationDropdownCol);
        var previous = sheet.Cells.GetValueOrDefault(cellRef);
        var cmd = new SetCellValueCommand(sheet, cellRef, value, null);
        _commandManager.Execute(cmd);
        _ = OnChange.InvokeAsync(new SpreadsheetChangeEventArgs(sheet, cellRef, previous?.Value, value, previous?.Formula, null));
        InvalidateRenderedCells(new[] { cellRef });
        await SyncCanvasJsEngineCellsAsync(new[] { cellRef });
        StateHasChanged();
    }

    private void CloseValidationDropdown()
    {
        _showValidationDropdown = false;
        StateHasChanged();
    }

    // ── Circle invalid data ───────────────────────────────────────────────────

    private async Task CircleInvalidData()
    {
        _circlesVisible = true;
        var sheet = _workbook.ActiveSheet;
        if (sheet is null) return;

        var invalidRefs = new List<string>();
        foreach (var rule in sheet.DataValidations)
        {
            for (var r = rule.Range.StartRow; r <= rule.Range.EndRow; r++)
            {
                for (var c = rule.Range.StartCol; c <= rule.Range.EndCol; c++)
                {
                    var cellRef = SpreadsheetSelectionState.ToCellRef(r, c);
                    var cell = sheet.Cells.GetValueOrDefault(cellRef);
                    var value = cell?.Value;
                    var result = SpreadsheetValidationEngine.Validate(value, rule, sheet, CultureInfo.CurrentCulture);
                    if (!result.IsValid)
                        invalidRefs.Add(cellRef);
                }
            }
        }

        if (CanvasJsEngineGrid is not null)
            await CanvasJsEngineGrid.ApplyValidationCirclesAsync(invalidRefs);

        StateHasChanged();
    }

    private async Task ClearValidationCircles()
    {
        _circlesVisible = false;
        if (CanvasJsEngineGrid is not null)
            await CanvasJsEngineGrid.ApplyValidationCirclesAsync(null);
        StateHasChanged();
    }

    // ── Validation in commit path ─────────────────────────────────────────────

    /// <summary>
    /// Checks whether <paramref name="parsedValue"/> satisfies the validation rule on <paramref name="cellRef"/>.
    /// Returns false (and sets error state) when the rule uses Stop style.
    /// Returns true and queues a confirmation when Warning/Information style.
    /// Returns true immediately when the cell has no rule or the value is valid.
    /// </summary>
    private bool CheckValidationBeforeCommit(
        SpreadsheetSheet sheet,
        string cellRef,
        object? parsedValue,
        Func<Task> commitAction)
    {
        var rule = FindValidationRule(sheet, cellRef);
        if (rule is null)
            return true; // no rule — proceed

        var result = SpreadsheetValidationEngine.Validate(parsedValue, rule, sheet, CultureInfo.CurrentCulture);
        if (result.IsValid)
            return true;

        var errorStyle = result.ErrorStyle;

        // The severity is the RULE's own style, so the alert picks its own icon and default words:
        // a Stop is a hard block, a Warning and an Information ask whether to keep the value —
        // and they no longer share one path, so an Information message never reads as a warning.
        if (errorStyle == SpreadsheetValidationErrorStyle.Stop)
        {
            _showValidationError = true;
            _validationErrorStyle = SpreadsheetValidationErrorStyle.Stop;
            _validationErrorTitle = rule.ErrorAlert?.Title;
            _validationErrorMessage = rule.ErrorAlert?.Message;
            StateHasChanged();
            return false; // blocked
        }

        // Warning / Information — ask user to confirm
        _pendingConfirmedCommit = commitAction;
        _showValidationConfirm = true;
        _validationConfirmStyle = errorStyle;
        _validationConfirmTitle = rule.ErrorAlert?.Title;
        _validationConfirmMessage = rule.ErrorAlert?.Message;
        StateHasChanged();
        return false; // deferred
    }

    private static SpreadsheetDataValidation? FindValidationRule(SpreadsheetSheet sheet, string cellRef)
    {
        var (row, col) = SpreadsheetSelectionState.ParseCellRef(cellRef);
        return sheet.DataValidations
            .FirstOrDefault(dv => dv.Range.Contains(row, col));
    }

    // Closing an alert only flips the flag: the alert's focus scope carries the restore target (the
    // grid root), so focus returns to the grid when the trap deactivates — after the inert background
    // is released. An explicit FocusAsync here would run while the grid is still inert and be a no-op,
    // and the trap would then restore to the opener instead.
    private void DismissValidationError()
    {
        _showValidationError = false;
        StateHasChanged();
    }

    private async Task ConfirmValidationWarning()
    {
        _showValidationConfirm = false;
        var action = _pendingConfirmedCommit;
        _pendingConfirmedCommit = null;
        StateHasChanged();

        if (action is not null)
        {
            await action();
        }
    }

    private void DismissValidationConfirm()
    {
        _showValidationConfirm = false;
        _pendingConfirmedCommit = null;
        StateHasChanged();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>Returns list items for the validation dropdown of the active cell.</summary>
    internal IReadOnlyList<string> GetActiveCellValidationListItems()
    {
        var sheet = _workbook.ActiveSheet;
        if (sheet?.ActiveCellRef is null) return Array.Empty<string>();

        var rule = FindValidationRule(sheet, sheet.ActiveCellRef);
        if (rule?.Type != SpreadsheetValidationType.List) return Array.Empty<string>();

        return SpreadsheetValidationEngine.GetListItems(rule, sheet);
    }

    /// <summary>Returns the input message for the active cell's validation rule.</summary>
    internal SpreadsheetInputMessage? GetActiveCellInputMessage()
    {
        var sheet = _workbook.ActiveSheet;
        if (sheet?.ActiveCellRef is null) return null;
        var rule = FindValidationRule(sheet, sheet.ActiveCellRef);
        return rule?.InputMessage;
    }
}
