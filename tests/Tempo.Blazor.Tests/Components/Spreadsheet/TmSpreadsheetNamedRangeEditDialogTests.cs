using Bunit;
using FluentAssertions;
using Tempo.Blazor.Components.Spreadsheet.Dialogs;
using Tempo.Blazor.Components.Spreadsheet.Models;
using Tempo.Blazor.Tests.Localization;
using Xunit;

namespace Tempo.Blazor.Tests.Components.Spreadsheet;

public class TmSpreadsheetNamedRangeEditDialogTests : LocalizationTestBase
{
    [Fact]
    public void NewMode_Renders_TitleAndEmptyFields()
    {
        var workbook = new SpreadsheetWorkbook();
        var cut = Render<TmSpreadsheetNamedRangeEditDialog>(
            parameters => parameters.Add(p => p.Workbook, workbook));

        cut.Find(".tm-spreadsheet-named-range-edit__title").TextContent.Should().Be("New Name");
        cut.Find("#nr-name").GetAttribute("value").Should().BeNullOrEmpty();
        cut.Find("#nr-refers").GetAttribute("value").Should().BeNullOrEmpty();
    }

    [Fact]
    public void EditMode_Renders_PrePopulatedFields()
    {
        var workbook = new SpreadsheetWorkbook();
        var range = new SpreadsheetNamedRange { Name = "Sales", RefersTo = "A1:A10", Scope = NamedRangeScope.Sheet, SheetIndex = 0, Comment = "Q1" };

        var cut = Render<TmSpreadsheetNamedRangeEditDialog>(
            parameters => parameters
                .Add(p => p.Workbook, workbook)
                .Add(p => p.Range, range));

        cut.Find(".tm-spreadsheet-named-range-edit__title").TextContent.Should().Be("Edit Name");
        cut.Find("#nr-name").GetAttribute("value").Should().Be("Sales");
        cut.Find("#nr-refers").GetAttribute("value").Should().Be("A1:A10");
    }

    [Fact]
    public void Save_WithEmptyName_ShowsError()
    {
        var workbook = new SpreadsheetWorkbook();
        var cut = Render<TmSpreadsheetNamedRangeEditDialog>(
            parameters => parameters.Add(p => p.Workbook, workbook));

        cut.Find(".tm-spreadsheet-named-range-edit__btn--ok").Click();

        cut.FindAll(".tm-spreadsheet-named-range-edit__error").Count.Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public void Save_WithValidData_RaisesOnSave()
    {
        var workbook = new SpreadsheetWorkbook();
        SpreadsheetNamedRange? saved = null;

        var cut = Render<TmSpreadsheetNamedRangeEditDialog>(
            parameters => parameters
                .Add(p => p.Workbook, workbook)
                .Add(p => p.OnSave, r => saved = r));

        cut.Find("#nr-name").Input("Total");
        cut.Find("#nr-refers").Input("B1:B10");
        cut.Find(".tm-spreadsheet-named-range-edit__btn--ok").Click();

        saved.Should().NotBeNull();
        saved!.Name.Should().Be("Total");
        saved.RefersTo.Should().Be("B1:B10");
    }

    [Fact]
    public void Cancel_RaisesOnCancel()
    {
        var workbook = new SpreadsheetWorkbook();
        var fired = false;

        var cut = Render<TmSpreadsheetNamedRangeEditDialog>(
            parameters => parameters
                .Add(p => p.Workbook, workbook)
                .Add(p => p.OnCancel, () => fired = true));

        cut.Find(".tm-spreadsheet-named-range-edit__btn--cancel").Click();
        fired.Should().BeTrue();
    }

    // ── Keyboard activation ─────────────────────────────────────────────────
    // Ctrl+Enter -> save lives on the dialog root; the footer buttons and the
    // header close button isolate their keydowns so the browser's native
    // keydown -> click on a <button> can't also trigger the root shortcut.

    [Fact]
    public void CtrlEnter_OnSaveButton_SavesExactlyOnce()
    {
        var workbook = new SpreadsheetWorkbook();
        var saves = 0;
        var cut = Render<TmSpreadsheetNamedRangeEditDialog>(
            parameters => parameters
                .Add(p => p.Workbook, workbook)
                .Add(p => p.OnSave, _ => saves++));

        cut.Find("#nr-name").Input("Total");
        cut.Find("#nr-refers").Input("B1:B10");

        // keydown (native click would follow) — the actions group stops the
        // keydown before the root's Ctrl+Enter -> save case.
        cut.Find(".tm-spreadsheet-named-range-edit__btn--ok")
            .KeyDown(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "Enter", CtrlKey = true });
        cut.Find(".tm-spreadsheet-named-range-edit__btn--ok").Click();

        saves.Should().Be(1);
    }

    [Fact]
    public void Enter_OnCancelButton_Cancels_WithoutSaving()
    {
        var workbook = new SpreadsheetWorkbook();
        var saves = 0;
        var cancels = 0;
        var cut = Render<TmSpreadsheetNamedRangeEditDialog>(
            parameters => parameters
                .Add(p => p.Workbook, workbook)
                .Add(p => p.OnSave, _ => saves++)
                .Add(p => p.OnCancel, () => cancels++));

        cut.Find("#nr-name").Input("Total");
        cut.Find("#nr-refers").Input("B1:B10");

        cut.Find(".tm-spreadsheet-named-range-edit__btn--cancel")
            .KeyDown(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "Enter" });
        cut.Find(".tm-spreadsheet-named-range-edit__btn--cancel").Click();

        cancels.Should().Be(1);
        saves.Should().Be(0);
    }

    [Fact]
    public void Escape_OnSaveButton_CancelsOnce()
    {
        var workbook = new SpreadsheetWorkbook();
        var saves = 0;
        var cancels = 0;
        var cut = Render<TmSpreadsheetNamedRangeEditDialog>(
            parameters => parameters
                .Add(p => p.Workbook, workbook)
                .Add(p => p.OnSave, _ => saves++)
                .Add(p => p.OnCancel, () => cancels++));

        // Escape stays usable from the footer: the actions group handles it
        // locally and stops it from also reaching the root's Escape case.
        cut.Find(".tm-spreadsheet-named-range-edit__btn--ok")
            .KeyDown(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "Escape" });

        cancels.Should().Be(1);
        saves.Should().Be(0);
    }

    [Fact]
    public void CtrlEnter_InInput_StillSaves()
    {
        var workbook = new SpreadsheetWorkbook();
        SpreadsheetNamedRange? saved = null;
        var cut = Render<TmSpreadsheetNamedRangeEditDialog>(
            parameters => parameters
                .Add(p => p.Workbook, workbook)
                .Add(p => p.OnSave, r => saved = r));

        cut.Find("#nr-name").Input("Total");
        cut.Find("#nr-refers").Input("B1:B10");

        cut.Find("#nr-name")
            .KeyDown(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "Enter", CtrlKey = true });

        saved.Should().NotBeNull();
        saved!.Name.Should().Be("Total");
    }

    [Fact]
    public void CtrlEnter_OnCloseButton_Cancels_WithoutSaving()
    {
        var workbook = new SpreadsheetWorkbook();
        var saves = 0;
        var cancels = 0;
        var cut = Render<TmSpreadsheetNamedRangeEditDialog>(
            parameters => parameters
                .Add(p => p.Workbook, workbook)
                .Add(p => p.OnSave, _ => saves++)
                .Add(p => p.OnCancel, () => cancels++));

        cut.Find("#nr-name").Input("Total");
        cut.Find("#nr-refers").Input("B1:B10");

        cut.Find(".tm-spreadsheet-named-range-edit__close")
            .KeyDown(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "Enter", CtrlKey = true });
        cut.Find(".tm-spreadsheet-named-range-edit__close").Click();

        cancels.Should().Be(1);
        saves.Should().Be(0);
    }
}
