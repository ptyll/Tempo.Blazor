using Tempo.Blazor.Components.Inputs;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.Components.Spreadsheet.Data;

namespace Tempo.Blazor.Components.Spreadsheet.Dialogs;

/// <summary>
/// The Paste Special dialog. Radio options choose what to paste (all / values / formulas / formats /
/// values+formats / all-except-borders), a dropdown picks an arithmetic operation, and checkboxes
/// toggle transpose and skip-blanks. Applying yields a <see cref="SpreadsheetPasteSpecialOptions"/>.
/// All text is localized.
/// </summary>
public partial class TmSpreadsheetSpecialPasteDialog
{
    private readonly string _idSuffix = Guid.NewGuid().ToString("N")[..8];

    private SpreadsheetPasteContent _content = SpreadsheetPasteContent.All;
    private SpreadsheetPasteOperation _operation = SpreadsheetPasteOperation.None;
    private bool _transpose;
    private bool _skipBlanks;

    /// <summary>Raised when the user applies the paste.</summary>
    [Parameter] public EventCallback<SpreadsheetPasteSpecialOptions> OnApply { get; set; }

    /// <summary>Raised when the dialog is dismissed.</summary>
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

    private string FirstContentDomId => $"spreadsheet-pastespecial-all-{_idSuffix}";

    /// <summary>What to paste — the reference's six choices, in its order.</summary>
    private List<RadioOption<SpreadsheetPasteContent>> ContentOptions =>
    [
        new(Loc["TmSpreadsheet_PasteSpecial_All"], SpreadsheetPasteContent.All),
        new(Loc["TmSpreadsheet_PasteSpecial_Values"], SpreadsheetPasteContent.Values),
        new(Loc["TmSpreadsheet_PasteSpecial_Formulas"], SpreadsheetPasteContent.Formulas),
        new(Loc["TmSpreadsheet_PasteSpecial_Formats"], SpreadsheetPasteContent.Formats),
        new(Loc["TmSpreadsheet_PasteSpecial_ValuesAndFormats"], SpreadsheetPasteContent.ValuesAndFormats),
        new(Loc["TmSpreadsheet_PasteSpecial_WithoutBorders"], SpreadsheetPasteContent.AllExceptBorders)
    ];

    /// <summary>The arithmetic operation, as a radio column next to the paste content.</summary>
    private List<RadioOption<SpreadsheetPasteOperation>> OperationOptions =>
    [
        new(Loc["TmSpreadsheet_PasteSpecial_OpNone"], SpreadsheetPasteOperation.None),
        new(Loc["TmSpreadsheet_PasteSpecial_OpAdd"], SpreadsheetPasteOperation.Add),
        new(Loc["TmSpreadsheet_PasteSpecial_OpSubtract"], SpreadsheetPasteOperation.Subtract),
        new(Loc["TmSpreadsheet_PasteSpecial_OpMultiply"], SpreadsheetPasteOperation.Multiply),
        new(Loc["TmSpreadsheet_PasteSpecial_OpDivide"], SpreadsheetPasteOperation.Divide)
    ];

    private void OnContentChanged(SpreadsheetPasteContent content) => _content = content;

    private void OnOperationChanged(SpreadsheetPasteOperation operation) => _operation = operation;

    private Task Apply()
    {
        var options = new SpreadsheetPasteSpecialOptions
        {
            Content = _content,
            Operation = _operation,
            Transpose = _transpose,
            SkipBlanks = _skipBlanks
        };

        return OnApply.InvokeAsync(options);
    }

    private Task Close() => OnClose.InvokeAsync();

}
