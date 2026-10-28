using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Components.Spreadsheet.Data;
using Tempo.Blazor.Components.Spreadsheet.Models;
using Tempo.Blazor.Abstractions.Layout;

namespace Tempo.Blazor.Components.Spreadsheet.Dialogs;

/// <summary>
/// The multi-level sort dialog. Each level chooses a column key, what to sort on (value / cell colour
/// / font colour) and the order. Supports add/remove of levels, a "my data has headers" toggle and a
/// case-sensitivity option. Applying produces a <see cref="SpreadsheetSortSpec"/>. All text is localized.
/// </summary>
public partial class TmSpreadsheetSortDialog
{
    private readonly string _idSuffix = Guid.NewGuid().ToString("N")[..8];

    private readonly List<SpreadsheetSortLevel> _levels = [];
    private bool _hasHeader = true;
    private bool _caseSensitive;

    /// <summary>The range being sorted.</summary>
    [Parameter, EditorRequired] public SpreadsheetRange Range { get; set; } = null!;

    /// <summary>Raised when the user applies the sort.</summary>
    [Parameter] public EventCallback<SpreadsheetSortSpec> OnApply { get; set; }

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

    private string AddLevelDomId => $"spreadsheet-sort-add-level-{_idSuffix}";

    /// <summary>The selectable column keys (within the range).</summary>
    private IReadOnlyList<(int Index, string Label)> ColumnChoices { get; set; } = [];

    protected override void OnParametersSet()
    {
        ColumnChoices = Enumerable.Range(Range.StartCol, Range.ColumnCount)
            .Select(c => (c, SpreadsheetRange.ColumnIndexToLetters(c)))
            .ToList();

        if (_levels.Count == 0)
            _levels.Add(new SpreadsheetSortLevel { KeyIndex = Range.StartCol });
    }

    private void AddLevel()
    {
        var used = _levels.Select(l => l.KeyIndex).ToHashSet();
        var next = ColumnChoices.Select(c => c.Index).FirstOrDefault(c => !used.Contains(c), Range.StartCol);
        _levels.Add(new SpreadsheetSortLevel { KeyIndex = next });
    }

    private void RemoveLevel(int index)
    {
        if (_levels.Count > 1)
            _levels.RemoveAt(index);
    }

    private Task Apply()
    {
        var spec = new SpreadsheetSortSpec(Range)
        {
            HasHeader = _hasHeader,
            Levels = _levels.Select(l => new SpreadsheetSortLevel
            {
                KeyIndex = l.KeyIndex,
                Direction = l.Direction,
                SortOn = l.SortOn,
                ColorKey = l.ColorKey,
                CaseSensitive = _caseSensitive
            }).ToList()
        };

        return OnApply.InvokeAsync(spec);
    }

    private Task Close() => OnClose.InvokeAsync();

}
