using Bunit;
using FluentAssertions;
using Tempo.Blazor.Components.Spreadsheet.Dialogs;
using Tempo.Blazor.Components.Spreadsheet.Models;
using Tempo.Blazor.Tests.Localization;
using Xunit;

namespace Tempo.Blazor.Tests.Components.Spreadsheet;

public class TmSpreadsheetHyperlinkDialogTests : LocalizationTestBase
{
    [Fact]
    public void Renders_TitleAndTypeOptions()
    {
        var workbook = new SpreadsheetWorkbook();
        var cut = Render<TmSpreadsheetHyperlinkDialog>(
            parameters => parameters.Add(p => p.Workbook, workbook));

        cut.Find(".tm-spreadsheet-hyperlink__title").TextContent.Should().Be("Hyperlink");
        var options = cut.FindAll("#hl-type option");
        options.Count.Should().Be(4);
    }

    [Fact]
    public void EditMode_PrePopulatesFields()
    {
        var workbook = new SpreadsheetWorkbook();
        var link = new SpreadsheetHyperlink
        {
            Kind = SpreadsheetHyperlinkKind.Web,
            Target = "https://example.com",
            Display = "Example",
            Tooltip = "Click"
        };

        var cut = Render<TmSpreadsheetHyperlinkDialog>(
            parameters => parameters
                .Add(p => p.Workbook, workbook)
                .Add(p => p.Hyperlink, link));

        cut.Find("#hl-target").GetAttribute("value").Should().Be("https://example.com");
    }

    [Fact]
    public void Save_RaisesOnSave_WithHyperlink()
    {
        var workbook = new SpreadsheetWorkbook();
        SpreadsheetHyperlink? saved = null;

        var cut = Render<TmSpreadsheetHyperlinkDialog>(
            parameters => parameters
                .Add(p => p.Workbook, workbook)
                .Add(p => p.OnSave, h => saved = h));

        cut.Find("#hl-target").Input("https://test.com");
        cut.Find("#hl-display").Change("Test");
        cut.Find(".tm-spreadsheet-hyperlink__btn--ok").Click();

        saved.Should().NotBeNull();
        saved!.Kind.Should().Be(SpreadsheetHyperlinkKind.Web);
        saved.Target.Should().Be("https://test.com");
        saved.Display.Should().Be("Test");
    }

    [Fact]
    public void Cancel_RaisesOnCancel()
    {
        var workbook = new SpreadsheetWorkbook();
        var fired = false;

        var cut = Render<TmSpreadsheetHyperlinkDialog>(
            parameters => parameters
                .Add(p => p.Workbook, workbook)
                .Add(p => p.OnCancel, () => fired = true));

        cut.Find(".tm-spreadsheet-hyperlink__btn--cancel").Click();
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
        var cut = Render<TmSpreadsheetHyperlinkDialog>(
            parameters => parameters
                .Add(p => p.Workbook, workbook)
                .Add(p => p.OnSave, _ => saves++));

        cut.Find("#hl-target").Input("https://test.com");

        cut.Find(".tm-spreadsheet-hyperlink__btn--ok")
            .KeyDown(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "Enter", CtrlKey = true });
        cut.Find(".tm-spreadsheet-hyperlink__btn--ok").Click();

        saves.Should().Be(1);
    }

    [Fact]
    public void Enter_OnCancelButton_Cancels_WithoutSaving()
    {
        var workbook = new SpreadsheetWorkbook();
        var saves = 0;
        var cancels = 0;
        var cut = Render<TmSpreadsheetHyperlinkDialog>(
            parameters => parameters
                .Add(p => p.Workbook, workbook)
                .Add(p => p.OnSave, _ => saves++)
                .Add(p => p.OnCancel, () => cancels++));

        cut.Find(".tm-spreadsheet-hyperlink__btn--cancel")
            .KeyDown(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "Enter" });
        cut.Find(".tm-spreadsheet-hyperlink__btn--cancel").Click();

        cancels.Should().Be(1);
        saves.Should().Be(0);
    }

    [Fact]
    public void Escape_OnSaveButton_CancelsOnce()
    {
        var workbook = new SpreadsheetWorkbook();
        var saves = 0;
        var cancels = 0;
        var cut = Render<TmSpreadsheetHyperlinkDialog>(
            parameters => parameters
                .Add(p => p.Workbook, workbook)
                .Add(p => p.OnSave, _ => saves++)
                .Add(p => p.OnCancel, () => cancels++));

        // Escape stays usable from the footer: the actions group handles it
        // locally and stops it from also reaching the root's Escape case.
        cut.Find(".tm-spreadsheet-hyperlink__btn--ok")
            .KeyDown(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "Escape" });

        cancels.Should().Be(1);
        saves.Should().Be(0);
    }

    [Fact]
    public void CtrlEnter_InInput_StillSaves()
    {
        var workbook = new SpreadsheetWorkbook();
        SpreadsheetHyperlink? saved = null;
        var cut = Render<TmSpreadsheetHyperlinkDialog>(
            parameters => parameters
                .Add(p => p.Workbook, workbook)
                .Add(p => p.OnSave, h => saved = h));

        cut.Find("#hl-target").Input("https://test.com");

        cut.Find("#hl-target")
            .KeyDown(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "Enter", CtrlKey = true });

        saved.Should().NotBeNull();
        saved!.Target.Should().Be("https://test.com");
    }

    [Fact]
    public void CtrlEnter_OnCloseButton_Cancels_WithoutSaving()
    {
        var workbook = new SpreadsheetWorkbook();
        var saves = 0;
        var cancels = 0;
        var cut = Render<TmSpreadsheetHyperlinkDialog>(
            parameters => parameters
                .Add(p => p.Workbook, workbook)
                .Add(p => p.OnSave, _ => saves++)
                .Add(p => p.OnCancel, () => cancels++));

        cut.Find(".tm-spreadsheet-hyperlink__close")
            .KeyDown(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "Enter", CtrlKey = true });
        cut.Find(".tm-spreadsheet-hyperlink__close").Click();

        cancels.Should().Be(1);
        saves.Should().Be(0);
    }
}
