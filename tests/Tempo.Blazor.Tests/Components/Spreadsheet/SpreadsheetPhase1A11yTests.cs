using System;
using System.Collections.Generic;
using System.IO;
using AngleSharp.Dom;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Tempo.Blazor.Components.Feedback;
using Tempo.Blazor.Components.Spreadsheet;
using Tempo.Blazor.Components.Spreadsheet.Data;
using Tempo.Blazor.Components.Spreadsheet.Dialogs;
using Tempo.Blazor.Components.Spreadsheet.Models;
using Tempo.Blazor.Tests.Localization;
using Xunit;

namespace Tempo.Blazor.Tests.Components.Spreadsheet;

/// <summary>
/// Phase FZE-1 (wave 0) — the a11y and bug-fix phase that needs no visual redesign: the three
/// dialogs that used to render as an unstyled block in the document flow under the grid
/// (Name Manager, Named Range Edit, Hyperlink) become real modal overlays with a dialog role and an
/// accessible name, the dialog controls that carried no accessible name get one, the validation
/// alerts become <c>alertdialog</c>s with focus management, and the two selection lists (Format Cells
/// categories, Name Manager rows) become keyboard operable per
/// <c>docs/keyboard-activation-convention.md</c>.
/// </summary>
public class SpreadsheetPhase1A11yTests : LocalizationTestBase
{
    // "ElementReference.FocusAsync" surfaces in bUnit's Loose JSInterop under this identifier
    // (same constant the TmDashboard / TmSplitButton focus tests use).
    private const string FocusInvocation = "Blazor._internal.domWrapper.focus";

    // ── Modal overlay + dialog role for the three previously unstyled dialogs ──

    [Fact]
    public void NameManagerDialog_RendersModalOverlay_WithDialogRoleAndAriaModal()
    {
        var workbook = new SpreadsheetWorkbook();
        workbook.NamedRanges.Add(new SpreadsheetNamedRange { Name = "Sales", RefersTo = "A1:A10" });

        var cut = Render<TmSpreadsheetNameManagerDialog>(
            parameters => parameters.Add(p => p.Workbook, workbook));

        // The overlay wraps the card, so the dialog paints above the grid instead of in the flow.
        var backdrop = cut.Find(".tm-spreadsheet-name-manager__backdrop");
        backdrop.QuerySelector(".tm-spreadsheet-name-manager").Should()
            .NotBeNull("the dialog card must be a child of the fixed overlay");

        // The dialog contract belongs to the core focus scope that wraps the card.
        var dialog = cut.Find("[role='dialog']");
        dialog.GetAttribute("aria-modal").Should().Be("true");

        var labelledBy = dialog.GetAttribute("aria-labelledby");
        labelledBy.Should().NotBeNullOrWhiteSpace("the dialog needs an accessible name pointing at its title");
        cut.Find($"#{labelledBy}").TextContent.Trim().Should().Be("Name Manager");

        // The card keeps its own class for styling and for the E2E lane.
        dialog.QuerySelector(".tm-spreadsheet-name-manager").Should().NotBeNull();
    }

    [Fact]
    public void NamedRangeEditDialog_RendersModalOverlay_WithAriaLabelledBy()
    {
        var cut = Render<TmSpreadsheetNamedRangeEditDialog>(parameters => parameters
            .Add(p => p.Workbook, new SpreadsheetWorkbook()));

        cut.Find(".tm-spreadsheet-named-range-edit__backdrop")
            .QuerySelector(".tm-spreadsheet-named-range-edit").Should()
            .NotBeNull("the dialog card must be a child of the fixed overlay");

        var dialog = cut.Find("[role='dialog']");
        dialog.GetAttribute("aria-modal").Should().Be("true");

        var labelledBy = dialog.GetAttribute("aria-labelledby");
        labelledBy.Should().NotBeNullOrWhiteSpace();
        cut.Find($"#{labelledBy}").TextContent.Trim().Should().Be("New Name");
        dialog.QuerySelector(".tm-spreadsheet-named-range-edit").Should().NotBeNull();
    }

    [Fact]
    public void HyperlinkDialog_RendersModalOverlay_WithAriaLabelledBy()
    {
        var cut = Render<TmSpreadsheetHyperlinkDialog>(parameters => parameters
            .Add(p => p.Workbook, new SpreadsheetWorkbook()));

        cut.Find(".tm-spreadsheet-hyperlink__backdrop")
            .QuerySelector(".tm-spreadsheet-hyperlink").Should()
            .NotBeNull("the dialog card must be a child of the fixed overlay");

        var dialog = cut.Find("[role='dialog']");
        dialog.GetAttribute("aria-modal").Should().Be("true");

        var labelledBy = dialog.GetAttribute("aria-labelledby");
        labelledBy.Should().NotBeNullOrWhiteSpace();
        cut.Find($"#{labelledBy}").TextContent.Trim().Should().Be("Hyperlink");
        dialog.QuerySelector(".tm-spreadsheet-hyperlink").Should().NotBeNull();
    }

    [Fact]
    public void NamedRangeEdit_KeepsTypedInput_WhenTheParentReRenders()
    {
        // A Spreadsheet re-renders on its own (canvas sync callbacks, aggregation). The dialog must not
        // reset the user's half-typed form when that happens — the E2E long-list lane caught the
        // reset as a Save that silently refused to close.
        var workbook = new SpreadsheetWorkbook();
        var cut = Render<TmSpreadsheetNamedRangeEditDialog>(parameters => parameters
            .Add(p => p.Workbook, workbook));

        cut.Find("#nr-name").Input("KeptName");
        cut.Find("#nr-refers").Input("=Sheet1!B2:B9");

        cut.Render(parameters => parameters.Add(p => p.Workbook, workbook));

        cut.Find("#nr-name").GetAttribute("value").Should().Be("KeptName",
            "a parent re-render must not wipe the name the user typed");
        cut.Find("#nr-refers").GetAttribute("value").Should().Be("=Sheet1!B2:B9");
    }

    [Fact]
    public void Hyperlink_KeepsTypedInput_WhenTheParentReRenders()
    {
        var workbook = new SpreadsheetWorkbook();
        var cut = Render<TmSpreadsheetHyperlinkDialog>(parameters => parameters
            .Add(p => p.Workbook, workbook));

        cut.Find("#hl-target").Input("https://example.com");
        cut.Find("#hl-display").Change("Example");

        cut.Render(parameters => parameters.Add(p => p.Workbook, workbook));

        cut.Find("#hl-target").GetAttribute("value").Should().Be("https://example.com",
            "a parent re-render must not wipe the address the user typed");
        cut.Find("#hl-display").GetAttribute("value").Should().Be("Example");
    }

    [Fact]
    public void NamedRangeEdit_InvalidName_MarksTheFieldAndPointsAtTheError()
    {
        var cut = Render<TmSpreadsheetNamedRangeEditDialog>(parameters => parameters
            .Add(p => p.Workbook, new SpreadsheetWorkbook()));

        cut.Find("#nr-name").Input("1 not a name");
        cut.Find("#nr-refers").Input("=Sheet1!A1");
        cut.Find(".tm-spreadsheet-named-range-edit__btn--ok").Click();

        var name = cut.Find("#nr-name");
        name.GetAttribute("aria-invalid").Should().Be("true");

        var describedBy = name.GetAttribute("aria-describedby");
        describedBy.Should().NotBeNullOrWhiteSpace("the invalid field points at its message");

        var error = cut.Find($"#{describedBy}");
        error.GetAttribute("role").Should().Be("alert");
        error.TextContent.Trim().Should().Be("The name is not valid");

        // The first invalid field takes the focus, so the message is announced from where it is.
        FocusTargetId(FocusTargets().LastOrDefault()).Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void NamedRangeEdit_EmptyName_ShowsOnlyTheRequiredMessage()
    {
        var cut = Render<TmSpreadsheetNamedRangeEditDialog>(parameters => parameters
            .Add(p => p.Workbook, new SpreadsheetWorkbook()));

        cut.Find("#nr-refers").Input("=Sheet1!A1");
        cut.Find(".tm-spreadsheet-named-range-edit__btn--ok").Click();

        var errors = cut.FindAll(".tm-spreadsheet-named-range-edit__error");
        errors.Should().ContainSingle("an empty name is missing, not malformed — the two messages never stack");
        errors[0].TextContent.Trim().Should().Be("Name is required");
        cut.Find("#nr-name").GetAttribute("aria-invalid").Should().Be("true");
    }

    /// <summary>
    /// "No unstyled dialog" is only true while the stylesheet actually carries a fixed, modal-z-index
    /// overlay rule for each of the three cards, so the DoD is guarded from the source side too.
    /// </summary>
    [Fact]
    public void DialogOverlays_HaveFixedModalRulesInThePackageStylesheet()
    {
        var css = File.ReadAllText(Path.Combine(
            RepositoryRoot(), "src", "Tempo.Blazor.Spreadsheet", "wwwroot", "css", "components", "_spreadsheet.css"));

        var overlay = RuleBody(css, ".tm-spreadsheet-dialog-backdrop");
        overlay.Should().NotBeNull("the shared overlay wrapper must be styled");
        overlay.Should().Contain("position: fixed");
        overlay.Should().Contain("z-index: var(--tm-z-modal)");

        // The per-dialog backdrops are the shared wrapper plus a hook class — no duplicated rules.
        foreach (var hook in new[]
                 {
                     "tm-spreadsheet-name-manager__backdrop",
                     "tm-spreadsheet-named-range-edit__backdrop",
                     "tm-spreadsheet-hyperlink__backdrop",
                 })
        {
            RuleBody(css, "." + hook).Should().BeNull(
                $"'.{hook}' must not duplicate the shared overlay rule");
        }

        foreach (var card in new[]
                 {
                     "tm-spreadsheet-name-manager",
                     "tm-spreadsheet-named-range-edit",
                     "tm-spreadsheet-hyperlink",
                 })
        {
            RuleBody(css, "." + card).Should().NotBeNull($"'.{card}' (the dialog card) must be styled");
        }

        // The sticky header of the Name Manager table and the selected-row highlight.
        RuleBody(css, ".tm-spreadsheet-name-manager__table th").Should().NotBeNull("the table header sticks");
        RuleBody(css, ".tm-spreadsheet-name-manager__row--selected")
            .Should().NotBeNull("the selected row must be highlighted");
        css.Should().Contain("--tm-color-primary-subtle", "the selected row uses the primary-subtle token");

        // A long list must not push the footer out of the viewport: the height limit has to propagate
        // from the fixed backdrop through the focus-scope wrapper into the card, whose middle scrolls.
        var scope = RuleBody(css, ".tm-spreadsheet-dialog-scope");
        scope.Should().NotBeNull("the focus-scope wrapper is part of the height chain");
        scope.Should().Contain("min-height: 0");
        scope.Should().Contain("max-height: 100%");

        var managerCard = RuleBody(css, ".tm-spreadsheet-name-manager");
        managerCard.Should().NotBeNull();
        managerCard.Should().Contain("flex-direction: column");
        managerCard.Should().Contain("max-height: 100%");
        managerCard.Should().Contain("min-height: 0");

        RuleBody(css, ".tm-spreadsheet-name-manager__table-wrap").Should().Contain("overflow: auto",
            "the table wrap is the scrollable middle of the card");
        RuleBody(css, ".tm-spreadsheet-name-manager__actions").Should().Contain("flex: none",
            "the footer keeps its natural height inside the column");
    }

    // ── Text to Columns: localized fixed-width placeholder ──

    [Fact]
    public void TextToColumns_FixedWidthPlaceholder_IsLocalized()
    {
        UseCzechLocalization();

        var cut = Render<TmSpreadsheetTextToColumnsDialog>(p => p
            .Add(c => c.SourceRows, new[] { "Jan;Novak;Praha" }));

        // Step 1 → pick "Fixed width" → step 2 renders the break-positions field.
        cut.FindAll(".tm-spreadsheet-t2c__radio input[type=radio]")[1].Change(true);
        cut.Find(".tm-spreadsheet-t2c__btn--ok").Click();

        var input = cut.Find(".tm-spreadsheet-t2c__breaks-input");
        var placeholder = input.GetAttribute("placeholder");

        placeholder.Should().Be("např. 5, 10");
        placeholder.Should().NotBe("e.g. 5, 10", "the placeholder must not be a hardcoded English string");
    }

    // ── Accessible names for the label-less dialog controls ──

    [Fact]
    public void CustomFilter_AllSelectsAndInputs_HaveAccessibleName()
    {
        var cut = Render<TmSpreadsheetCustomFilterDialog>(p => p
            .Add(c => c.ColumnIndex, 1)
            .Add(c => c.Kind, SpreadsheetFilterKind.Number));

        // "Between" renders both operand inputs of the first condition, and a second condition
        // adds its own operator select — the full control surface in one render.
        cut.FindAll(".tm-spreadsheet-custom-filter__op")[0]
            .Change(SpreadsheetFilterOperator.Between.ToString());
        cut.FindAll(".tm-spreadsheet-custom-filter__op")[1]
            .Change(SpreadsheetFilterOperator.GreaterThan.ToString());

        var controls = cut.FindAll(".tm-spreadsheet-custom-filter select, .tm-spreadsheet-custom-filter input");
        controls.Count.Should().BeGreaterThanOrEqualTo(6);

        var unnamed = controls
            .Where(element => string.IsNullOrWhiteSpace(AccessibleName(cut, element)))
            .Select(element => element.OuterHtml)
            .ToList();

        unnamed.Should().BeEmpty("every select/input of the dialog needs an accessible name: "
                                 + string.Join(" | ", unnamed));
    }

    [Fact]
    public void SortDialog_LevelSelects_HaveAccessibleName()
    {
        var cut = Render<TmSpreadsheetSortDialog>(p => p
            .Add(c => c.Range, new SpreadsheetRange(0, 0, 9, 2)));

        cut.Find(".tm-spreadsheet-sort__tool").Click(); // second level

        var rows = cut.FindAll(".tm-spreadsheet-sort__table tbody tr");
        rows.Count.Should().Be(2);

        var firstLevel = rows[0].QuerySelectorAll("select").Select(s => s.GetAttribute("aria-label")).ToList();
        firstLevel.Should().Equal(
            new[] { "Column, level 1", "Sort on, level 1", "Order, level 1" },
            "each level select names its role AND its level number (no plural form is involved)");

        var secondLevel = rows[1].QuerySelectorAll("select").Select(s => s.GetAttribute("aria-label")).ToList();
        secondLevel.Should().Equal(new[] { "Column, level 2", "Sort on, level 2", "Order, level 2" });
    }

    [Fact]
    public void InsertImage_Label_IsAssociatedWithInput()
    {
        var cut = Render<TmSpreadsheet>();
        OpenInsertImageDialog(cut);

        var input = cut.Find(".tm-spreadsheet-dialog__input");
        var id = input.GetAttribute("id");
        id.Should().NotBeNullOrWhiteSpace("the url input needs an id so its label can point at it");

        cut.Find($".tm-spreadsheet-dialog__label[for='{id}']").TextContent.Trim()
            .Should().Be("Image URL");
    }

    [Fact]
    public async Task InsertImage_Enter_Applies_AndEscape_Cancels()
    {
        var cut = Render<TmSpreadsheet>();
        var sheet = cut.Instance.Workbook.ActiveSheet!;
        sheet.ActiveCellRef = "B2";

        OpenInsertImageDialog(cut);

        // Escape is the surface's own dismissal, wired to the focus scope (which installs the
        // document-level listener); bUnit has no JS module, so the scope's JSInvokable is driven.
        var input = cut.Find(".tm-spreadsheet-dialog__input");
        input.Input("https://example.com/pic.png");
        var scope = cut.FindComponent<TmFocusScope>();
        scope.Instance.CloseOnEscape.Should().BeTrue();
        await cut.InvokeAsync(() => scope.Instance.HandleFocusTrapEscapeAsync());

        cut.FindAll(".tm-spreadsheet-dialog").Should().BeEmpty("Escape must cancel the dialog");
        sheet.Cells.TryGetValue("B2", out var untouched).Should().BeFalse();

        // Enter on the same input applies it.
        OpenInsertImageDialog(cut);
        var again = cut.Find(".tm-spreadsheet-dialog__input");
        again.Input("https://example.com/pic.png");
        again.KeyDown(new KeyboardEventArgs { Key = "Enter" });

        cut.FindAll(".tm-spreadsheet-dialog").Should().BeEmpty("Enter must apply and close");
        sheet.Cells.TryGetValue("B2", out var committed).Should().BeTrue();
        committed!.ImageUrl.Should().Be("https://example.com/pic.png");
    }

    // ── Validation alerts: alertdialog + focus + Escape + Stop backdrop ──

    [Fact]
    public async Task ValidationAlert_Error_HasAlertDialogRole_DescribedBy_AndFocusesPrimary()
    {
        var cut = Render<TmSpreadsheet>();
        AddStopValidationRule(cut);

        await cut.InvokeAsync(() => cut.FindComponent<TmSpreadsheetCanvasGrid>()
            .Instance.OnCanvasCellEditCommitted(0, 0, "99"));

        var alert = cut.Find("[role='alertdialog']");
        alert.GetAttribute("aria-modal").Should().Be("true");

        var labelledBy = alert.GetAttribute("aria-labelledby");
        labelledBy.Should().NotBeNullOrWhiteSpace();
        cut.Find($"#{labelledBy}").TextContent.Trim().Should().Be("Bad input");

        var describedBy = alert.GetAttribute("aria-describedby");
        describedBy.Should().NotBeNullOrWhiteSpace();
        cut.Find($"#{describedBy}").TextContent.Trim().Should().Be("Must be 1–10.");

        // The initial focus is declared on the scope for the focus-trap module, which resolves the
        // primary action by id when it activates (bUnit has no JS module, so the declaration is the
        // observable contract; the E2E lane asserts the live document.activeElement).
        var initialFocusId = alert.GetAttribute("data-initial-focus");
        initialFocusId.Should().NotBeNullOrWhiteSpace("an alertdialog moves focus to its primary action");
        cut.Find($"#{initialFocusId}").TextContent.Trim().Should().Be("Confirm");
        alert.QuerySelector(".tm-spreadsheet-alert__actions button:last-of-type")!.Id
            .Should().Be(initialFocusId, "the primary action is the confirm button");
    }

    [Fact]
    public async Task ValidationAlert_Stop_BackdropClick_DoesNotClose()
    {
        var cut = Render<TmSpreadsheet>();
        AddStopValidationRule(cut);

        await cut.InvokeAsync(() => cut.FindComponent<TmSpreadsheetCanvasGrid>()
            .Instance.OnCanvasCellEditCommitted(0, 0, "99"));
        cut.FindAll(".tm-spreadsheet-alert").Should().HaveCount(1);

        // A Stop alert is a hard block: the backdrop click is ignored (the surface is dismissed only
        // by its primary action or Escape), so it cannot wave the invalid value through.
        cut.Find(".tm-spreadsheet-dialog-backdrop").Click();

        cut.FindAll(".tm-spreadsheet-alert").Should().HaveCount(1,
            "a Stop alert only closes through its own primary action");

        // ... while the primary action still dismisses it.
        cut.Find(".tm-spreadsheet-alert__actions button").Click();
        cut.FindAll(".tm-spreadsheet-alert").Should().BeEmpty();
    }

    [Fact]
    public async Task ValidationAlert_Dismiss_DeclaresTheGridAsItsRestoreTarget()
    {
        var cut = Render<TmSpreadsheet>();
        AddStopValidationRule(cut);

        await cut.InvokeAsync(() => cut.FindComponent<TmSpreadsheetCanvasGrid>()
            .Instance.OnCanvasCellEditCommitted(0, 0, "99"));
        cut.FindAll(".tm-spreadsheet-alert").Should().HaveCount(1);

        // The restore is the focus trap's job, not a FocusAsync race against the inert background: the
        // scope names the grid root, and the trap focuses it while it deactivates.
        var scope = cut.FindComponent<TmFocusScope>();
        scope.Instance.RestoreFocusTargetId.Should().NotBeNullOrWhiteSpace();
        cut.Find($"#{scope.Instance.RestoreFocusTargetId}").ClassList
            .Should().Contain("tm-spreadsheet-canvas-grid", "focus must return to the grid");

        cut.Find(".tm-spreadsheet-alert__actions button").Click();
        cut.FindAll(".tm-spreadsheet-alert").Should().BeEmpty();
    }

    [Fact]
    public async Task ValidationAlert_Escape_ClosesTheAlert()
    {
        var cut = Render<TmSpreadsheet>();
        AddStopValidationRule(cut);

        await cut.InvokeAsync(() => cut.FindComponent<TmSpreadsheetCanvasGrid>()
            .Instance.OnCanvasCellEditCommitted(0, 0, "99"));
        cut.FindAll(".tm-spreadsheet-alert").Should().HaveCount(1);

        var scope = cut.FindComponent<TmFocusScope>();
        scope.Instance.CloseOnEscape.Should().BeTrue("the alert closes on Escape");
        await cut.InvokeAsync(() => scope.Instance.HandleFocusTrapEscapeAsync());

        cut.FindAll(".tm-spreadsheet-alert").Should().BeEmpty("Escape dismisses the alert");
        cut.Instance.Workbook.ActiveSheet!.Cells.ContainsKey("A1").Should().BeFalse();
    }

    // ── Keyboard operable selection lists ──

    [Fact]
    public void FormatCells_CategoryList_IsListbox_WithArrowKeys()
    {
        var cut = Render<TmSpreadsheetFormatCellsDialog>(p => p
            .Add(c => c.Style, new SpreadsheetCellStyle())
            .Add(c => c.OnApply, EventCallback.Factory.Create<SpreadsheetCellStyle>(this, _ => { })));

        var listbox = cut.Find(".tm-fcd__listbox[role='listbox']");
        listbox.GetAttribute("tabindex").Should().BeNull("the container is not the tab stop");
        listbox.GetAttribute("aria-activedescendant").Should().BeNull("the roving tabindex replaces it");
        listbox.GetAttribute("aria-labelledby").Should().NotBeNullOrWhiteSpace();

        var options = Options(cut);
        options.Should().HaveCount(8);
        options[0].GetAttribute("aria-selected").Should().Be("true");
        options[0].GetAttribute("tabindex").Should().Be("0", "the selected option is the single tab stop");
        options[1].GetAttribute("aria-selected").Should().Be("false");
        options[1].GetAttribute("tabindex").Should().Be("-1");

        // ↓ moves the selection one category down; the roving tab stop follows it.
        listbox.KeyDown(new KeyboardEventArgs { Key = "ArrowDown" });
        options = Options(cut);
        options[1].GetAttribute("aria-selected").Should().Be("true");
        options[1].GetAttribute("tabindex").Should().Be("0");
        options[0].GetAttribute("tabindex").Should().Be("-1");
        options[1].ClassList.Should().Contain("tm-fcd__listbox-item--selected");

        // The DOM focus follows the roving stop (scrolls it into view).
        FocusTargetId(FocusTargets().LastOrDefault()).Should().NotBeNullOrEmpty(
            "the newly selected option takes the focus");

        // ↑ moves it back up.
        cut.Find(".tm-fcd__listbox[role='listbox']").KeyDown(new KeyboardEventArgs { Key = "ArrowUp" });
        Options(cut)[0].GetAttribute("aria-selected").Should().Be("true");

        // Home/End jump to the ends of the list.
        cut.Find(".tm-fcd__listbox[role='listbox']").KeyDown(new KeyboardEventArgs { Key = "End" });
        Options(cut)[^1].GetAttribute("aria-selected").Should().Be("true");
        Options(cut)[^1].GetAttribute("tabindex").Should().Be("0");

        cut.Find(".tm-fcd__listbox[role='listbox']").KeyDown(new KeyboardEventArgs { Key = "Home" });
        Options(cut)[0].GetAttribute("aria-selected").Should().Be("true");
    }

    [Fact]
    public void NameManager_RowSelection_KeyboardOperable()
    {
        var workbook = new SpreadsheetWorkbook();
        var sales = new SpreadsheetNamedRange { Name = "Sales", RefersTo = "A1" };
        var tax = new SpreadsheetNamedRange { Name = "Tax", RefersTo = "B1" };
        workbook.NamedRanges.Add(sales);
        workbook.NamedRanges.Add(tax);

        SpreadsheetNamedRange? edited = null;
        SpreadsheetNamedRange? deleted = null;

        var cut = Render<TmSpreadsheetNameManagerDialog>(parameters => parameters
            .Add(p => p.Workbook, workbook)
            .Add(p => p.OnEdit, range => edited = range)
            .Add(p => p.OnDelete, range => deleted = range));

        var table = cut.Find(".tm-spreadsheet-name-manager__table");
        table.GetAttribute("role").Should().Be("grid");
        table.GetAttribute("tabindex").Should().BeNull("the rows are the roving tab stops, not the table");

        // A roving tabindex needs exactly one tab stop, so the grid opens on its first row.
        var rows = cut.FindAll("tbody tr");
        rows[0].GetAttribute("aria-selected").Should().Be("true");
        rows[0].GetAttribute("tabindex").Should().Be("0");
        rows[1].GetAttribute("aria-selected").Should().Be("false");
        rows[1].GetAttribute("tabindex").Should().Be("-1");
        rows[0].ClassList.Should().Contain("tm-spreadsheet-name-manager__row--selected");

        // ↓ walks to the second row and moves the tab stop (and the DOM focus) with it.
        table.KeyDown(new KeyboardEventArgs { Key = "ArrowDown" });
        rows = cut.FindAll("tbody tr");
        rows[0].GetAttribute("aria-selected").Should().Be("false");
        rows[0].GetAttribute("tabindex").Should().Be("-1");
        rows[1].GetAttribute("aria-selected").Should().Be("true");
        rows[1].GetAttribute("tabindex").Should().Be("0");
        FocusTargetId(FocusTargets().LastOrDefault()).Should().NotBeNullOrEmpty();

        // ↑ walks back.
        cut.Find(".tm-spreadsheet-name-manager__table").KeyDown(new KeyboardEventArgs { Key = "ArrowUp" });
        cut.FindAll("tbody tr")[0].GetAttribute("aria-selected").Should().Be("true");

        // Enter edits the selected range.
        cut.Find(".tm-spreadsheet-name-manager__table").KeyDown(new KeyboardEventArgs { Key = "Enter" });
        edited.Should().NotBeNull();
        edited!.Name.Should().Be("Sales");

        // Delete deletes the selected range.
        cut.Find(".tm-spreadsheet-name-manager__table").KeyDown(new KeyboardEventArgs { Key = "Delete" });
        deleted.Should().NotBeNull();
        deleted!.Name.Should().Be("Sales");
    }

    [Fact]
    public void NameManager_Filter_HidingTheSelectedRow_MovesTheTabStopToTheFirstVisibleRow()
    {
        var workbook = new SpreadsheetWorkbook();
        workbook.NamedRanges.Add(new SpreadsheetNamedRange { Name = "Alpha", RefersTo = "A1" });
        workbook.NamedRanges.Add(new SpreadsheetNamedRange { Name = "Beta", RefersTo = "A2" });

        var cut = Render<TmSpreadsheetNameManagerDialog>(parameters => parameters
            .Add(p => p.Workbook, workbook));

        // Select the second row, then filter it away.
        cut.FindAll("tbody tr")[1].Click();
        cut.Find(".tm-spreadsheet-name-manager__filter").Input("Alp");

        var rows = cut.FindAll("tbody tr");
        rows.Should().ContainSingle();
        rows[0].TextContent.Should().Contain("Alpha");
        rows[0].GetAttribute("aria-selected").Should().Be("true");
        cut.FindAll("tbody tr[tabindex='0']").Should().ContainSingle(
            "a roving tabindex needs exactly one tab stop, and the hidden selection must not keep it");

        // Edit stays usable because the selected row is visible again.
        cut.FindAll(".tm-spreadsheet-name-manager__tool")
            .First(button => button.TextContent.Contains("Edit"))
            .HasAttribute("disabled").Should().BeFalse();
    }

    [Fact]
    public void NameManager_Filter_WithoutMatches_DisablesEditAndDelete()
    {
        var workbook = new SpreadsheetWorkbook();
        workbook.NamedRanges.Add(new SpreadsheetNamedRange { Name = "Alpha", RefersTo = "A1" });

        var cut = Render<TmSpreadsheetNameManagerDialog>(parameters => parameters
            .Add(p => p.Workbook, workbook));

        cut.Find(".tm-spreadsheet-name-manager__filter").Input("zzz");

        cut.FindAll("tbody tr").Should().BeEmpty();
        foreach (var tool in cut.FindAll(".tm-spreadsheet-name-manager__tool"))
        {
            var label = tool.TextContent;
            if (label.Contains("Edit") || label.Contains("Delete"))
            {
                tool.HasAttribute("disabled").Should().BeTrue(
                    $"'{label.Trim()}' has no visible row to act on");
            }
        }
    }

    [Fact]
    public void NameManager_Delete_SelectsTheNeighbour()
    {
        var workbook = new SpreadsheetWorkbook();
        workbook.NamedRanges.Add(new SpreadsheetNamedRange { Name = "Alpha", RefersTo = "A1" });
        workbook.NamedRanges.Add(new SpreadsheetNamedRange { Name = "Beta", RefersTo = "A2" });
        workbook.NamedRanges.Add(new SpreadsheetNamedRange { Name = "Gamma", RefersTo = "A3" });

        var cut = Render<TmSpreadsheetNameManagerDialog>(parameters => parameters
            .Add(p => p.Workbook, workbook)
            .Add(p => p.OnDelete, range => workbook.NamedRanges.Remove(range)));

        // Select the middle row, then delete it from the toolbar.
        cut.FindAll("tbody tr")[1].Click();
        cut.FindAll(".tm-spreadsheet-name-manager__tool")
            .First(button => button.TextContent.Contains("Delete"))
            .Click();

        var rows = cut.FindAll("tbody tr");
        rows.Should().HaveCount(2);
        rows.Should().Contain(row => row.ClassName != null
                                     && row.ClassName.Contains("tm-spreadsheet-name-manager__row--selected")
                                     && row.TextContent.Contains("Gamma"),
            "after deleting the middle row the row that took its place is selected");
    }

    [Fact]
    public void NameManager_Delete_LastRow_SelectsNewLastRow_AndEmptyDisablesTheToolbar()
    {
        var workbook = new SpreadsheetWorkbook();
        workbook.NamedRanges.Add(new SpreadsheetNamedRange { Name = "Only", RefersTo = "A1" });

        var cut = Render<TmSpreadsheetNameManagerDialog>(parameters => parameters
            .Add(p => p.Workbook, workbook)
            .Add(p => p.OnDelete, range => workbook.NamedRanges.Remove(range)));

        // One row: delete it through the keyboard and the list becomes empty.
        cut.Find(".tm-spreadsheet-name-manager__table").KeyDown(new KeyboardEventArgs { Key = "Delete" });

        cut.FindAll("tbody tr").Should().BeEmpty();
        cut.FindAll(".tm-spreadsheet-name-manager__empty").Should().ContainSingle();
        cut.FindAll("tbody tr").Should().BeEmpty();

        foreach (var tool in cut.FindAll(".tm-spreadsheet-name-manager__tool"))
        {
            var label = tool.TextContent;
            if (label.Contains("Edit") || label.Contains("Delete"))
            {
                tool.HasAttribute("disabled").Should().BeTrue(
                    $"'{label.Trim()}' has nothing to act on once the list is empty");
            }
        }
    }

    [Fact]
    public void NameManager_RowDoubleClick_Edits()
    {
        var workbook = new SpreadsheetWorkbook();
        workbook.NamedRanges.Add(new SpreadsheetNamedRange { Name = "Sales", RefersTo = "A1" });

        SpreadsheetNamedRange? edited = null;
        var cut = Render<TmSpreadsheetNameManagerDialog>(parameters => parameters
            .Add(p => p.Workbook, workbook)
            .Add(p => p.OnEdit, range => edited = range));

        cut.Find("tbody tr").DoubleClick();

        edited.Should().NotBeNull();
        edited!.Name.Should().Be("Sales");
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static List<IElement> Options(IRenderedComponent<TmSpreadsheetFormatCellsDialog> cut)
        => cut.FindAll(".tm-fcd__listbox[role='listbox'] [role='option']").ToList();

    private List<object?> FocusTargets() => JSInterop.Invocations
        .Where(invocation => invocation.Identifier == FocusInvocation)
        .Select(invocation => invocation.Arguments[0])
        .ToList();

    private static string? FocusTargetId(object? target)
        => target is ElementReference reference ? reference.Id : null;

    private static void OpenInsertImageDialog(IRenderedComponent<TmSpreadsheet> cut)
    {
        cut.FindAll(".tm-spreadsheet-toolbar__tab")[1].Click(); // Insert tab
        cut.FindAll(".tm-spreadsheet-toolbar__button")
            .First(button => button.GetAttribute("title") == "Insert image")
            .Click();
        cut.FindAll(".tm-spreadsheet-dialog").Should().HaveCount(1);
    }

    private static void AddStopValidationRule(IRenderedComponent<TmSpreadsheet> cut)
    {
        cut.Instance.Workbook.ActiveSheet!.DataValidations.Add(new SpreadsheetDataValidation
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
    }

    /// <summary>
    /// The accessible name a screen reader would compute for a form control: <c>aria-label</c>,
    /// <c>aria-labelledby</c>, a <c>label[for]</c> that points at it, or a wrapping <c>&lt;label&gt;</c>.
    /// </summary>
    private static string? AccessibleName<TComponent>(IRenderedComponent<TComponent> cut, IElement element)
        where TComponent : IComponent
    {
        var ariaLabel = element.GetAttribute("aria-label");
        if (!string.IsNullOrWhiteSpace(ariaLabel))
        {
            return ariaLabel;
        }

        var labelledBy = element.GetAttribute("aria-labelledby");
        if (!string.IsNullOrWhiteSpace(labelledBy))
        {
            var text = string.Join(" ", labelledBy
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Select(id => cut.Find($"#{id}").TextContent.Trim()));
            if (!string.IsNullOrWhiteSpace(text))
            {
                return text;
            }
        }

        var id = element.GetAttribute("id");
        if (!string.IsNullOrWhiteSpace(id))
        {
            var label = cut.FindAll($"label[for='{id}']").FirstOrDefault();
            if (label is not null)
            {
                return label.TextContent.Trim();
            }
        }

        var ancestor = element.ParentElement;
        while (ancestor is not null)
        {
            if (string.Equals(ancestor.LocalName, "label", StringComparison.OrdinalIgnoreCase))
            {
                return ancestor.TextContent.Trim();
            }

            ancestor = ancestor.ParentElement;
        }

        return null;
    }

    /// <summary>
    /// The declaration body of the stylesheet rule whose selector stands alone on its own line —
    /// the formatting the package stylesheet uses, so a plain anchored regex is exact.
    /// </summary>
    private static string? RuleBody(string css, string selector)
    {
        // The selector may stand alone or be one part of a comma-separated list on the same line
        // (the interim dialog block groups the three cards' shared deltas).
        var match = Regex.Match(
            css,
            @"(?m)^[ \t]*(?:" + Regex.Escape(selector) + @"[ \t]*(?:,[^{}]*)?|(?:[^{},\r\n]+,[ \t]*)+"
            + Regex.Escape(selector) + @"[ \t]*(?:,[^{}]*)?)\{([^{}]*)\}");
        return match.Success ? match.Groups[1].Value : null;
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TempoBlazor.slnx")))
        {
            directory = directory.Parent;
        }

        directory.Should().NotBeNull("the repository root must be discoverable from the test output directory");
        return directory!.FullName;
    }
}
