using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.Components.Spreadsheet.Data;

namespace Tempo.Blazor.Components.Spreadsheet.Dialogs;

/// <summary>
/// The custom filter dialog: an operator with up to two operands, combined with a second optional
/// condition via AND/OR. The operator list is adapted to the column kind (text/number/date). Applying
/// produces a <see cref="SpreadsheetColumnFilter"/> with the matching criteria. All text is localized.
/// </summary>
public partial class TmSpreadsheetCustomFilterDialog
{
    private readonly string _idSuffix = Guid.NewGuid().ToString("N")[..8];

    private SpreadsheetFilterOperator _op1;
    private SpreadsheetFilterOperator? _op2;
    private string? _value1a;
    private string? _value1b;
    private string? _value2a;
    private string? _value2b;
    private SpreadsheetFilterJoin _join = SpreadsheetFilterJoin.And;

    /// <summary>The column index the filter applies to.</summary>
    [Parameter] public int ColumnIndex { get; set; }

    /// <summary>The kind of filter (text/number/date).</summary>
    [Parameter] public SpreadsheetFilterKind Kind { get; set; } = SpreadsheetFilterKind.Text;

    /// <summary>Raised when the user applies the custom filter (null clears it).</summary>
    [Parameter] public EventCallback<SpreadsheetColumnFilter?> OnApply { get; set; }

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

    private string FirstOperatorDomId => $"spreadsheet-custom-filter-op-{_idSuffix}";

    /// <summary>
    /// Numeric and date criteria take a numeric keyboard on a phone; a text filter keeps the text one.
    /// </summary>
    private string ValueInputMode => Kind == SpreadsheetFilterKind.Text ? "text" : "decimal";

    private SpreadsheetFilterOperator[] Operators => Kind switch
    {
        SpreadsheetFilterKind.Number =>
        [
            SpreadsheetFilterOperator.Equals, SpreadsheetFilterOperator.NotEquals,
            SpreadsheetFilterOperator.GreaterThan, SpreadsheetFilterOperator.GreaterThanOrEqual,
            SpreadsheetFilterOperator.LessThan, SpreadsheetFilterOperator.LessThanOrEqual,
            SpreadsheetFilterOperator.Between, SpreadsheetFilterOperator.NotBetween,
            SpreadsheetFilterOperator.Top10, SpreadsheetFilterOperator.AboveAverage, SpreadsheetFilterOperator.BelowAverage
        ],
        SpreadsheetFilterKind.Date =>
        [
            SpreadsheetFilterOperator.Equals, SpreadsheetFilterOperator.NotEquals,
            SpreadsheetFilterOperator.GreaterThan, SpreadsheetFilterOperator.LessThan,
            SpreadsheetFilterOperator.Between,
            SpreadsheetFilterOperator.Today, SpreadsheetFilterOperator.ThisMonth, SpreadsheetFilterOperator.ThisYear
        ],
        _ =>
        [
            SpreadsheetFilterOperator.Contains, SpreadsheetFilterOperator.NotContains,
            SpreadsheetFilterOperator.BeginsWith, SpreadsheetFilterOperator.EndsWith,
            SpreadsheetFilterOperator.Equals, SpreadsheetFilterOperator.NotEquals
        ]
    };

    protected override void OnParametersSet()
    {
        if (!Operators.Contains(_op1))
            _op1 = Operators[0];
    }

    private static bool NeedsValue(SpreadsheetFilterOperator op) => op is not
        (SpreadsheetFilterOperator.AboveAverage
        or SpreadsheetFilterOperator.BelowAverage
        or SpreadsheetFilterOperator.Today
        or SpreadsheetFilterOperator.Yesterday
        or SpreadsheetFilterOperator.Tomorrow
        or SpreadsheetFilterOperator.ThisWeek
        or SpreadsheetFilterOperator.ThisMonth
        or SpreadsheetFilterOperator.ThisYear);

    private static bool NeedsSecondValue(SpreadsheetFilterOperator op)
        => op is SpreadsheetFilterOperator.Between or SpreadsheetFilterOperator.NotBetween;

    private static string OperatorKey(SpreadsheetFilterOperator op) => $"TmSpreadsheet_FilterOp_{op}";

    private SpreadsheetFilterCondition BuildCondition(SpreadsheetFilterOperator op, string? a, string? b)
        => new() { Operator = op, Operand = a, Operand2 = b };

    private Task Apply()
    {
        var conditions = new List<SpreadsheetFilterCondition> { BuildCondition(_op1, _value1a, _value1b) };
        if (_op2 is { } op2)
            conditions.Add(BuildCondition(op2, _value2a, _value2b));

        var filter = new SpreadsheetColumnFilter
        {
            ColumnIndex = ColumnIndex,
            Kind = Kind,
            Criteria = new SpreadsheetFilterCriteria { Join = _join, Conditions = conditions }
        };

        return OnApply.InvokeAsync(filter);
    }

    private Task Close() => OnClose.InvokeAsync();

}
