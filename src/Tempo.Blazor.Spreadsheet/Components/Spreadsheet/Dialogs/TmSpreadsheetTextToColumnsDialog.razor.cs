using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Tempo.Blazor.Components.Spreadsheet.Data;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.Components.Feedback;

namespace Tempo.Blazor.Components.Spreadsheet.Dialogs;

/// <summary>
/// The multi-step Text to Columns wizard. Step 1 picks the split type (delimited / fixed width),
/// step 2 configures the delimiters (or break positions) with a live preview, and step 3 chooses the
/// target format of each produced column. The preview updates live as options change. Applying yields
/// a <see cref="SpreadsheetTextToColumnsResult"/>. All text is localized.
/// </summary>
public partial class TmSpreadsheetTextToColumnsDialog
{
    private int _step = 1;

    private readonly SpreadsheetSeparatorOptions _options = new() { Comma = true };
    private string _otherDelimiter = string.Empty;
    private bool _useOther;
    private string _qualifier = "\"";
    private string _fixedBreaks = string.Empty;
    private readonly Dictionary<int, SpreadsheetColumnFormat> _columnFormats = new();

    /// <summary>The raw text of each source row to be split (used for the live preview).</summary>
    [Parameter, EditorRequired] public IReadOnlyList<string> SourceRows { get; set; } = [];

    /// <summary>Raised when the user finishes the wizard.</summary>
    [Parameter] public EventCallback<SpreadsheetTextToColumnsResult> OnApply { get; set; }

    /// <summary>Raised when the dialog is dismissed.</summary>
    [Parameter] public EventCallback OnClose { get; set; }

    /// <summary>
    /// The id of the element focus returns to on close — the host spreadsheet passes its grid root so
    /// focus lands on the grid. Null falls back to the element focused before the dialog opened.
    /// </summary>
    [Parameter] public string? RestoreFocusTargetId { get; set; }

    /// <summary>
    /// The layout the dialog renders. <see cref="TmLayoutMode.Auto"/> follows the viewport scope (a
    /// fullscreen panel on the narrow one); a forced value pins the mode without measuring, so a test
    /// can render that presentation without a DOM.
    /// </summary>
    [Parameter] public TmLayoutMode LayoutMode { get; set; } = TmLayoutMode.Auto;


    /// <summary>The number of preview rows shown (capped for readability).</summary>
    private const int MaxPreviewRows = 8;

    private SpreadsheetSeparatorOptions BuildOptions()
    {
        _options.OtherDelimiter = _useOther && _otherDelimiter.Length > 0 ? _otherDelimiter : null;
        _options.TextQualifier = string.IsNullOrEmpty(_qualifier) ? null : _qualifier[0];
        _options.FixedWidthBreaks = ParseBreaks(_fixedBreaks);
        return _options;
    }

    private static List<int> ParseBreaks(string text)
    {
        var result = new List<int>();
        foreach (var part in text.Split([',', ' ', ';'], StringSplitOptions.RemoveEmptyEntries))
            if (int.TryParse(part, out var n) && n > 0)
                result.Add(n);
        return result;
    }

    private IReadOnlyList<IReadOnlyList<string>> Preview()
    {
        var rows = SourceRows.Take(MaxPreviewRows).ToList();
        return rows.Count == 0
            ? []
            : SpreadsheetTextToColumns.Split(rows, BuildOptions());
    }

    private int PreviewColumnCount()
    {
        var preview = Preview();
        return preview.Count == 0 ? 0 : preview.Max(r => r.Count);
    }

    private SpreadsheetColumnFormat FormatFor(int col)
        => _columnFormats.TryGetValue(col, out var fmt) ? fmt : SpreadsheetColumnFormat.General;

    private void SetFormat(int col, SpreadsheetColumnFormat fmt) => _columnFormats[col] = fmt;

    private void OnFormatChanged(int col, ChangeEventArgs e)
    {
        if (Enum.TryParse<SpreadsheetColumnFormat>(e.Value?.ToString(), out var fmt))
            SetFormat(col, fmt);
    }

    private void SetMode(SpreadsheetTextToColumnsMode mode) => _options.Mode = mode;

    private bool IsMode(SpreadsheetTextToColumnsMode mode) => _options.Mode == mode;

    private void Next()
    {
        if (_step < 3)
            _step++;
    }

    private void Back()
    {
        if (_step > 1)
            _step--;
    }

    private Task Finish()
    {
        var columns = PreviewColumnCount();
        var formats = Enumerable.Range(0, columns).Select(FormatFor).ToList();

        var result = new SpreadsheetTextToColumnsResult
        {
            Options = BuildOptions(),
            Formats = formats
        };

        return OnApply.InvokeAsync(result);
    }

    private Task Close() => OnClose.InvokeAsync();
}
