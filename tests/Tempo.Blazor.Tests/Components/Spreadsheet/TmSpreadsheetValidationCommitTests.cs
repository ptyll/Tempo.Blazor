using Bunit;
using Tempo.Blazor.Components.Spreadsheet;
using Tempo.Blazor.Components.Spreadsheet.Data;
using Tempo.Blazor.Components.Spreadsheet.Models;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.Spreadsheet;

[Collection("SpreadsheetClipboard")]
public class TmSpreadsheetValidationCommitTests : LocalizationTestBase
{
    // ── Stop style ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Commit_InvalidValue_StopStyle_ShowsErrorAndBlocksCommit()
    {
        var cut = Render<TmSpreadsheet>();
        var sheet = cut.Instance.Workbook.ActiveSheet!;

        sheet.DataValidations.Add(new SpreadsheetDataValidation
        {
            Range = new SpreadsheetRange(0, 0, 0, 0), // A1
            Type = SpreadsheetValidationType.Whole,
            Operator = SpreadsheetValidationOperator.Between,
            Formula1 = "1",
            Formula2 = "10",
            AllowBlank = false,
            ErrorAlert = new SpreadsheetValidationErrorAlert
            {
                Style = SpreadsheetValidationErrorStyle.Stop,
                Title = "Bad input",
                Message = "Must be 1–10."
            }
        });

        var grid = cut.FindComponent<TmSpreadsheetCanvasGrid>();
        await cut.InvokeAsync(() => grid.Instance.OnCanvasCellEditCommitted(0, 0, "99"));

        cut.FindAll(".tm-spreadsheet-alert").Should().HaveCount(1, "error alert should appear");
        cut.Find(".tm-modal-title").TextContent.Should().Be("Bad input");
        cut.Find(".tm-spreadsheet-alert__message").TextContent.Should().Be("Must be 1–10.");

        // Cell must NOT have been written
        sheet.Cells.ContainsKey("A1").Should().BeFalse("Stop should reject the value");
    }

    [Fact]
    public async Task Commit_InvalidValue_StopStyle_DismissClosesAlert()
    {
        var cut = Render<TmSpreadsheet>();
        var sheet = cut.Instance.Workbook.ActiveSheet!;

        sheet.DataValidations.Add(new SpreadsheetDataValidation
        {
            Range = new SpreadsheetRange(0, 0, 0, 0),
            Type = SpreadsheetValidationType.Whole,
            Operator = SpreadsheetValidationOperator.Between,
            Formula1 = "1",
            Formula2 = "10",
            AllowBlank = false,
            ErrorAlert = new SpreadsheetValidationErrorAlert { Style = SpreadsheetValidationErrorStyle.Stop }
        });

        var grid = cut.FindComponent<TmSpreadsheetCanvasGrid>();
        await cut.InvokeAsync(() => grid.Instance.OnCanvasCellEditCommitted(0, 0, "99"));
        cut.FindAll(".tm-spreadsheet-alert").Should().HaveCount(1);

        // Clicking "OK" dismisses the alert
        cut.Find(".tm-spreadsheet-alert button").Click();
        cut.FindAll(".tm-spreadsheet-alert").Should().BeEmpty();
    }

    // ── Warning style ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Commit_InvalidValue_WarningStyle_ShowsConfirmDialog()
    {
        var cut = Render<TmSpreadsheet>();
        var sheet = cut.Instance.Workbook.ActiveSheet!;

        sheet.DataValidations.Add(new SpreadsheetDataValidation
        {
            Range = new SpreadsheetRange(0, 0, 0, 0),
            Type = SpreadsheetValidationType.Whole,
            Operator = SpreadsheetValidationOperator.Between,
            Formula1 = "1",
            Formula2 = "10",
            AllowBlank = false,
            ErrorAlert = new SpreadsheetValidationErrorAlert
            {
                Style = SpreadsheetValidationErrorStyle.Warning,
                Title = "Warning",
                Message = "Value out of range."
            }
        });

        var grid = cut.FindComponent<TmSpreadsheetCanvasGrid>();
        await cut.InvokeAsync(() => grid.Instance.OnCanvasCellEditCommitted(0, 0, "99"));

        // Confirmation dialog should appear (not an error — it asks whether to keep the value)
        cut.FindAll(".tm-spreadsheet-alert").Should().HaveCount(1, "confirm dialog should appear");
        cut.Markup.Should().Contain("Use anyway", "a Warning confirm offers the keep-the-value action");

        // Value not yet written
        sheet.Cells.ContainsKey("A1").Should().BeFalse("value should not be committed before confirmation");
    }

    [Fact]
    public async Task Commit_InvalidValue_WarningStyle_ConfirmCommitsValue()
    {
        var cut = Render<TmSpreadsheet>();
        var sheet = cut.Instance.Workbook.ActiveSheet!;

        sheet.DataValidations.Add(new SpreadsheetDataValidation
        {
            Range = new SpreadsheetRange(0, 0, 0, 0),
            Type = SpreadsheetValidationType.Whole,
            Operator = SpreadsheetValidationOperator.Between,
            Formula1 = "1",
            Formula2 = "10",
            AllowBlank = false,
            ErrorAlert = new SpreadsheetValidationErrorAlert { Style = SpreadsheetValidationErrorStyle.Warning }
        });

        var grid = cut.FindComponent<TmSpreadsheetCanvasGrid>();
        await cut.InvokeAsync(() => grid.Instance.OnCanvasCellEditCommitted(0, 0, "99"));

        // Click "Use anyway" to keep the value
        await cut.InvokeAsync(() => cut.FindAll(".tm-spreadsheet-alert button").Last().Click());

        // Value should now be written
        sheet.Cells.TryGetValue("A1", out var cell).Should().BeTrue();
        cell!.Value.Should().Be(99.0);
        cut.FindAll(".tm-spreadsheet-alert").Should().BeEmpty();
    }

    [Fact]
    public async Task Commit_InvalidValue_WarningStyle_CancelDoesNotCommit()
    {
        var cut = Render<TmSpreadsheet>();
        var sheet = cut.Instance.Workbook.ActiveSheet!;

        sheet.DataValidations.Add(new SpreadsheetDataValidation
        {
            Range = new SpreadsheetRange(0, 0, 0, 0),
            Type = SpreadsheetValidationType.Whole,
            Operator = SpreadsheetValidationOperator.Between,
            Formula1 = "1",
            Formula2 = "10",
            AllowBlank = false,
            ErrorAlert = new SpreadsheetValidationErrorAlert { Style = SpreadsheetValidationErrorStyle.Warning }
        });

        var grid = cut.FindComponent<TmSpreadsheetCanvasGrid>();
        await cut.InvokeAsync(() => grid.Instance.OnCanvasCellEditCommitted(0, 0, "99"));

        // Click "Cancel"
        await cut.InvokeAsync(() => cut.FindAll(".tm-spreadsheet-alert button").First().Click());

        sheet.Cells.ContainsKey("A1").Should().BeFalse("cancel should not commit the value");
        cut.FindAll(".tm-spreadsheet-alert").Should().BeEmpty();
    }

    // ── Valid value passes through ────────────────────────────────────────────

    [Fact]
    public async Task Commit_ValidValue_IsCommittedWithoutDialog()
    {
        var cut = Render<TmSpreadsheet>();
        var sheet = cut.Instance.Workbook.ActiveSheet!;

        sheet.DataValidations.Add(new SpreadsheetDataValidation
        {
            Range = new SpreadsheetRange(0, 0, 0, 0),
            Type = SpreadsheetValidationType.Whole,
            Operator = SpreadsheetValidationOperator.Between,
            Formula1 = "1",
            Formula2 = "10",
            AllowBlank = false,
            ErrorAlert = new SpreadsheetValidationErrorAlert { Style = SpreadsheetValidationErrorStyle.Stop }
        });

        var grid = cut.FindComponent<TmSpreadsheetCanvasGrid>();
        await cut.InvokeAsync(() => grid.Instance.OnCanvasCellEditCommitted(0, 0, "5"));

        cut.FindAll(".tm-spreadsheet-alert").Should().BeEmpty("valid value should produce no dialog");
        sheet.Cells.TryGetValue("A1", out var cell).Should().BeTrue();
        cell!.Value.Should().Be(5.0);
    }
}
