using System.Linq;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Components.Feedback;
using Tempo.Blazor.Components.Spreadsheet;
using Tempo.Blazor.Components.Spreadsheet.Models;
using Tempo.Blazor.Tests.Localization;
using Xunit;

namespace Tempo.Blazor.Tests.Components.Spreadsheet;

/// <summary>
/// The two column-width / row-height dialogs of the canvas grid are dialogs of the package too, so the
/// phase-3 DoD ("no dialog of the package paints its own backdrop") covers them: they render the core
/// <see cref="TmModal"/> like the other twelve surfaces instead of the old
/// <c>.tm-spreadsheet-resize-dialog-backdrop</c> card. The class of the input and of both footer buttons
/// stays — the published tests and specs key off it.
/// </summary>
[Collection("SpreadsheetClipboard")]
public class SpreadsheetResizeDialogTmModalTests : LocalizationTestBase
{
    public static TheoryData<string, string> ResizeDialogs => new()
    {
        { "SetRowHeight", "Set row height" },
        { "SetColumnWidth", "Set column width" },
    };

    [Theory]
    [MemberData(nameof(ResizeDialogs))]
    public async Task ResizeDialog_UsesTmModal_WithDialogRole(string locKey, string label)
    {
        var (cut, grid) = await OpenResizeDialogAsync(locKey, label);

        cut.FindAll(".tm-modal-overlay.tm-spreadsheet-resize-dialog").Should().ContainSingle(
            $"{locKey} must render through the core TmModal");
        cut.FindAll(".tm-spreadsheet-resize-dialog-backdrop").Should().BeEmpty(
            "the package backdrop is gone");

        var scope = cut.Find(".tm-modal-overlay [role='dialog']");
        scope.GetAttribute("aria-modal").Should().Be("true");
        var labelledBy = scope.GetAttribute("aria-labelledby");
        labelledBy.Should().NotBeNullOrWhiteSpace("a dialog needs an accessible name");
        cut.Find($"[id='{labelledBy}']").TextContent.Should().Contain(label,
            "the frame header carries the dialog's own title");

        // The content contract the published tests key off survives.
        cut.Find(".tm-modal-body .tm-spreadsheet-resize-dialog__input").Should().NotBeNull();
        cut.Find(".tm-modal-footer .tm-spreadsheet-resize-dialog__btn--ok").Should().NotBeNull();
        cut.Find(".tm-modal-footer .tm-spreadsheet-resize-dialog__btn--cancel").Should().NotBeNull();

        // A desktop viewport is the centred card, never a sheet.
        cut.Find(".tm-modal-overlay").ClassList.Should().Contain("tm-modal--dialog");
    }

    [Theory]
    [MemberData(nameof(ResizeDialogs))]
    public async Task ResizeDialog_Escape_Closes_AndRestoresFocusToTheGrid(string locKey, string label)
    {
        var (cut, grid) = await OpenResizeDialogAsync(locKey, label);

        var gridRootId = grid.Find(".tm-spreadsheet-canvas-grid").Id;
        gridRootId.Should().NotBeNullOrWhiteSpace("the grid root is the focus-restore target");
        cut.Find(".tm-modal-overlay .tm-focus-scope").GetAttribute("data-restore-target")
            .Should().Be(gridRootId, "closing the dialog hands the focus back to the grid");

        var scope = cut.FindComponents<TmFocusScope>().Single();
        await cut.InvokeAsync(() => scope.Instance.HandleFocusTrapEscapeAsync());

        grid.FindAll(".tm-modal-overlay").Should().BeEmpty($"{locKey} closes on Escape");
        grid.FindAll(".tm-spreadsheet-resize-dialog-backdrop").Should().BeEmpty();
    }

    private async Task<(IRenderedComponent<TmSpreadsheet> Cut, IRenderedComponent<TmSpreadsheetCanvasGrid> Grid)>
        OpenResizeDialogAsync(string locKey, string label)
    {
        var workbook = new SpreadsheetWorkbook();
        var cut = Render<TmSpreadsheet>(p => p.Add(c => c.InitialWorkbook, workbook));
        var grid = cut.FindComponent<TmSpreadsheetCanvasGrid>();

        // The only path a user has: the grid's context menu entry. The menu is rendered by the HOST
        // (TmSpreadsheet) as a sibling of its layout container — the grid only reports the gesture, because a
        // position:fixed menu inside the container would be positioned against the card.
        await cut.InvokeAsync(() => grid.Instance.OnCanvasContextMenu(4, 4, 10, 10));
        cut.Render();
        cut.FindAll(".tm-spreadsheet-context-menu__item")
            .First(item => item.TextContent.Contains(label, StringComparison.Ordinal))
            .Click();

        return (cut, grid);
    }
}
