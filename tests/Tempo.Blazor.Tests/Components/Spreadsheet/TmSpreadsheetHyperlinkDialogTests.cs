using Bunit;
using Tempo.Blazor.Components.Feedback;
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

        cut.Find(".tm-modal-title").TextContent.Should().Be("Hyperlink");
        var options = cut.FindAll("#hl-type .tm-radio-segment");
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
    // The dialog's Ctrl+Enter -> save shortcut lives on the root; a chrome group bars its keydown
    // (@onkeydown:stopPropagation) so the browser's native keydown -> click on a <button> cannot also
    // trigger the root shortcut. bUnit proves the barrier by refusing to dispatch the keydown at all
    // (the element has no handler of its own) — that exception IS the observable proof.

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

        var save = cut.Find(".tm-spreadsheet-hyperlink__btn--ok");
        Assert.Throws<MissingEventHandlerException>(() => save.KeyDown(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "Enter", CtrlKey = true }));
        save.Click();

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

        var cancel = cut.Find(".tm-spreadsheet-hyperlink__btn--cancel");
        Assert.Throws<MissingEventHandlerException>(() => cancel.KeyDown(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "Enter" }));
        cancel.Click();

        cancels.Should().Be(1);
        saves.Should().Be(0);
    }

    [Fact]
    public async Task Escape_CancelsOnce_ThroughTheFocusScope()
    {
        var workbook = new SpreadsheetWorkbook();
        var saves = 0;
        var cancels = 0;
        var cut = Render<TmSpreadsheetHyperlinkDialog>(
            parameters => parameters
                .Add(p => p.Workbook, workbook)
                .Add(p => p.OnSave, _ => saves++)
                .Add(p => p.OnCancel, () => cancels++));

        // Escape is the dialog's own dismissal, wherever the focus sits: the focus scope's
        // document-level listener routes it back here exactly once. The footer buttons only fence
        // their keydown so Ctrl+Enter cannot double-fire the root's save shortcut.
        var scope = cut.FindComponent<TmFocusScope>();
        scope.Instance.CloseOnEscape.Should().BeTrue();
        await cut.InvokeAsync(() => scope.Instance.HandleFocusTrapEscapeAsync());

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

        // A keydown inside a field reaches the form wrapper by bubbling in the browser; bUnit does not
        // bubble, so the event is dispatched on the wrapper that owns the shortcut.
        cut.Find(".tm-spreadsheet-hyperlink__body")
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

        // The close button is the shared frame's: it sits outside the form's Ctrl+Enter wrapper, so
        // its keydown carries no handler and only its native click cancels.
        var close = cut.Find(".tm-modal-close");
        Assert.Throws<MissingEventHandlerException>(() => close.KeyDown(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "Enter", CtrlKey = true }));
        close.Click();

        cancels.Should().Be(1);
        saves.Should().Be(0);
    }
}
