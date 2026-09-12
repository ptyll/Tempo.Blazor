using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Tempo.Blazor.Components.Spreadsheet.Enums;
using Tempo.Blazor.Components.Spreadsheet.Models;

namespace Tempo.Blazor.Components.Spreadsheet;

public partial class TmSpreadsheetFormatCellsDialog
{
    [Parameter, EditorRequired] public SpreadsheetCellStyle Style { get; set; } = new();
    [Parameter, EditorRequired] public EventCallback<SpreadsheetCellStyle> OnApply { get; set; }
    [Parameter, EditorRequired] public EventCallback OnClose { get; set; }

    private SpreadsheetCellStyle _workingStyle = new();

    // Number tab state
    private string _selectedCategory = "General";
    private int _numDecimalPlaces = 2;
    private bool _numThousands = false;
    private string _currencySymbol = "Kč";

    // Border tab state
    private SpreadsheetBorderStyle _borderStyle = SpreadsheetBorderStyle.Thin;
    private string _borderColor = "#000000";

    private static readonly string[] _numberCategories =
    [
        "General", "Number", "Currency", "Accounting", "Date", "Percentage", "Custom", "Text"
    ];

    private static readonly string[] _dateFormats =
    [
        "d.M.yyyy", "dd.MM.yyyy", "d. MMMM yyyy", "MMMM yyyy", "yyyy-MM-dd", "d.M.yy"
    ];

    private static readonly string[] _customFormats =
    [
        "General", "0", "0.00", "#,##0", "#,##0.00", "0%", "0.00%", "0.00E+00",
        "# ?/?", "# ??/??", "d.M.yyyy", "dd.MM.yyyy", "H:mm", "H:mm:ss",
        "d.M.yyyy H:mm", "@", "\"Kč\"#,##0.00", "_-\"Kč\"* #,##0.00_-"
    ];

    private static readonly string[] _fontFamilies =
    [
        "Arial", "Calibri", "Courier New", "Georgia", "Segoe UI",
        "Times New Roman", "Verdana", "Comic Sans MS", "Impact", "Trebuchet MS"
    ];

    private static readonly string[] _colorPalette =
    [
        "#000000", "#404040", "#808080", "#bfbfbf", "#ffffff",
        "#c00000", "#ff0000", "#ffc000", "#ffff00", "#92d050",
        "#00b050", "#00b0f0", "#0070c0", "#002060", "#7030a0",
        "#ff6600", "#ff9900", "#ffcc00", "#99ff00", "#33cc33",
        "#00cccc", "#3399ff", "#0000ff", "#6600cc", "#cc00cc",
        "#ffcccc", "#ffe0cc", "#fff2cc", "#e2efda", "#dae3f3",
        "#d6e4f7", "#d9e1f2", "#e8daef", "#fce4d6", "#fce5cd",
        "#fff2cc", "#ebf3da", "#d9e8fb", "#deebf7", "#ededed"
    ];

    private string _previewText = "AaBbCcYyZz";

    protected override void OnParametersSet()
    {
        _workingStyle = Style.Clone();
        InitNumberTabFromFormat(_workingStyle.NumberFormat);
    }

    private void InitNumberTabFromFormat(string format)
    {
        if (format is "General" or "")
        {
            _selectedCategory = "General";
        }
        else if (format.Contains("yyyy") || format.Contains("MM") || format.Contains("dd"))
        {
            _selectedCategory = "Date";
        }
        else if (format.EndsWith('%'))
        {
            _selectedCategory = "Percentage";
            _numDecimalPlaces = format.Contains('.') ? format.Length - format.IndexOf('.') - 2 : 0;
        }
        else if (format.Contains("Kč") || format.Contains('$') || format.Contains('€'))
        {
            _selectedCategory = "Currency";
        }
        else if (format == "@")
        {
            _selectedCategory = "Text";
        }
        else
        {
            _selectedCategory = "Custom";
        }
    }

    private void SelectCategory(string cat)
    {
        _selectedCategory = cat;
        UpdateNumberFormat();
    }

    private void OnCurrencySymbolChange(Microsoft.AspNetCore.Components.ChangeEventArgs e)
    {
        _currencySymbol = e.Value?.ToString() ?? "Kč";
        UpdateNumberFormat();
    }

    private void ClearBackgroundColor()
    {
        _workingStyle.BackgroundColor = "transparent";
    }

    private void UpdateNumberFormat()
    {
        _workingStyle.NumberFormat = _selectedCategory switch
        {
            "General" => "General",
            "Number" => BuildNumberFormat(_numDecimalPlaces, _numThousands, null),
            "Currency" => BuildNumberFormat(_numDecimalPlaces, true, _currencySymbol),
            "Accounting" => $"_-\"{_currencySymbol}\"* {BuildNumberFormat(_numDecimalPlaces, true, null)}_-",
            "Percentage" => _numDecimalPlaces == 0 ? "0%" : $"0.{"0".PadRight(_numDecimalPlaces, '0')}%",
            "Text" => "@",
            _ => _workingStyle.NumberFormat
        };
    }

    private static string BuildNumberFormat(int decimals, bool thousands, string? currency)
    {
        var intPart = thousands ? "#,##0" : "0";
        var decPart = decimals > 0 ? "." + new string('0', decimals) : "";
        var fmt = intPart + decPart;
        if (currency is not null)
            fmt = $"\"{currency}\" {fmt}";
        return fmt;
    }

    private void ApplyOutsideBorder()
    {
        var border = new SpreadsheetBorder(_borderStyle, _borderColor);
        _workingStyle.BorderTop = border;
        _workingStyle.BorderRight = new SpreadsheetBorder(_borderStyle, _borderColor);
        _workingStyle.BorderBottom = new SpreadsheetBorder(_borderStyle, _borderColor);
        _workingStyle.BorderLeft = new SpreadsheetBorder(_borderStyle, _borderColor);
    }

    private void ApplyAllBorders()
    {
        ApplyOutsideBorder();
    }

    private void ApplyThickBorder()
    {
        _workingStyle.BorderTop = new SpreadsheetBorder(SpreadsheetBorderStyle.Thick, _borderColor);
        _workingStyle.BorderRight = new SpreadsheetBorder(SpreadsheetBorderStyle.Thick, _borderColor);
        _workingStyle.BorderBottom = new SpreadsheetBorder(SpreadsheetBorderStyle.Thick, _borderColor);
        _workingStyle.BorderLeft = new SpreadsheetBorder(SpreadsheetBorderStyle.Thick, _borderColor);
    }

    private SpreadsheetBorder ToggleBorder(SpreadsheetBorder current)
        => current.Style == SpreadsheetBorderStyle.None
            ? new SpreadsheetBorder(_borderStyle, _borderColor)
            : new SpreadsheetBorder(SpreadsheetBorderStyle.None, current.Color);

    private void ToggleBorderTop() => _workingStyle.BorderTop = ToggleBorder(_workingStyle.BorderTop);
    private void ToggleBorderBottom() => _workingStyle.BorderBottom = ToggleBorder(_workingStyle.BorderBottom);
    private void ToggleBorderLeft() => _workingStyle.BorderLeft = ToggleBorder(_workingStyle.BorderLeft);
    private void ToggleBorderRight() => _workingStyle.BorderRight = ToggleBorder(_workingStyle.BorderRight);

    private string GetPreviewStyle()
    {
        var sb = new System.Text.StringBuilder();
        sb.Append($"font-family: {_workingStyle.FontFamily}; font-size: {_workingStyle.FontSize}pt;");
        if (_workingStyle.Bold) sb.Append(" font-weight: bold;");
        if (_workingStyle.Italic) sb.Append(" font-style: italic;");
        var decorations = new System.Text.StringBuilder();
        if (_workingStyle.DoubleUnderline) decorations.Append(" underline");
        else if (_workingStyle.Underline) decorations.Append(" underline");
        if (_workingStyle.StrikeThrough) decorations.Append(" line-through");
        if (decorations.Length > 0)
        {
            sb.Append($" text-decoration:{decorations};");
            if (_workingStyle.DoubleUnderline) sb.Append(" text-decoration-style: double;");
        }
        if (_workingStyle.ForeColor is not null and not "#000000")
            sb.Append($" color: {_workingStyle.ForeColor};");
        if (_workingStyle.BackgroundColor is not null and not "transparent")
            sb.Append($" background-color: {_workingStyle.BackgroundColor};");
        return sb.ToString();
    }

    private void Apply()
    {
        OnApply.InvokeAsync(_workingStyle);
    }

    private void OnKeyDown(KeyboardEventArgs e)
    {
        if (e.Key == "Escape")
            OnClose.InvokeAsync();
        else if (e.Key == "Enter" && e.CtrlKey)
            Apply();
    }

    // The dialog's Ctrl+Enter -> Apply shortcut lives on the root; the chrome
    // (tabs, footer buttons) isolates its keydowns so Enter/Ctrl+Enter on a
    // button runs only the button's native click. Escape is re-handled here
    // so it still closes while focus sits on those controls.
    private void OnChromeKeyDown(KeyboardEventArgs e)
    {
        if (e.Key == "Escape")
            OnClose.InvokeAsync();
    }
}
