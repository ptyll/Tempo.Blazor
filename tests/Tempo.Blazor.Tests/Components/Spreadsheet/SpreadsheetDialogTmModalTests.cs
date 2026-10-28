using System.Linq;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.Components.Feedback;
using Tempo.Blazor.Components.Spreadsheet;
using Tempo.Blazor.Components.Spreadsheet.Data;
using Tempo.Blazor.Components.Spreadsheet.Dialogs;
using Tempo.Blazor.Components.Spreadsheet.Models;
using Tempo.Blazor.Tests.Localization;
using Xunit;

namespace Tempo.Blazor.Tests.Components.Spreadsheet;

/// <summary>
/// Every Spreadsheet dialog rides the ONE core surface (G6 of the redesign plan): the core
/// <see cref="TmModal"/> owns the fixed overlay, the dialog role, aria-modal, the initial focus, the
/// document-level Escape, the focus restore and the mobile presentation. The package contributes only
/// content, footer actions and the localized words.
/// </summary>
[Collection("SpreadsheetClipboard")]
public class SpreadsheetDialogTmModalTests : LocalizationTestBase
{
    public enum DialogKind
    {
        Sort,
        CustomFilter,
        RemoveDuplicates,
        SpecialPaste,
        TextToColumns,
        Hyperlink,
        NameManager,
        NamedRangeEdit,
        DataValidation,
        FormatCells,
        InsertImage,
        ValidationAlertStop,
        ValidationAlertWarning
    }

    private const string GridRootId = "spreadsheet-grid-root";

    // ── The single dialog frame ───────────────────────────────────────────────

    public static TheoryData<DialogKind> AllDialogs => new(Enum.GetValues<DialogKind>());

    [Theory]
    [MemberData(nameof(AllDialogs))]
    public void Dialog_UsesTmModal_WithAriaModal(DialogKind kind)
    {
        var cut = RenderDialog(kind, RestoreFocusTargetId: GridRootId);

        // The overlay is the core TmModal's, not a package backdrop.
        cut.FindAll(".tm-modal-overlay").Should().ContainSingle($"{kind} must render through the core TmModal");
        cut.FindAll(".tm-spreadsheet-dialog-backdrop").Should().BeEmpty("the package backdrop class is gone");
        cut.FindAll("[class*='__backdrop']").Should().BeEmpty("no dialog keeps its own backdrop");

        // The dialog contract lives on the focus-scope root inside the overlay.
        var scope = cut.Find(".tm-modal-overlay [role='dialog'], .tm-modal-overlay [role='alertdialog']");
        scope.GetAttribute("aria-modal").Should().Be("true");
        var labelledBy = scope.GetAttribute("aria-labelledby");
        labelledBy.Should().NotBeNullOrWhiteSpace("a dialog needs an accessible name");
        cut.Find($"[id='{labelledBy}']").TextContent.Should().NotBeNullOrWhiteSpace();

        // The package's own root class survives on the overlay (E2E selector contract).
        cut.Find(".tm-modal-overlay").ClassList.Should().Contain(ExpectedRootClass(kind));

        // The title rides the shared header, not a per-dialog one.
        cut.FindAll(".tm-modal-title").Should().ContainSingle();
    }

    [Theory]
    [MemberData(nameof(AllDialogs))]
    public void Dialog_Close_RestoresFocusToGrid(DialogKind kind)
    {
        var cut = RenderDialog(kind, RestoreFocusTargetId: GridRootId);

        var scope = cut.Find(".tm-modal-overlay .tm-focus-scope");
        scope.GetAttribute("data-restore-target").Should().Be(GridRootId,
            $"{kind} must hand the focus back to the grid, like every other Surface of the frame");
    }

    [Theory]
    [MemberData(nameof(AllDialogs))]
    public void Dialog_Escape_Closes(DialogKind kind)
    {
        var closed = false;
        var cut = RenderDialog(kind, onClosed: () => closed = true);

        var scope = cut.FindComponents<TmFocusScope>().Single();
        cut.InvokeAsync(() => scope.Instance.HandleFocusTrapEscapeAsync());

        closed.Should().BeTrue($"{kind} closes on Escape through the core claim");
    }

    [Theory]
    [MemberData(nameof(AllDialogs))]
    public void Dialog_MobileLayout_UsesSheetPresentation(DialogKind kind)
    {
        var cut = RenderDialog(kind, layoutMode: TmLayoutMode.Mobile);

        var overlay = cut.Find(".tm-modal-overlay");
        if (ExpectedMobilePresentation(kind) == MobilePresentation.Fullscreen)
        {
            overlay.ClassList.Should().Contain("tm-modal--fullscreen-presentation",
                $"{kind} is a tall surface: on a phone it takes the whole viewport");
        }
        else
        {
            overlay.ClassList.Should().Contain("tm-modal--sheet",
                $"{kind} is a short form: on a phone it is a bottom sheet");
            cut.Find(".tm-modal-overlay .tm-sheet__panel").Should().NotBeNull();
        }
    }

    [Theory]
    [MemberData(nameof(AllDialogs))]
    public void Dialog_DesktopLayout_IsADialog(DialogKind kind)
    {
        var cut = RenderDialog(kind, layoutMode: TmLayoutMode.Desktop);

        cut.Find(".tm-modal-overlay").ClassList.Should().Contain("tm-modal--dialog");
        cut.Find(".tm-modal-overlay").ClassList.Should().NotContain("tm-modal--sheet");
    }

    [Fact]
    public void Dialog_FormatCells_KeepsItsCategoryListbox()
    {
        // The listbox contract from wave 0 survives the frame swap.
        var cut = RenderDialog(DialogKind.FormatCells);

        var listbox = cut.Find(".tm-fcd__listbox[role='listbox']");
        var options = listbox.QuerySelectorAll("[role='option']");
        options.Should().HaveCount(8);
        options[0].GetAttribute("aria-selected").Should().Be("true");
        options[0].GetAttribute("tabindex").Should().Be("0");
    }

    [Fact]
    public void DataValidationDialog_Title_HasNoEllipsis()
    {
        // The toolbar command is "Data validation..." (it opens something); the DIALOG title is the
        // plain name. One key, `TmSpreadsheet_Validation_Title`, shared with the alert (phase 4).
        var cut = RenderDialog(DialogKind.DataValidation);
        var title = cut.Find(".tm-modal-title").TextContent.Trim();

        title.Should().NotEndWith("...", "a dialog title is not a command label");
        title.Should().NotContain("…");
    }

    // ── Keyboard conventions preserved ────────────────────────────────────────

    [Fact]
    public void Dialog_CtrlEnter_Applies_NamedRangeEdit()
    {
        SpreadsheetNamedRange? saved = null;
        var workbook = new SpreadsheetWorkbook();
        var cut = Render<TmSpreadsheetNamedRangeEditDialog>(p => p
            .Add(c => c.Workbook, workbook)
            .Add(c => c.OnSave, EventCallback.Factory.Create<SpreadsheetNamedRange>(this, r => saved = r)));

        cut.Find("#nr-name").Input("Range_1");
        cut.Find("#nr-refers").Input("=Sheet1!A1:A3");
        cut.Find(".tm-spreadsheet-named-range-edit__body").KeyDown(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs
        {
            Key = "Enter",
            CtrlKey = true
        });

        saved.Should().NotBeNull("Ctrl+Enter applies the dialog, the same convention as before the frame swap");
        saved!.Name.Should().Be("Range_1");
    }

    [Fact]
    public void Dialog_CtrlEnter_Applies_Hyperlink()
    {
        SpreadsheetHyperlink? saved = null;
        var workbook = new SpreadsheetWorkbook();
        var cut = Render<TmSpreadsheetHyperlinkDialog>(p => p
            .Add(c => c.Workbook, workbook)
            .Add(c => c.OnSave, EventCallback.Factory.Create<SpreadsheetHyperlink>(this, h => saved = h)));

        cut.Find("#hl-target").Input("https://example.com");
        cut.Find(".tm-spreadsheet-hyperlink__body").KeyDown(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs
        {
            Key = "Enter",
            CtrlKey = true
        });

        saved.Should().NotBeNull();
        saved!.Target.Should().Be("https://example.com");
    }

    [Fact]
    public void Dialog_NamedRangeEdit_InitialFocus_IsTheNameField()
    {
        var workbook = new SpreadsheetWorkbook();
        var cut = Render<TmSpreadsheetNamedRangeEditDialog>(p => p.Add(c => c.Workbook, workbook));

        cut.Find(".tm-modal-overlay .tm-focus-scope").GetAttribute("data-initial-focus").Should().Be("nr-name");
    }

    [Fact]
    public void Dialog_Hyperlink_InitialFocus_IsTheTargetField()
    {
        var workbook = new SpreadsheetWorkbook();
        var cut = Render<TmSpreadsheetHyperlinkDialog>(p => p.Add(c => c.Workbook, workbook));

        cut.Find(".tm-modal-overlay .tm-focus-scope").GetAttribute("data-initial-focus").Should().Be("hl-target");
    }

    // ── Stacked dialogs: two active scopes, Escape closes the top one only ─────

    [Fact]
    public void Dialog_Stacked_NamedRangeEditOverNameManager_EscClosesTopOnly()
    {
        var cut = Render<TmSpreadsheet>();
        // The Name Manager lives on the Data tab of the ribbon.
        cut.FindAll(".tm-spreadsheet-toolbar__tab").First(t => t.TextContent.Contains("Data")).Click();
        cut.Find("button[title='Name Manager']").Click();
        cut.Find(".tm-spreadsheet-name-manager__toolbar button").Click();

        cut.FindAll("[role='dialog']").Should().HaveCount(2,
            "the Name Manager stays mounted under the Named Range editor");
        cut.FindAll(".tm-modal-overlay").Should().HaveCount(2);

        // Two active scopes are siblings; Escape reaches only the topmost one (the editor).
        var scopes = cut.FindComponents<TmFocusScope>();
        scopes.Should().HaveCount(2);
        var top = scopes[^1];
        cut.InvokeAsync(() => top.Instance.HandleFocusTrapEscapeAsync());

        cut.FindAll(".tm-spreadsheet-named-range-edit").Should().BeEmpty("Escape closed the editor");
        cut.FindAll(".tm-spreadsheet-name-manager").Should().ContainSingle("the manager underneath stays open");

        // ... and only one backdrop paints, so the page does not dim twice.
        cut.FindAll(".tm-modal-overlay").Should().ContainSingle();
    }

    // ── Insert Image ─────────────────────────────────────────────────────────

    [Fact]
    public void InsertImageDialog_ApplyDisabled_ForInvalidUrl()
    {
        var cut = Render<TmSpreadsheetInsertImageDialog>();

        var apply = cut.Find(".tm-spreadsheet-insert-image__btn--ok");
        apply.HasAttribute("disabled").Should().BeTrue("an empty URL cannot be applied");

        var input = cut.Find(".tm-spreadsheet-insert-image__input");
        input.GetAttribute("inputmode").Should().Be("url");
        input.GetAttribute("aria-label").Should().NotBeNullOrWhiteSpace();

        foreach (var invalid in new[] { "not a url", "ftp://example.com/a.png", "javascript:alert(1)", "//example.com/a.png" })
        {
            input.Input(invalid);
            cut.Find(".tm-spreadsheet-insert-image__btn--ok").HasAttribute("disabled").Should()
                .BeTrue($"'{invalid}' is not an http(s)/data image URL");
        }

        foreach (var valid in new[] { "https://example.com/a.png", "http://example.com/a.png", "data:image/png;base64,iVBORw0KGgo=" })
        {
            input.Input(valid);
            cut.Find(".tm-spreadsheet-insert-image__btn--ok").HasAttribute("disabled").Should()
                .BeFalse($"'{valid}' is an acceptable image URL");
        }
    }

    [Fact]
    public void InsertImageDialog_Apply_RaisesTheUrl()
    {
        string? applied = null;
        var cut = Render<TmSpreadsheetInsertImageDialog>(p => p
            .Add(c => c.OnApply, EventCallback.Factory.Create<string>(this, u => applied = u)));

        cut.Find(".tm-spreadsheet-insert-image__input").Input("https://example.com/a.png");
        cut.Find(".tm-spreadsheet-insert-image__btn--ok").Click();

        applied.Should().Be("https://example.com/a.png");
    }

    [Fact]
    public void InsertImageDialog_EnterApplies_AndEscapeCancels()
    {
        string? applied = null;
        var cancelled = false;
        var cut = Render<TmSpreadsheetInsertImageDialog>(p => p
            .Add(c => c.OnApply, EventCallback.Factory.Create<string>(this, u => applied = u))
            .Add(c => c.OnCancel, EventCallback.Factory.Create(this, () => cancelled = true)));

        var input = cut.Find(".tm-spreadsheet-insert-image__input");
        input.Input("https://example.com/a.png");
        input.KeyDown(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "Enter" });
        applied.Should().Be("https://example.com/a.png", "Enter applies from the URL field");

        cut.InvokeAsync(() => cut.FindComponent<TmFocusScope>().Instance.HandleFocusTrapEscapeAsync());
        cancelled.Should().BeTrue("Escape cancels the dialog");
    }

    // ── Validation alert ─────────────────────────────────────────────────────

    [Theory]
    [InlineData(SpreadsheetValidationErrorStyle.Stop, "tm-spreadsheet-alert--stop")]
    [InlineData(SpreadsheetValidationErrorStyle.Warning, "tm-spreadsheet-alert--warning")]
    [InlineData(SpreadsheetValidationErrorStyle.Information, "tm-spreadsheet-alert--information")]
    public void ValidationAlert_Severity_RendersIconAndButtons(SpreadsheetValidationErrorStyle severity, string modifier)
    {
        var cut = Render<TmSpreadsheetValidationAlert>(p => p
            .Add(c => c.Severity, severity)
            .Add(c => c.Title, "Title")
            .Add(c => c.Message, "Message"));

        cut.Find(".tm-spreadsheet-alert").ClassList.Should().Contain(modifier);
        cut.Find(".tm-spreadsheet-alert__icon").Should().NotBeNull("every severity paints its own icon");
        cut.Find(".tm-spreadsheet-alert__message").TextContent.Should().Be("Message");

        // One surface, one dialog frame: the alert is an alertdialog on TmModal.
        var scope = cut.Find(".tm-modal-overlay [role='alertdialog']");
        scope.GetAttribute("aria-modal").Should().Be("true");
        scope.GetAttribute("aria-describedby").Should().NotBeNullOrWhiteSpace();

        cut.FindAll(".tm-modal-close").Should().BeEmpty("an alert never grows a third close path");
    }

    [Fact]
    public void ValidationAlert_Warning_ShowsKeepValue()
    {
        var primary = false;
        var cancel = false;
        var cut = Render<TmSpreadsheetValidationAlert>(p => p
            .Add(c => c.Severity, SpreadsheetValidationErrorStyle.Warning)
            .Add(c => c.OnPrimary, EventCallback.Factory.Create(this, () => primary = true))
            .Add(c => c.OnCancel, EventCallback.Factory.Create(this, () => cancel = true)));

        var primaryButton = cut.Find(".tm-spreadsheet-alert__actions button:last-of-type");
        primaryButton.TextContent.Trim().Should().Be("Use anyway");
        cut.FindAll(".tm-spreadsheet-alert__actions button").Should().HaveCount(2);

        primaryButton.Click();
        primary.Should().BeTrue("the primary action keeps the value");
        cancel.Should().BeFalse();
    }

    [Fact]
    public void ValidationAlert_Stop_HasOnlyAcknowledge_AndIgnoresTheBackdrop()
    {
        var primary = false;
        var cancel = false;
        var cut = Render<TmSpreadsheetValidationAlert>(p => p
            .Add(c => c.Severity, SpreadsheetValidationErrorStyle.Stop)
            .Add(c => c.OnPrimary, EventCallback.Factory.Create(this, () => primary = true))
            .Add(c => c.OnCancel, EventCallback.Factory.Create(this, () => cancel = true)));

        cut.FindAll(".tm-spreadsheet-alert__actions button").Should().ContainSingle(
            "a Stop alert has one way out: acknowledge (or Escape)");
        cut.Find(".tm-spreadsheet-alert__actions button").TextContent.Trim().Should().Be("Got it");

        cut.Find(".tm-modal-overlay").Click();
        cancel.Should().BeFalse("a Stop alert must not close on a backdrop click");
    }

    [Fact]
    public void ValidationAlert_Information_UsesItsOwnDefaults()
    {
        var cut = Render<TmSpreadsheetValidationAlert>(p => p
            .Add(c => c.Severity, SpreadsheetValidationErrorStyle.Information));

        // Information no longer shares the Warning path: its own title/message defaults.
        cut.Find(".tm-modal-title").TextContent.Should().Be("Note");
        cut.Find(".tm-spreadsheet-alert__message").TextContent.Should().Be("Just so you know.");
        cut.Find(".tm-spreadsheet-alert").ClassList.Should().Contain("tm-spreadsheet-alert--information");
    }

    [Fact]
    public void ValidationAlert_WithoutARuleMessage_UsesTheSeverityDefaultMessage()
    {
        // A rule that carries only a style (the common case) must still explain itself: the message
        // falls back to the severity's localized default, never to an empty paragraph.
        var cut = Render<TmSpreadsheetValidationAlert>(p => p
            .Add(c => c.Severity, SpreadsheetValidationErrorStyle.Stop));

        cut.Find(".tm-spreadsheet-alert__message").TextContent.Trim()
            .Should().Be("The value you entered is not valid.");
        cut.Find(".tm-modal-title").TextContent.Trim().Should().Be("Invalid value");

        // The Data validation dialog seeds a rule's title/message as EMPTY strings, not null: an empty
        // string must fall back too, or the alert paints no title (and breaks aria-labelledby).
        var empty = Render<TmSpreadsheetValidationAlert>(p => p
            .Add(c => c.Severity, SpreadsheetValidationErrorStyle.Stop)
            .Add(c => c.Title, string.Empty)
            .Add(c => c.Message, string.Empty));
        empty.Find(".tm-modal-title").TextContent.Trim().Should().Be("Invalid value");
        empty.Find(".tm-spreadsheet-alert__message").TextContent.Trim()
            .Should().Be("The value you entered is not valid.");
    }

    [Fact]
    public void ValidationAlert_Warning_UsesItsOwnDefaults()
    {
        var cut = Render<TmSpreadsheetValidationAlert>(p => p
            .Add(c => c.Severity, SpreadsheetValidationErrorStyle.Warning));

        cut.Find(".tm-modal-title").TextContent.Should().Be("Value outside the recommended range");
        cut.Find(".tm-spreadsheet-alert__message").TextContent.Should()
            .Be("Keep the value anyway?");
    }

    // ── Find / Replace stays NON-modal (Q5) ──────────────────────────────────

    [Fact]
    public void FindReplace_Desktop_StaysNonModal()
    {
        var cut = Render<TmSpreadsheetFindReplaceDialog>(p => p
            .Add(c => c.LayoutMode, TmLayoutMode.Desktop));

        cut.FindAll(".tm-modal-overlay").Should().BeEmpty("Find/Replace is not a modal: the grid stays usable");
        cut.FindAll("[role='dialog']").Should().ContainSingle();
        cut.Find(".tm-spreadsheet-find").Should().NotBeNull();
        cut.FindAll(".tm-drawer__panel").Should().BeEmpty();
    }

    [Fact]
    public void FindReplace_Mobile_RendersANonModalBottomSheet()
    {
        var cut = Render<TmSpreadsheetFindReplaceDialog>(p => p
            .Add(c => c.LayoutMode, TmLayoutMode.Mobile));

        var panel = cut.Find(".tm-drawer__panel");
        panel.ClassList.Should().Contain("tm-sheet__panel");
        cut.Find(".tm-spreadsheet-find").Should().NotBeNull();

        // Non-modal: no backdrop, the grid stays operable.
        cut.FindAll(".tm-drawer__overlay").Should().BeEmpty();
        cut.FindAll(".tm-modal-overlay").Should().BeEmpty();
        cut.Find(".tm-focus-scope").GetAttribute("aria-modal").Should().NotBe("true");
    }

    [Fact]
    public async Task FindReplace_Mobile_Escape_ClosesOnlyOnce()
    {
        // One Escape = one close. The panel's own root handler is the single path; the sheet's focus
        // scope must have its own Escape switch OFF, or the drawer closes on top of it (two OnClose
        // per press, and the host closed the panel twice).
        var closes = 0;
        var cut = Render<TmSpreadsheetFindReplaceDialog>(p => p
            .Add(c => c.LayoutMode, TmLayoutMode.Mobile)
            .Add(c => c.OnClose, EventCallback.Factory.Create(this, () => closes++)));

        cut.Find(".tm-spreadsheet-find").KeyDown(
            new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "Escape" });
        closes.Should().Be(1, "the panel's own handler closes on Escape");

        // The sheet's trap (the second, now disabled path) must not add another close.
        var scope = cut.FindComponents<TmFocusScope>().Single();
        await cut.InvokeAsync(() => scope.Instance.HandleFocusTrapEscapeAsync());
        closes.Should().Be(1, "the sheet must not close itself on top of the panel's handler");
    }

    [Fact]
    public void FindReplace_KeepsItsKeyboardContract()
    {
        var closed = false;
        var cut = Render<TmSpreadsheetFindReplaceDialog>(p => p
            .Add(c => c.LayoutMode, TmLayoutMode.Desktop)
            .Add(c => c.OnClose, EventCallback.Factory.Create(this, () => closed = true)));

        cut.Find(".tm-spreadsheet-find__query").KeyDown(
            new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "Escape" });

        closed.Should().BeTrue("Escape still closes the find panel");
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private IRenderedComponent<IComponent> RenderDialog(
        DialogKind kind,
        string? RestoreFocusTargetId = null,
        Action? onClosed = null,
        TmLayoutMode? layoutMode = null)
    {
        var restore = RestoreFocusTargetId;
        var closed = EventCallback.Factory.Create(this, () => onClosed?.Invoke());
        var mode = layoutMode ?? TmLayoutMode.Desktop;

        return kind switch
        {
            DialogKind.Sort => Render<TmSpreadsheetSortDialog>(p => p
                .Add(c => c.Range, new SpreadsheetRange(0, 0, 9, 2))
                .Add(c => c.LayoutMode, mode)
                .Add(c => c.RestoreFocusTargetId, restore)
                .Add(c => c.OnClose, closed)),
            DialogKind.CustomFilter => Render<TmSpreadsheetCustomFilterDialog>(p => p
                .Add(c => c.ColumnIndex, 0)
                .Add(c => c.LayoutMode, mode)
                .Add(c => c.RestoreFocusTargetId, restore)
                .Add(c => c.OnClose, closed)),
            DialogKind.RemoveDuplicates => Render<TmSpreadsheetRemoveDuplicatesDialog>(p => p
                .Add(c => c.Range, new SpreadsheetRange(0, 0, 9, 2))
                .Add(c => c.LayoutMode, mode)
                .Add(c => c.RestoreFocusTargetId, restore)
                .Add(c => c.OnClose, closed)),
            DialogKind.SpecialPaste => Render<TmSpreadsheetSpecialPasteDialog>(p => p
                .Add(c => c.LayoutMode, mode)
                .Add(c => c.RestoreFocusTargetId, restore)
                .Add(c => c.OnClose, closed)),
            DialogKind.TextToColumns => Render<TmSpreadsheetTextToColumnsDialog>(p => p
                .Add(c => c.SourceRows, new[] { "a;b;c" })
                .Add(c => c.LayoutMode, mode)
                .Add(c => c.RestoreFocusTargetId, restore)
                .Add(c => c.OnClose, closed)),
            DialogKind.Hyperlink => Render<TmSpreadsheetHyperlinkDialog>(p => p
                .Add(c => c.Workbook, new SpreadsheetWorkbook())
                .Add(c => c.LayoutMode, mode)
                .Add(c => c.RestoreFocusTargetId, restore)
                .Add(c => c.OnCancel, closed)),
            DialogKind.NameManager => Render<TmSpreadsheetNameManagerDialog>(p => p
                .Add(c => c.Workbook, new SpreadsheetWorkbook())
                .Add(c => c.LayoutMode, mode)
                .Add(c => c.RestoreFocusTargetId, restore)
                .Add(c => c.OnClose, closed)),
            DialogKind.NamedRangeEdit => Render<TmSpreadsheetNamedRangeEditDialog>(p => p
                .Add(c => c.Workbook, new SpreadsheetWorkbook())
                .Add(c => c.LayoutMode, mode)
                .Add(c => c.RestoreFocusTargetId, restore)
                .Add(c => c.OnCancel, closed)),
            DialogKind.DataValidation => Render<TmSpreadsheetDataValidationDialog>(p => p
                .Add(c => c.Validation, new SpreadsheetDataValidation())
                .Add(c => c.LayoutMode, mode)
                .Add(c => c.RestoreFocusTargetId, restore)
                .Add(c => c.OnClose, closed)),
            DialogKind.FormatCells => Render<TmSpreadsheetFormatCellsDialog>(p => p
                .Add(c => c.Style, new SpreadsheetCellStyle())
                .Add(c => c.LayoutMode, mode)
                .Add(c => c.RestoreFocusTargetId, restore)
                .Add(c => c.OnClose, closed)),
            DialogKind.InsertImage => Render<TmSpreadsheetInsertImageDialog>(p => p
                .Add(c => c.LayoutMode, mode)
                .Add(c => c.RestoreFocusTargetId, restore)
                .Add(c => c.OnCancel, closed)),
            DialogKind.ValidationAlertStop => Render<TmSpreadsheetValidationAlert>(p => p
                .Add(c => c.Severity, SpreadsheetValidationErrorStyle.Stop)
                .Add(c => c.Title, "Invalid value")
                .Add(c => c.Message, "Not valid.")
                .Add(c => c.LayoutMode, mode)
                .Add(c => c.RestoreFocusTargetId, restore)
                .Add(c => c.OnPrimary, closed)),
            DialogKind.ValidationAlertWarning => Render<TmSpreadsheetValidationAlert>(p => p
                .Add(c => c.Severity, SpreadsheetValidationErrorStyle.Warning)
                .Add(c => c.Title, "Outside the range")
                .Add(c => c.Message, "Keep it?")
                .Add(c => c.LayoutMode, mode)
                .Add(c => c.RestoreFocusTargetId, restore)
                .Add(c => c.OnCancel, closed)),
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
    }

    private static string ExpectedRootClass(DialogKind kind) => kind switch
    {
        DialogKind.Sort => "tm-spreadsheet-sort",
        DialogKind.CustomFilter => "tm-spreadsheet-custom-filter",
        DialogKind.RemoveDuplicates => "tm-spreadsheet-dedup",
        DialogKind.SpecialPaste => "tm-spreadsheet-pastespecial",
        DialogKind.TextToColumns => "tm-spreadsheet-t2c",
        DialogKind.Hyperlink => "tm-spreadsheet-hyperlink",
        DialogKind.NameManager => "tm-spreadsheet-name-manager",
        DialogKind.NamedRangeEdit => "tm-spreadsheet-named-range-edit",
        DialogKind.DataValidation => "tm-dvd",
        DialogKind.FormatCells => "tm-fcd",
        DialogKind.InsertImage => "tm-spreadsheet-insert-image",
        DialogKind.ValidationAlertStop or DialogKind.ValidationAlertWarning => "tm-spreadsheet-alert",
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    private static MobilePresentation ExpectedMobilePresentation(DialogKind kind) => kind switch
    {
        DialogKind.FormatCells or DialogKind.TextToColumns or DialogKind.NameManager
            => MobilePresentation.Fullscreen,
        _ => MobilePresentation.Sheet
    };
}
